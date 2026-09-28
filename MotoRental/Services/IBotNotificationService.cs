using MotoRental.Entities;

namespace MotoRental.Services;

public class BroadcastResult
{
    public int Total { get; set; }
    public int Success { get; set; }
    public int Blocked { get; set; }
    public int Failed { get; set; }
    public TimeSpan Duration { get; set; }
}

public interface IBotNotificationService
{
    /// <summary>
    /// Уведомление администратора о новой заявке с Inline-кнопками Подтвердить / Отклонить
    /// </summary>
    Task NotifyAdminNewBookingAsync(Booking booking, CancellationToken cancellationToken = default);

    /// <summary>
    /// Уведомление клиента об изменении статуса его бронирования
    /// </summary>
    Task NotifyClientBookingStatusAsync(long clientTelegramId, int bookingId, BookingStatus status, string vehicleName, DateTime bookingDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовая рассылка всем клиентам из таблицы Clients с задержкой Task.Delay
    /// </summary>
    Task<BroadcastResult> BroadcastMessageAsync(string message, CancellationToken cancellationToken = default);
}
