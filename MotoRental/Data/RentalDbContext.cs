using Microsoft.EntityFrameworkCore;
using MotoRental.Entities;

namespace MotoRental.Data;

public class RentalDbContext : DbContext
{
    public RentalDbContext(DbContextOptions<RentalDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Client Configuration ---
        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("Clients");
            entity.HasKey(c => c.Id);

            // Уникальный индекс по TelegramId для мгновенного поиска и гарантии целостности
            entity.HasIndex(c => c.TelegramId).IsUnique();

            entity.Property(c => c.Username).HasMaxLength(64);
            entity.Property(c => c.FirstName).HasMaxLength(128);
            entity.Property(c => c.LastName).HasMaxLength(128);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // --- Vehicle Configuration ---
        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("Vehicles");
            entity.HasKey(v => v.Id);

            entity.Property(v => v.Name).IsRequired().HasMaxLength(150);
            entity.Property(v => v.Description).HasMaxLength(1000);
            entity.Property(v => v.Price).HasPrecision(18, 2);
            entity.Property(v => v.ImageUrl).HasMaxLength(500);

            entity.Property(v => v.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
        });

        // --- Booking Configuration ---
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(b => b.Id);

            entity.Property(b => b.TotalPrice).HasPrecision(18, 2);
            entity.Property(b => b.Comment).HasMaxLength(500);
            entity.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(b => b.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            // Индекс по дате для быстрой фильтрации и проверки доступности слотов
            entity.HasIndex(b => b.BookingDate);
            entity.HasIndex(b => new { b.VehicleId, b.BookingDate });

            // Связи: 1 Client -> many Bookings
            entity.HasOne(b => b.Client)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            // Связи: 1 Vehicle -> many Bookings
            entity.HasOne(b => b.Vehicle)
                .WithMany(v => v.Bookings)
                .HasForeignKey(b => b.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- Seed Initial Vehicles ---
        modelBuilder.Entity<Vehicle>().HasData(
            new Vehicle
            {
                Id = 1,
                Name = "Квадроцикл CFMOTO CFORCE 600",
                Description = "Мощный двухместный утилитарный квадроцикл с полным приводом 4WD, электроусилителем руля (EPS) и повышенной проходимостью для лесных трасс.",
                Price = 3500.00m,
                Status = VehicleStatus.Available,
                ImageUrl = "https://images.unsplash.com/photo-1558981403-c5f9899a28bc?auto=format&fit=crop&w=800&q=80"
            },
            new Vehicle
            {
                Id = 2,
                Name = "Эндуро мотоцикл Regulmoto Sport 003",
                Description = "Легкий и маневренный полноразмерный мотоцикл 250cc. Идеален для динамичных прохватов по пересеченной местности и песчаным карьерам.",
                Price = 2800.00m,
                Status = VehicleStatus.Available,
                ImageUrl = "https://images.unsplash.com/photo-1568772585407-9361f9bf3a87?auto=format&fit=crop&w=800&q=80"
            },
            new Vehicle
            {
                Id = 3,
                Name = "Багги BRP Can-Am Maverick X3",
                Description = "Турбированный сайд-бай-сайд для экстремального драйва с максимальным уровнем безопасности и спортивной подвеской Fox Racing.",
                Price = 7500.00m,
                Status = VehicleStatus.Available,
                ImageUrl = "https://images.unsplash.com/photo-1533473359331-0135ef1b58bf?auto=format&fit=crop&w=800&q=80"
            }
        );
    }
}
