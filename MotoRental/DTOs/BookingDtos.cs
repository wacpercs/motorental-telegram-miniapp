using System.ComponentModel.DataAnnotations;
using MotoRental.Entities;

namespace MotoRental.DTOs;

public class CreateBookingRequestDto
{
    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "TelegramId должен быть положительным числом.")]
    public long TelegramId { get; set; }

    [MaxLength(64)]
    public string? Username { get; set; }

    [MaxLength(128)]
    public string? FirstName { get; set; }

    [MaxLength(128)]
    public string? LastName { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Необходимо выбрать технику.")]
    public int VehicleId { get; set; }

    [Required]
    public DateTime BookingDate { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }
}

public class BookingResponseDto
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    public static BookingResponseDto FromEntity(Booking booking)
    {
        return new BookingResponseDto
        {
            Id = booking.Id,
            VehicleId = booking.VehicleId,
            VehicleName = booking.Vehicle?.Name ?? "Мототехника",
            BookingDate = booking.BookingDate,
            Status = booking.Status.ToString(),
            TotalPrice = booking.TotalPrice,
            Comment = booking.Comment,
            CreatedAt = booking.CreatedAt
        };
    }
}

public class TimeSlotDto
{
    public DateTime SlotTime { get; set; }
    public string FormattedTime { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}
