using System.ComponentModel.DataAnnotations;

namespace MotoRental.Entities;

public class Client
{
    public int Id { get; set; }

    /// <summary>
    /// Telegram User ID (уникальный идентификатор пользователя в Telegram)
    /// </summary>
    public long TelegramId { get; set; }

    [MaxLength(64)]
    public string? Username { get; set; }

    [MaxLength(128)]
    public string? FirstName { get; set; }

    [MaxLength(128)]
    public string? LastName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
