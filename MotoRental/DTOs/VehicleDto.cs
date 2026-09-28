using MotoRental.Entities;

namespace MotoRental.DTOs;

public class VehicleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    public static VehicleDto FromEntity(Vehicle vehicle)
    {
        return new VehicleDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Description = vehicle.Description,
            Price = vehicle.Price,
            Status = vehicle.Status.ToString(),
            ImageUrl = vehicle.ImageUrl
        };
    }
}
