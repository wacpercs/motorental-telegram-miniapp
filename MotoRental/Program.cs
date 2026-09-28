using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MotoRental.Configuration;
using MotoRental.Data;
using MotoRental.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;

var builder = WebApplication.CreateBuilder(args);

// 1. Конфигурация приложения
builder.Services.Configure<BotConfiguration>(
    builder.Configuration.GetSection(BotConfiguration.SectionName));

// 2. База данных PostgreSQL
builder.Services.AddDbContext<RentalDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

// 3. Регистрация TelegramBotClient через HttpClientFactory
builder.Services.AddHttpClient("TelegramBotClient")
    .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
    {
        var config = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
        var token = !string.IsNullOrWhiteSpace(config.BotToken) ? config.BotToken : "DUMMY_TOKEN_FOR_STARTUP";
        return new TelegramBotClient(token, httpClient);
    });

// 4. Сервисы бота
builder.Services.AddSingleton<IBotNotificationService, BotNotificationService>();
builder.Services.AddSingleton<IUpdateHandler, BotUpdateHandler>();
builder.Services.AddHostedService<TelegramPollingService>();

// 5. Контроллеры и Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Настройка CORS для обращения из Telegram Mini App
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Автоматическая инициализация схемы БД и seed-данных при старте
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();
    db.Database.EnsureCreated();
}
catch (Exception ex)
{
    app.Logger.LogWarning("Автоматическая инициализация БД отложена: {Message}", ex.Message);
}

// Настройка HTTP пайплайна
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();

// Статические файлы фронтенда Telegram Mini App (из каталога wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

app.Run();
