using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MotoRental.Configuration;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace MotoRental.Services;

public class TelegramPollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUpdateHandler _updateHandler;
    private readonly BotConfiguration _config;
    private readonly ILogger<TelegramPollingService> _logger;

    public TelegramPollingService(
        ITelegramBotClient botClient,
        IUpdateHandler updateHandler,
        IOptions<BotConfiguration> config,
        ILogger<TelegramPollingService> logger)
    {
        _botClient = botClient;
        _updateHandler = updateHandler;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_config.BotToken) || _config.BotToken == "YOUR_TELEGRAM_BOT_TOKEN_HERE")
        {
            _logger.LogWarning("Telegram BotToken не сконфигурирован в appsettings.json. Фоновый опрос обновлений не запущен.");
            return;
        }

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("Успешное подключение к Telegram Bot API: @{Username} (ID: {Id})", me.Username, me.Id);

            if (_config.UseWebhook)
            {
                if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
                {
                    _logger.LogError("Режим Webhook включен, но WebhookUrl не указан в конфигурации!");
                    return;
                }

                _logger.LogInformation("Установка Webhook на адрес: {WebhookUrl}", _config.WebhookUrl);
                await _botClient.SetWebhook(
                    url: _config.WebhookUrl,
                    secretToken: _config.SecretToken,
                    allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    dropPendingUpdates: true,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Webhook успешно зарегистрирован в Telegram.");
                // В режиме Webhook фоновый поток просто ожидает завершения работы приложения
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            else
            {
                // Режим Long Polling: удаляем webhook, если он был установлен ранее
                await _botClient.DeleteWebhook(dropPendingUpdates: true, cancellationToken: stoppingToken);
                _logger.LogInformation("Предыдущие вебхуки сброшены. Запуск Long Polling...");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    DropPendingUpdates = true
                };

                _botClient.StartReceiving(
                    updateHandler: _updateHandler,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Telegram Bot Long Polling успешно запущен и слушает входящие события.");

                // Удерживаем BackgroundService активным
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Остановка фонового сервиса Telegram Bot...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Критическая ошибка при инициализации или работе Telegram Bot.");
        }
    }
}
