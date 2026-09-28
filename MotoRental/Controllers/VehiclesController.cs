using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotoRental.Data;
using MotoRental.DTOs;
using MotoRental.Entities;

namespace MotoRental.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehiclesController : ControllerBase
{
    private readonly RentalDbContext _db;
    private readonly ILogger<VehiclesController> _logger;

    public VehiclesController(RentalDbContext db, ILogger<VehiclesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Получить каталог техники для витрины Mini App
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VehicleDto>>> GetVehicles([FromQuery] bool onlyAvailable = true)
    {
        try
        {
            var query = _db.Vehicles.AsNoTracking();

            if (onlyAvailable)
            {
                query = query.Where(v => v.Status == VehicleStatus.Available);
            }

            var vehicles = await query
                .OrderBy(v => v.Id)
                .Select(v => VehicleDto.FromEntity(v))
                .ToListAsync();

            return Ok(vehicles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении списка техники.");
            return StatusCode(500, new { error = "Не удалось загрузить каталог техники." });
        }
    }

    /// <summary>
    /// Получить детальную информацию о единице техники
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<VehicleDto>> GetVehicle(int id)
    {
        try
        {
            var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle == null)
            {
                return NotFound(new { error = $"Техника с ID {id} не найдена." });
            }

            return Ok(VehicleDto.FromEntity(vehicle));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении техники #{Id}.", id);
            return StatusCode(500, new { error = "Внутренняя ошибка сервера." });
        }
    }

    /// <summary>
    /// Получить доступные временные слоты для техники на указанную дату
    /// </summary>
    [HttpGet("{id:int}/slots")]
    public async Task<ActionResult<IEnumerable<TimeSlotDto>>> GetAvailableSlots(int id, [FromQuery] DateTime? date)
    {
        try
        {
            var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle == null)
            {
                return NotFound(new { error = $"Техника с ID {id} не найдена." });
            }

            var targetDate = (date ?? DateTime.UtcNow).Date;

            // Рабочие часы проката: с 10:00 до 20:00 (слоты по 1 часу)
            var startUtc = DateTime.SpecifyKind(targetDate.AddHours(10), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(targetDate.AddHours(20), DateTimeKind.Utc);

            // Получаем активные брони на этот день
            var bookedDates = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.VehicleId == id &&
                            b.BookingDate >= startUtc &&
                            b.BookingDate <= endUtc &&
                            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
                .Select(b => b.BookingDate)
                .ToListAsync();

            var slots = new List<TimeSlotDto>();
            for (var time = startUtc; time <= endUtc; time = time.AddHours(1))
            {
                // Слот занят, если есть пересечение в окне 45 минут
                bool isOccupied = bookedDates.Any(b => Math.Abs((b - time).TotalMinutes) < 45);
                bool isPast = time < DateTime.UtcNow;

                slots.Add(new TimeSlotDto
                {
                    SlotTime = time,
                    FormattedTime = time.ToString("HH:mm"),
                    IsAvailable = !isOccupied && !isPast
                });
            }

            return Ok(slots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при расчете слотов для техники #{Id}.", id);
            return StatusCode(500, new { error = "Не удалось загрузить слоты." });
        }
    }
}
