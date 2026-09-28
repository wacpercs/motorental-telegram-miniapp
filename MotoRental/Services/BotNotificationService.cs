using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MotoRental.Configuration;
using MotoRental.Data;
using MotoRental.Entities;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MotoRental.Services;

public class BotNotificationService : IBotNotificationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly BotConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotNotificationService> _logger;

    public BotNotificationService(
        ITelegramBotClient botClient,
        IOptions<BotConfiguration> config,
        IServiceScopeFactory scopeFactory,
        ILogger<BotNotificationService> logger)
    {
        _botClient = botClient;
        _config = config.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task NotifyAdminNewBookingAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        if (_config.AdminTelegramId <= 0)
        {
            _logger.LogWarning("AdminTelegramId не задан или некорректен в конфигурации. Уведомление пропущено.");
            return;
        }

        try
        {
            var clientName = !string.IsNullOrWhiteSpace(booking.Client.Username)
                ? $"@{booking.Client.Username}"
                : $"{booking.Client.FirstName} {booking.Client.LastName}".Trim();

            if (string.IsNullOrWhiteSpace(clientName))
            {
                clientName = $"ID: {booking.Client.TelegramId}";
            }

            // Формат по ТЗ: «Новая бронь: Квадроцикл, [Дата], [Клиент]»
            var header = $"🔔 <b>Новая бронь: {booking.Vehicle.Name}, {booking.BookingDate:dd.MM.yyyy HH:mm}, {clientName}</b>";

            var messageText =
                $"{header}\n\n" +
                $"🆔 <b>Номер заявки:</b> #{booking.Id}\n" +
                $"🏍 <b>Техника:</b> {booking.Vehicle.Name}\n" +
                $"📅 <b>Слот:</b> {booking.BookingDate:dd.MM.yyyy HH:mm} (UTC)\n" +
                $"👤 <b>Клиент:</b> {clientName} (Telegram ID: <code>{booking.Client.TelegramId}</code>)\n" +
                $"💰 <b>Стоимость:</b> {booking.TotalPrice:N2} ₽";

            if (!string.IsNullOrWhiteSpace(booking.Comment))
            {
                messageText += $"\n💬 <b>Комментарий:</b> {booking.Comment}";
            }

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить", $"confirm_{booking.Id}"),
                    InlineKeyboardButton.WithCallbackData("❌ Отклонить", $"reject_{booking.Id}")
                }
            });

            await _botClient.SendMessage(
                chatId: _config.AdminTelegramId,
                text: messageText,
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Уведомление о новой брони #{BookingId} доставлено администратору {AdminId}.", booking.Id, _config.AdminTelegramId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка отправки уведомления администратору о брони #{BookingId}.", booking.Id);
        }
    }

    public async Task NotifyClientBookingStatusAsync(
        long clientTelegramId,
        int bookingId,
        BookingStatus status,
        string vehicleName,
        DateTime bookingDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string statusHeader = status == BookingStatus.Confirmed
                ? "🎉 <b>Ваша бронь подтверждена администратором!</b>"
                : "❌ <b>Ваша бронь отклонена администратором.</b>";

            string statusDescription = status == BookingStatus.Confirmed
                ? "Ждем вас на базе проката к назначенному времени. При себе необходимо иметь документ, удостоверяющий личность."
                : "К сожалению, выбранный слот времени сейчас недоступен. Пожалуйста, выберите другое удобное время в каталоге техники.";

            var message = $"{statusHeader}\n\n" +
                          $"{statusDescription}\n\n" +
                          $"📋 <b>Детали заявки:</b>\n" +
                          $"• Номер: #{bookingId}\n" +
                          $"• Техника: {vehicleName}\n" +
                          $"• Дата и время: {bookingDate:dd.MM.yyyy HH:mm} (UTC)";

            await _botClient.SendMessage(
                chatId: clientTelegramId,
                text: message,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Клиент {ClientId} уведомлен о статусе заявки #{BookingId}: {Status}.", clientTelegramId, bookingId, status);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            _logger.LogWarning("Клиент {ClientId} заблокировал бота, уведомление не доставлено.", clientTelegramId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при уведомлении клиента {ClientId} по заявке #{BookingId}.", clientTelegramId, bookingId);
        }
    }

    public async Task<BroadcastResult> BroadcastMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new BroadcastResult();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();

        var clientTelegramIds = await db.Clients
            .AsNoTracking()
            .Where(c => c.TelegramId > 0)
            .Select(c => c.TelegramId)
            .Distinct()
            .ToListAsync(cancellationToken);

        result.Total = clientTelegramIds.Count;
        _logger.LogInformation("Запуск массовой рассылки для {Count} пользователей...", result.Total);

        foreach (var telegramId in clientTelegramIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await _botClient.SendMessage(
                    chatId: telegramId,
                    text: message,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);

                result.Success++;

                // Задержка Task.Delay для обхода лимитов Telegram Bot API (до ~30 сообщений/сек)
                await Task.Delay(50, cancellationToken);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                result.Blocked++;
                _logger.LogDebug("Клиент {TelegramId} заблокировал бота.", telegramId);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                _logger.LogWarning("Лимит запросов Telegram (429). Ожидание {RetryAfter} сек...", retryAfter);
                await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken);

                // Повторная попытка после ожидания
                try
                {
                    await _botClient.SendMessage(
                        chatId: telegramId,
                        text: message,
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);
                    result.Success++;
                }
                catch
                {
                    result.Failed++;
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.LogError(ex, "Ошибка рассылки клиенту {TelegramId}.", telegramId);
            }
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        _logger.LogInformation("Рассылка завершена за {Elapsed}. Успешно: {Success}, Заблокировано: {Blocked}, Ошибок: {Failed}.",
            sw.Elapsed, result.Success, result.Blocked, result.Failed);

        return result;
    }
}
