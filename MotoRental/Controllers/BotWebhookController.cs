using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MotoRental.Configuration;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace MotoRental.Controllers;

[ApiController]
[Route("api/bot")]
public class BotWebhookController : ControllerBase
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUpdateHandler _updateHandler;
    private readonly BotConfiguration _config;
    private readonly ILogger<BotWebhookController> _logger;

    public BotWebhookController(
        ITelegramBotClient botClient,
        IUpdateHandler updateHandler,
        IOptions<BotConfiguration> config,
        ILogger<BotWebhookController> logger)
    {
        _botClient = botClient;
        _updateHandler = updateHandler;
        _config = config.Value;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> PostWebhook(
        [FromBody] Update update,
        [FromHeader(Name = "X-Telegram-Bot-Api-Secret-Token")] string? secretToken,
        CancellationToken cancellationToken)
    {
        // Верификация секретного токена, если он задан в конфигурации
        if (!string.IsNullOrWhiteSpace(_config.SecretToken) && _config.SecretToken != secretToken)
        {
            _logger.LogWarning("Отклонен запрос Webhook с неверным секретным токеном.");
            return Forbid();
        }

        try
        {
            await _updateHandler.HandleUpdateAsync(_botClient, update, cancellationToken);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке Webhook обновления.");
            return Ok(); // Telegram требует ответ 200 OK, чтобы не слать повторы бесконечно
        }
    }
}
