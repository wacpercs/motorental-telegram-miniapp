namespace MotoRental.Configuration;

public class BotConfiguration
{
    public const string SectionName = "BotConfiguration";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// TelegramId администратора для получения уведомлений и выполнения рассылок
    /// </summary>
    public long AdminTelegramId { get; set; }

    /// <summary>
    /// URL веб-приложения Telegram Mini App
    /// </summary>
    public string MiniAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Флаг режима работы: false - Long Polling (локальная разработка), true - Webhook
    /// </summary>
    public bool UseWebhook { get; set; } = false;

    /// <summary>
    /// Публичный URL для Webhook (например, через ngrok: https://xyz.ngrok-free.app/api/bot/webhook)
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Секретный токен для верификации запросов Webhook от Telegram
    /// </summary>
    public string? SecretToken { get; set; }
}
