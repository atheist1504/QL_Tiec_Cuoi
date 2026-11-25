using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Hall>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.BasePrice).HasPrecision(18, 0);
            e.HasData(
                new Hall { Id = 1, Name = "Sảnh Kim Cương", MaxTables = 60, BasePrice = 30_000_000 },
                new Hall { Id = 2, Name = "Sảnh Hoàng Gia", MaxTables = 45, BasePrice = 22_000_000 },
                new Hall { Id = 3, Name = "Sảnh Ngọc Trai", MaxTables = 30, BasePrice = 15_000_000 },
                new Hall { Id = 4, Name = "Sảnh Hồng Ngọc", MaxTables = 20, BasePrice = 10_000_000 });
        });

        b.Entity<Booking>(e =>
        {
            e.Property(x => x.CustomerName).HasMaxLength(100).IsRequired();
            e.Property(x => x.CustomerPhone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);

            // Chống trùng sảnh ở mức CSDL: mỗi (sảnh, ngày, ca) chỉ có tối đa
            // một lượt đặt chưa hủy. Kể cả khi 2 nhân viên bấm cùng lúc,
            // CSDL vẫn chỉ nhận một bản ghi.
            e.HasIndex(x => new { x.HallId, x.EventDate, x.Shift })
                .IsUnique()
                .HasFilter($"[Status] <> {(int)BookingStatus.Cancelled}")
                .HasDatabaseName("UX_Booking_ActiveSlot");
        });
    }
}
