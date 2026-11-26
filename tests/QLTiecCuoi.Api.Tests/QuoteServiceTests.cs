using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

public class QuoteServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2027, 1, 9);

    // Dữ liệu mẫu: Sảnh Kim Cương (Id 1) 30.000.000 đ, tối đa 60 bàn.
    // Món 1 = 350.000 đ/bàn, món 4 = 650.000 đ/bàn. Dịch vụ 2 (MC) = 3.000.000 đ, dịch vụ 5 (bia) = 420.000 đ/thùng.

    public QuoteServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private QuoteService NewService(AppDbContext db) => new(db, _clock, new QuoteOptions { VatPercent = 10, ValidDays = 7 });

    private static QuoteRequest Req(int tables = 40, List<int>? menu = null, List<QuoteServiceRequest>? services = null,
        int? bookingId = null, DateOnly? date = null) =>
        new(1, date ?? Day, Shift.Dinner, tables, "Nguyễn An", "0900000000",
            menu ?? [1, 4], services ?? [new(2, 1), new(5, 10)], bookingId);

    [Fact]
    public async Task Preview_ComputesTotalsCorrectly()
    {
        using var db = NewDb();
        var q = await NewService(db).PreviewAsync(Req());

        Assert.Null(q.Id);
        Assert.Equal(30_000_000, q.HallPrice);
        Assert.Equal((350_000 + 650_000) * 40, q.MenuTotal);       // 40.000.000
        Assert.Equal(3_000_000 + 420_000 * 10, q.ServiceTotal);    // 7.200.000
        Assert.Equal(77_200_000, q.Subtotal);
        Assert.Equal(7_720_000, q.VatAmount);
        Assert.Equal(84_920_000, q.Total);
        Assert.Equal(2, q.MenuLines.Count);
        Assert.All(q.MenuLines, l => Assert.Equal(40, l.Quantity));
        Assert.Equal(SlotStatus.Available, q.SlotStatus);
        Assert.Equal(0, await db.Quotes.CountAsync()); // xem trước không lưu
    }

    [Fact]
    public async Task DefaultVat_IsEightPercent()
    {
        using var db = NewDb();
        var q = await new QuoteService(db, _clock, new QuoteOptions()).PreviewAsync(Req());
        Assert.Equal(8, q.VatPercent);
        Assert.Equal(6_176_000, q.VatAmount);   // 8% × 77.200.000
        Assert.Equal(83_376_000, q.Total);
    }

    [Fact]
    public async Task Create_SavesQuoteWithValidity()
    {
        using var db = NewDb();
        var q = await NewService(db).CreateAsync(Req());

        Assert.NotNull(q.Id);
        Assert.Equal(new DateOnly(2026, 12, 8), q.ValidUntil);
        using var check = NewDb();
        Assert.Equal(4, await check.Quotes.Where(x => x.Id == q.Id).SelectMany(x => x.Lines).CountAsync());
    }

    [Fact]
    public async Task SavedQuote_KeepsOldPrices_WhenMenuPriceChanges()
    {
        int id;
        using (var db = NewDb())
            id = (await NewService(db).CreateAsync(Req())).Id!.Value;

        using (var db = NewDb())
        {
            await new CatalogService(db).SaveMenuItemAsync(4, new MenuItemUpsert("Tôm sú rang muối", MenuCategory.Main, 800_000));
        }

        using (var db = NewDb())
        {
            var q = await NewService(db).GetAsync(id);
            Assert.Equal(650_000, q.MenuLines.Single(l => l.Name == "Tôm sú rang muối").UnitPrice);
            Assert.Equal(84_920_000, q.Total);

            // Báo giá lập lại sau khi đổi giá thì dùng giá mới.
            var revised = await NewService(db).PreviewAsync(Req());
            Assert.Equal(800_000, revised.MenuLines.Single(l => l.Name == "Tôm sú rang muối").UnitPrice);
        }
    }

    [Fact]
    public async Task DuplicateMenuAndServiceLines_AreMerged()
    {
        using var db = NewDb();
        var q = await NewService(db).PreviewAsync(Req(menu: [1, 1, 4], services: [new(5, 4), new(5, 6)]));
        Assert.Equal(2, q.MenuLines.Count);
        Assert.Equal(10, q.ServiceLines.Single().Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task InvalidTableCount_IsRejected(int tables)
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req(tables: tables)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task EmptyMenu_IsRejected()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req(menu: [])));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task InactiveMenuItem_IsRejected()
    {
        using var db = NewDb();
        await new CatalogService(db).SaveMenuItemAsync(1, new MenuItemUpsert("Gỏi ngó sen tôm thịt", MenuCategory.Appetizer, 350_000, IsActive: false));
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req()));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task ZeroServiceQuantity_IsRejected()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req(services: [new(2, 0)])));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task PastDate_IsRejected()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req(date: new DateOnly(2026, 11, 30))));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Quote_ShowsSlotHeld_AndLinksToMatchingBooking()
    {
        using var db = NewDb();
        var booking = await new BookingService(db, _clock, new BookingOptions())
            .HoldAsync(new HoldRequest(1, Day, Shift.Dinner, "Nguyễn An", "0900000000", 40, null));

        var q = await NewService(db).CreateAsync(Req(bookingId: booking.Id));

        Assert.Equal(SlotStatus.Held, q.SlotStatus);
        Assert.Equal(booking.Id, q.BookingId);
        Assert.Single(await NewService(db).ListAsync(null, booking.Id));
    }

    [Fact]
    public async Task Quote_WithBookingOfDifferentSlot_IsRejected()
    {
        using var db = NewDb();
        var booking = await new BookingService(db, _clock, new BookingOptions())
            .HoldAsync(new HoldRequest(1, Day, Shift.Lunch, "Nguyễn An", "0900000000", 40, null));
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).PreviewAsync(Req(bookingId: booking.Id)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Catalog_RejectsDuplicateName_AndNonPositivePrice()
    {
        using var db = NewDb();
        var svc = new CatalogService(db);
        var dup = await Assert.ThrowsAsync<AppException>(() =>
            svc.SaveMenuItemAsync(null, new MenuItemUpsert(" Lẩu gà lá é ", MenuCategory.Hotpot, 500_000)));
        Assert.Equal(ErrorKind.Conflict, dup.Kind);
        var price = await Assert.ThrowsAsync<AppException>(() =>
            svc.SaveServiceAsync(null, new ServiceItemUpsert("Pháo giấy", "lần", 0)));
        Assert.Equal(ErrorKind.Validation, price.Kind);
    }

    [Fact]
    public async Task Catalog_HidesInactiveItemsByDefault()
    {
        using var db = NewDb();
        var svc = new CatalogService(db);
        await svc.SaveServiceAsync(3, new ServiceItemUpsert("Ban nhạc sống", "buổi", 8_000_000, IsActive: false));
        Assert.DoesNotContain(await svc.GetServicesAsync(false), x => x.Id == 3);
        Assert.Contains(await svc.GetServicesAsync(true), x => x.Id == 3);
    }
}
