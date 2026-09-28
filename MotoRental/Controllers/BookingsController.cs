using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MotoRental.Data;
using MotoRental.DTOs;
using MotoRental.Entities;
using MotoRental.Services;

namespace MotoRental.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly RentalDbContext _db;
    private readonly IBotNotificationService _notificationService;
    private readonly ILogger<BookingsController> _logger;

    public BookingsController(
        RentalDbContext db,
        IBotNotificationService notificationService,
        ILogger<BookingsController> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Создание нового бронирования из Telegram Mini App
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking([FromBody] CreateBookingRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // 1. Проверяем наличие техники
            var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId);
            if (vehicle == null)
            {
                return NotFound(new { error = $"Техника с ID {request.VehicleId} не найдена." });
            }

            if (vehicle.Status != VehicleStatus.Available)
            {
                return BadRequest(new { error = "Выбранная техника временно недоступна для бронирования." });
            }

            // 2. Валидация времени слота
            var bookingDateUtc = request.BookingDate.Kind == DateTimeKind.Utc
                ? request.BookingDate
                : request.BookingDate.ToUniversalTime();

            if (bookingDateUtc < DateTime.UtcNow.AddMinutes(-5))
            {
                return BadRequest(new { error = "Нельзя забронировать прошедшее время." });
            }

            // 3. Проверка конфликтов (занят ли слот)
            var slotWindowStart = bookingDateUtc.AddMinutes(-45);
            var slotWindowEnd = bookingDateUtc.AddMinutes(45);

            bool isSlotTaken = await _db.Bookings.AnyAsync(b =>
                b.VehicleId == request.VehicleId &&
                (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) &&
                b.BookingDate >= slotWindowStart &&
                b.BookingDate <= slotWindowEnd);

            if (isSlotTaken)
            {
                return Conflict(new { error = "Выбранный временной слот уже занят. Пожалуйста, выберите другое время." });
            }

            // 4. Поиск или создание клиента (Upsert)
            var client = await _db.Clients.FirstOrDefaultAsync(c => c.TelegramId == request.TelegramId);
            if (client == null)
            {
                client = new Client
                {
                    TelegramId = request.TelegramId,
                    Username = request.Username,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Clients.Add(client);
                await _db.SaveChangesAsync();
            }
            else
            {
                // Актуализация профиля
                client.Username = request.Username ?? client.Username;
                client.FirstName = request.FirstName ?? client.FirstName;
                client.LastName = request.LastName ?? client.LastName;
            }

            // 5. Создание записи бронирования
            var booking = new Booking
            {
                ClientId = client.Id,
                Client = client,
                VehicleId = vehicle.Id,
                Vehicle = vehicle,
                BookingDate = bookingDateUtc,
                Status = BookingStatus.Pending,
                TotalPrice = vehicle.Price,
                Comment = request.Comment,
                CreatedAt = DateTime.UtcNow
            };

            _db.Bookings.Add(booking);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Создана бронь #{BookingId} для клиента {TelegramId} на технику {VehicleId}.",
                booking.Id, request.TelegramId, vehicle.Id);

            // 6. Асинхронное уведомление администратора в Telegram с кнопками
            await _notificationService.NotifyAdminNewBookingAsync(booking);

            var responseDto = BookingResponseDto.FromEntity(booking);
            return CreatedAtAction(nameof(GetBookingById), new { id = booking.Id }, responseDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при оформлении бронирования.");
            return StatusCode(500, new { error = "Произошла внутренняя ошибка сервера при создании брони." });
        }
    }

    /// <summary>
    /// Получить статус конкретного бронирования по ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponseDto>> GetBookingById(int id)
    {
        try
        {
            var booking = await _db.Bookings
                .Include(b => b.Vehicle)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return NotFound(new { error = $"Бронь #{id} не найдена." });
            }

            return Ok(BookingResponseDto.FromEntity(booking));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении брони #{Id}.", id);
            return StatusCode(500, new { error = "Внутренняя ошибка сервера." });
        }
    }

    /// <summary>
    /// История бронирований текущего пользователя
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<BookingResponseDto>>> GetMyBookings([FromQuery] long telegramId)
    {
        if (telegramId <= 0)
        {
            return BadRequest(new { error = "Некорректный TelegramId." });
        }

        try
        {
            var bookings = await _db.Bookings
                .Include(b => b.Vehicle)
                .Include(b => b.Client)
                .AsNoTracking()
                .Where(b => b.Client.TelegramId == telegramId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => BookingResponseDto.FromEntity(b))
                .ToListAsync();

            return Ok(bookings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке истории бронирований для TelegramId {TelegramId}.", telegramId);
            return StatusCode(500, new { error = "Не удалось загрузить историю бронирований." });
        }
    }
}
