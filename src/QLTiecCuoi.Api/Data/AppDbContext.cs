using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<Quote> Quotes => Set<Quote>();

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

        b.Entity<MenuItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.PricePerTable).HasPrecision(18, 0);
            e.HasData(
                new MenuItem { Id = 1, Name = "Gỏi ngó sen tôm thịt", Category = MenuCategory.Appetizer, PricePerTable = 350_000 },
                new MenuItem { Id = 2, Name = "Súp cua gà xé", Category = MenuCategory.Appetizer, PricePerTable = 300_000 },
                new MenuItem { Id = 3, Name = "Chả giò hải sản", Category = MenuCategory.Appetizer, PricePerTable = 320_000 },
                new MenuItem { Id = 4, Name = "Tôm sú rang muối", Category = MenuCategory.Main, PricePerTable = 650_000 },
                new MenuItem { Id = 5, Name = "Gà hấp lá chanh", Category = MenuCategory.Main, PricePerTable = 550_000 },
                new MenuItem { Id = 6, Name = "Bò lúc lắc khoai tây", Category = MenuCategory.Main, PricePerTable = 600_000 },
                new MenuItem { Id = 7, Name = "Cá điêu hồng hấp xì dầu", Category = MenuCategory.Main, PricePerTable = 500_000 },
                new MenuItem { Id = 8, Name = "Lẩu thái hải sản", Category = MenuCategory.Hotpot, PricePerTable = 700_000 },
                new MenuItem { Id = 9, Name = "Lẩu gà lá é", Category = MenuCategory.Hotpot, PricePerTable = 600_000 },
                new MenuItem { Id = 10, Name = "Chè hạt sen long nhãn", Category = MenuCategory.Dessert, PricePerTable = 200_000 },
                new MenuItem { Id = 11, Name = "Trái cây theo mùa", Category = MenuCategory.Dessert, PricePerTable = 220_000 });
        });

        b.Entity<ServiceItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 0);
            e.HasData(
                new ServiceItem { Id = 1, Name = "Trang trí cổng hoa và sân khấu", Unit = "gói", Price = 5_000_000 },
                new ServiceItem { Id = 2, Name = "MC dẫn chương trình", Unit = "buổi", Price = 3_000_000 },
                new ServiceItem { Id = 3, Name = "Ban nhạc sống", Unit = "buổi", Price = 8_000_000 },
                new ServiceItem { Id = 4, Name = "Quay phim, chụp ảnh", Unit = "gói", Price = 7_000_000 },
                new ServiceItem { Id = 5, Name = "Bia lon (thùng 24 lon)", Unit = "thùng", Price = 420_000 },
                new ServiceItem { Id = 6, Name = "Nước ngọt (thùng 24 lon)", Unit = "thùng", Price = 220_000 });
        });

        b.Entity<Quote>(e =>
        {
            e.Property(x => x.CustomerName).HasMaxLength(100).IsRequired();
            e.Property(x => x.CustomerPhone).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            foreach (var p in new[] { nameof(Quote.HallPrice), nameof(Quote.MenuTotal), nameof(Quote.ServiceTotal),
                                      nameof(Quote.Subtotal), nameof(Quote.VatAmount), nameof(Quote.Total) })
                e.Property(p).HasPrecision(18, 0);
            e.Property(x => x.VatPercent).HasPrecision(5, 2);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CustomerPhone);
        });

        b.Entity<QuoteLine>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(30);
            e.Property(x => x.UnitPrice).HasPrecision(18, 0);
            e.Property(x => x.Amount).HasPrecision(18, 0);
        });
    }
}
