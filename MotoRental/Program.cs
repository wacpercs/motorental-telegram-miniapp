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

// 2. База данных (Авто-определение Railway DATABASE_URL, либо локальный SQLite / PostgreSQL)
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
var useSqlite = builder.Configuration.GetValue<bool>("UseSqlite", string.IsNullOrWhiteSpace(databaseUrl));

builder.Services.AddDbContext<RentalDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(databaseUrl))
    {
        options.UseNpgsql(ConvertPostgresUrlToConnectionString(databaseUrl));
    }
    else if (useSqlite)
    {
        var sqliteConn = builder.Configuration.GetConnectionString("SqliteConnection") ?? "Data Source=motorental.db";
        options.UseSqlite(sqliteConn);
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        options.UseNpgsql(connectionString);
    }
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

// Преобразование стандартного URL подключения Railway/Heroku к формату Npgsql
static string ConvertPostgresUrlToConnectionString(string databaseUrl)
{
    if (databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
    return databaseUrl;
}
