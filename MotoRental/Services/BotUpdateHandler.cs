using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MotoRental.Configuration;
using MotoRental.Data;
using MotoRental.Entities;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MotoRental.Services;

public class BotUpdateHandler : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotConfiguration _config;
    private readonly IBotNotificationService _notificationService;
    private readonly ILogger<BotUpdateHandler> _logger;

    // Временное хранение состояния ожидания ввода текста рассылки от администратора
    private static readonly ConcurrentDictionary<long, string> PendingAdminActions = new();

    public BotUpdateHandler(
        IServiceScopeFactory scopeFactory,
        IOptions<BotConfiguration> config,
        IBotNotificationService notificationService,
        ILogger<BotUpdateHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config.Value;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message when update.Message is not null:
                    await HandleMessageAsync(botClient, update.Message, cancellationToken);
                    break;

                case UpdateType.CallbackQuery when update.CallbackQuery is not null:
                    await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
                    break;

                default:
                    _logger.LogDebug("Игнорирование типа обновления: {Type}", update.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при обработке обновления {UpdateId}.", update.Id);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Ошибка в Telegram Bot Polling (источник: {Source}): {Message}", source, exception.Message);
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        if (message.From is null) return;

        var user = message.From;
        var text = message.Text?.Trim() ?? string.Empty;
        var chatId = message.Chat.Id;

        // Синхронизация клиента с базой данных
        await EnsureClientRegisteredAsync(user, cancellationToken);

        bool isAdmin = user.Id == _config.AdminTelegramId;

        // Команда отмены
        if (text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (PendingAdminActions.TryRemove(user.Id, out _))
            {
                await botClient.SendMessage(chatId, "❌ Действие отменено.", cancellationToken: cancellationToken);
                return;
            }
        }

        // Проверка, ожидает ли администратор ввод текста для рассылки
        if (isAdmin && PendingAdminActions.TryGetValue(user.Id, out var action) && action == "awaiting_broadcast_text")
        {
            PendingAdminActions.TryRemove(user.Id, out _);
            await ExecuteBroadcastAsync(botClient, chatId, text, cancellationToken);
            return;
        }

        // Команда /start
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await SendWelcomeMessageAsync(botClient, chatId, user.FirstName, isAdmin, cancellationToken);
            return;
        }

        // Админские команды
        if (isAdmin)
        {
            if (text.StartsWith("/broadcast", StringComparison.OrdinalIgnoreCase))
            {
                var broadcastText = text.Length > 10 ? text[10..].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(broadcastText))
                {
                    PendingAdminActions[user.Id] = "awaiting_broadcast_text";
                    await botClient.SendMessage(
                        chatId,
                        "📢 <b>Режим массовой рассылки:</b>\n\n" +
                        "Отправьте следующим сообщением текст, который получат все клиенты сервиса.\n" +
                        "<i>(Поддерживаются HTML-теги: &lt;b&gt;, &lt;i&gt;, &lt;code&gt; и ссылки).\n" +
                        "Для отмены отправьте /cancel.</i>",
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);
                    return;
                }

                await ExecuteBroadcastAsync(botClient, chatId, broadcastText, cancellationToken);
                return;
            }

            if (text.Equals("/stats", StringComparison.OrdinalIgnoreCase))
            {
                await SendAdminStatsAsync(botClient, chatId, cancellationToken);
                return;
            }

            if (text.Equals("/bookings", StringComparison.OrdinalIgnoreCase))
            {
                await SendAdminRecentBookingsAsync(botClient, chatId, cancellationToken);
                return;
            }
        }

        // Ответ по умолчанию
        await SendDefaultHelpMessageAsync(botClient, chatId, cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data ?? string.Empty;
        var fromUser = callbackQuery.From;

        // Проверка прав администратора
        if (fromUser.Id != _config.AdminTelegramId)
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "⛔ Доступ запрещен. Только администратор может выполнять это действие.",
                showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        // Обработка кнопок Подтвердить / Отклонить бронь
        if (data.StartsWith("confirm_") || data.StartsWith("reject_"))
        {
            await HandleBookingDecisionAsync(botClient, callbackQuery, data, cancellationToken);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
    }

    private async Task HandleBookingDecisionAsync(
        ITelegramBotClient botClient,
        CallbackQuery callbackQuery,
        string data,
        CancellationToken cancellationToken)
    {
        bool isConfirm = data.StartsWith("confirm_");
        var bookingIdStr = isConfirm ? data["confirm_".Length..] : data["reject_".Length..];

        if (!int.TryParse(bookingIdStr, out int bookingId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Некорректный ID заявки.", cancellationToken: cancellationToken);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();

        var booking = await db.Bookings
            .Include(b => b.Client)
            .Include(b => b.Vehicle)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

        if (booking == null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Заявка не найдена в базе данных.", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        // Защита от повторного нажатия
        if (booking.Status != BookingStatus.Pending)
        {
            var currentStatusTitle = booking.Status == BookingStatus.Confirmed ? "уже подтверждена" : "уже отклонена";
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                $"⚠️ Эта бронь {currentStatusTitle}!",
                showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        // Обновляем статус
        var newStatus = isConfirm ? BookingStatus.Confirmed : BookingStatus.Rejected;
        booking.Status = newStatus;
        booking.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        // Всплывающее подтверждение для админа в Telegram
        await botClient.AnswerCallbackQuery(
            callbackQuery.Id,
            isConfirm ? "✅ Заявка успешно подтверждена!" : "❌ Заявка отклонена.",
            cancellationToken: cancellationToken);

        // Редактируем сообщение администратора: фиксируем статус и убираем кнопки
        if (callbackQuery.Message != null)
        {
            var statusBadge = isConfirm
                ? "🟢 <b>СТАТУС: ПОДТВЕРЖДЕНА</b>"
                : "🔴 <b>СТАТУС: ОТКЛОНЕНА</b>";

            var updatedText = callbackQuery.Message.Text +
                              $"\n\n━━━━━━━━━━━━━━━━━━━━\n" +
                              $"{statusBadge}\n" +
                              $"<i>Обработал: @{callbackQuery.From.Username ?? callbackQuery.From.FirstName} в {DateTime.UtcNow:HH:mm:ss} UTC</i>";

            await botClient.EditMessageText(
                chatId: callbackQuery.Message.Chat.Id,
                messageId: callbackQuery.Message.Id,
                text: updatedText,
                parseMode: ParseMode.Html,
                replyMarkup: null, // Убираем Inline-кнопки
                cancellationToken: cancellationToken);
        }

        // Отправляем уведомление клиенту
        await _notificationService.NotifyClientBookingStatusAsync(
            booking.Client.TelegramId,
            booking.Id,
            newStatus,
            booking.Vehicle.Name,
            booking.BookingDate,
            cancellationToken);
    }

    private async Task ExecuteBroadcastAsync(ITelegramBotClient botClient, long adminChatId, string broadcastText, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(
            adminChatId,
            "⏳ <b>Запуск массовой рассылки...</b>\nПожалуйста, подождите завершения процесса.",
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);

        var report = await _notificationService.BroadcastMessageAsync(broadcastText, cancellationToken);

        var reportMessage =
            "📢 <b>Отчет о массовой рассылке:</b>\n\n" +
            $"👥 Всего пользователей в базе: <b>{report.Total}</b>\n" +
            $"✅ Успешно доставлено: <b>{report.Success}</b>\n" +
            $"🚫 Заблокировали бота: <b>{report.Blocked}</b>\n" +
            $"⚠️ Ошибок отправки: <b>{report.Failed}</b>\n" +
            $"⏱ Время выполнения: <b>{report.Duration.TotalSeconds:F1} сек.</b>";

        await botClient.SendMessage(
            adminChatId,
            reportMessage,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task SendAdminStatsAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();

        var clientsCount = await db.Clients.CountAsync(cancellationToken);
        var vehiclesCount = await db.Vehicles.CountAsync(cancellationToken);
        var totalBookings = await db.Bookings.CountAsync(cancellationToken);
        var pendingBookings = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending, cancellationToken);
        var confirmedBookings = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed, cancellationToken);
        var totalRevenue = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .SumAsync(b => (decimal?)b.TotalPrice, cancellationToken) ?? 0m;

        var statsMsg =
            "📊 <b>Статистика сервиса проката мототехники:</b>\n\n" +
            $"👥 Зарегистрированных клиентов: <b>{clientsCount}</b>\n" +
            $"🏍 Техники в гараже: <b>{vehiclesCount}</b>\n" +
            $"📋 Всего заявок: <b>{totalBookings}</b>\n" +
            $"⏳ Ожидают подтверждения: <b>{pendingBookings}</b>\n" +
            $"✅ Подтвержденных заездов: <b>{confirmedBookings}</b>\n" +
            $"💰 Выручка (подтвержденная): <b>{totalRevenue:N2} ₽</b>";

        await botClient.SendMessage(
            chatId,
            statsMsg,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task SendAdminRecentBookingsAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();

        var recentBookings = await db.Bookings
            .Include(b => b.Client)
            .Include(b => b.Vehicle)
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (recentBookings.Count == 0)
        {
            await botClient.SendMessage(chatId, "📭 Заявок на бронирование пока нет.", cancellationToken: cancellationToken);
            return;
        }

        var message = "📋 <b>Последние 5 заявок:</b>\n\n";
        foreach (var b in recentBookings)
        {
            var statusIcon = b.Status switch
            {
                BookingStatus.Pending => "⏳ В ожидании",
                BookingStatus.Confirmed => "✅ Подтверждена",
                BookingStatus.Rejected => "❌ Отклонена",
                BookingStatus.Cancelled => "🚫 Отменена",
                _ => b.Status.ToString()
            };

            var clientName = !string.IsNullOrWhiteSpace(b.Client.Username)
                ? $"@{b.Client.Username}"
                : b.Client.FirstName ?? $"ID:{b.Client.TelegramId}";

            message += $"• <b>#{b.Id}</b> {b.Vehicle.Name}\n" +
                       $"  📅 {b.BookingDate:dd.MM.yyyy HH:mm} | {b.TotalPrice:N0} ₽\n" +
                       $"  👤 {clientName} | {statusIcon}\n\n";
        }

        await botClient.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task EnsureClientRegisteredAsync(User user, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();

        var client = await db.Clients.FirstOrDefaultAsync(c => c.TelegramId == user.Id, cancellationToken);
        if (client == null)
        {
            client = new Client
            {
                TelegramId = user.Id,
                Username = user.Username,
                FirstName = user.FirstName,
                LastName = user.LastName,
                CreatedAt = DateTime.UtcNow
            };
            db.Clients.Add(client);
        }
        else
        {
            client.Username = user.Username;
            client.FirstName = user.FirstName;
            client.LastName = user.LastName;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendWelcomeMessageAsync(ITelegramBotClient botClient, long chatId, string? name, bool isAdmin, CancellationToken cancellationToken)
    {
        var greeting = string.IsNullOrWhiteSpace(name) ? "Приветствуем!" : $"Привет, {name}!";

        var text = $"🔥 <b>{greeting}</b>\n\n" +
                   "Добро пожаловать в сервис проката мототехники!\n" +
                   "Квадроциклы, эндуро и багги готовы к вашему заезду.\n\n" +
                   "👇 Нажмите кнопку ниже, чтобы открыть онлайн-витрину и забронировать технику:";

        if (isAdmin)
        {
            text += "\n\n⭐ <b>Панель Администратора:</b>\n" +
                    "• Новые заявки поступают сюда с кнопками «✅ Подтвердить» и «❌ Отклонить»\n" +
                    "• <code>/broadcast текст</code> — запустить массовую рассылку клиентам\n" +
                    "• <code>/bookings</code> — список последних броней\n" +
                    "• <code>/stats</code> — финансовая и количественная статистика";
        }

        var webAppUrl = !string.IsNullOrWhiteSpace(_config.MiniAppUrl)
            ? _config.MiniAppUrl
            : "https://t.me";

        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[]
            {
                new KeyboardButton("🏍 Открыть витрину техники")
                {
                    WebApp = new WebAppInfo { Url = webAppUrl }
                }
            }
        })
        {
            ResizeKeyboard = true
        };

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithWebApp("🚀 Забронировать технику (Mini App)", new WebAppInfo { Url = webAppUrl })
        });

        await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);

        await botClient.SendMessage(
            chatId: chatId,
            text: "Кнопка витрины также доступна в нижней панели ⬇️",
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task SendDefaultHelpMessageAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var webAppUrl = !string.IsNullOrWhiteSpace(_config.MiniAppUrl)
            ? _config.MiniAppUrl
            : "https://t.me";

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithWebApp("🏍 Открыть каталог техники", new WebAppInfo { Url = webAppUrl })
        });

        await botClient.SendMessage(
            chatId: chatId,
            text: "Для выбора техники и бронирования слота откройте наше приложение:",
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
    }
}
