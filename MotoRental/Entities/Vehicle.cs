using System.ComponentModel.DataAnnotations;

namespace MotoRental.Entities;

public class Vehicle
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Стоимость проката (руб./час или фиксированная цена слота)
    /// </summary>
    public decimal Price { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Available;

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    // Navigation property
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
