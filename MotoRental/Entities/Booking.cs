using System.ComponentModel.DataAnnotations;

namespace MotoRental.Entities;

public class Booking
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary>
    /// Выбранная дата и время слота бронирования (UTC)
    /// </summary>
    public DateTime BookingDate { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>
    /// Сумма бронирования на момент оформления
    /// </summary>
    public decimal TotalPrice { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
