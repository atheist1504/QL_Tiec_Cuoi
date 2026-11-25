using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

/// <summary>Đồng hồ giả để kiểm thử giữ chỗ hết hạn.</summary>
public class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public class BookingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2027, 1, 9);

    public BookingServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private BookingService NewService(AppDbContext db) => new(db, _clock, new BookingOptions { HoldHours = 48 });

    private static HoldRequest Req(int hall = 1, DateOnly? date = null, Shift shift = Shift.Dinner, int tables = 30) =>
        new(hall, date ?? Day, shift, "Nguyễn An", "0900000000", tables, null);

    [Fact]
    public async Task Hold_FreeSlot_Succeeds()
    {
        using var db = NewDb();
        var b = await NewService(db).HoldAsync(Req());
        Assert.Equal(BookingStatus.Held, b.Status);
        Assert.Equal(_clock.Now.UtcDateTime.AddHours(48), b.HoldExpiresAt);
    }

    [Fact]
    public async Task Hold_SameHallDayShift_IsRejected()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.HoldAsync(Req());
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.HoldAsync(Req()));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task Hold_OtherShiftOrHall_IsAllowed()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.HoldAsync(Req());
        await svc.HoldAsync(Req(shift: Shift.Lunch));
        await svc.HoldAsync(Req(hall: 2));
        Assert.Equal(3, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Hold_AfterPreviousHoldExpired_Succeeds()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var first = await svc.HoldAsync(Req());
        _clock.Now = _clock.Now.AddHours(49);

        var second = await svc.HoldAsync(Req());

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(BookingStatus.Cancelled, (await svc.GetAsync(first.Id)).Status);
    }

    [Fact]
    public async Task Hold_AfterCancel_Succeeds()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var first = await svc.HoldAsync(Req());
        await svc.CancelHoldAsync(first.Id);
        await svc.HoldAsync(Req());
    }

    [Fact]
    public async Task Hold_ConcurrentRequests_OnlyOneWins()
    {
        // Hai nhân viên ở hai máy cùng giữ một ô: dùng hai DbContext riêng.
        using var db1 = NewDb();
        using var db2 = NewDb();
        var results = await Task.WhenAll(
            Try(() => NewService(db1).HoldAsync(Req())),
            Try(() => NewService(db2).HoldAsync(Req())));
        Assert.Single(results, ok => ok);
        using var check = NewDb();
        Assert.Equal(1, await check.Bookings.CountAsync(x => x.Status != BookingStatus.Cancelled));

        static async Task<bool> Try(Func<Task> f)
        {
            try { await f(); return true; }
            catch (AppException e) when (e.Kind == ErrorKind.Conflict) { return false; }
        }
    }

    [Fact]
    public async Task Database_RejectsDuplicateActiveBooking()
    {
        // Kiểm tra trực tiếp chỉ mục UNIQUE, bỏ qua tầng service.
        using var db = NewDb();
        Booking Make() => new()
        {
            HallId = 1, EventDate = Day, Shift = Shift.Dinner, Status = BookingStatus.Held,
            CustomerName = "A", CustomerPhone = "1", ExpectedTables = 10, CreatedAt = DateTime.UtcNow,
        };
        db.Bookings.Add(Make());
        await db.SaveChangesAsync();
        db.Bookings.Add(Make());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)] // Sảnh Kim Cương tối đa 60 bàn
    public async Task Hold_InvalidTableCount_IsRejected(int tables)
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(() => NewService(db).HoldAsync(Req(tables: tables)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Hold_PastOrTodayDate_IsRejected()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<AppException>(
            () => NewService(db).HoldAsync(Req(date: new DateOnly(2026, 12, 1))));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Schedule_ShowsStatusPerSlot_AndExpiredHoldAsAvailable()
    {
        using var db = NewDb();
        var svc = NewService(db);
        await svc.HoldAsync(Req(hall: 1));
        await svc.HoldAsync(Req(hall: 2));
        db.Bookings.Single(x => x.HallId == 2).HoldExpiresAt = _clock.Now.UtcDateTime.AddMinutes(-1);
        await db.SaveChangesAsync();

        var slots = await svc.GetScheduleAsync(Day, Day);

        Assert.Equal(4 * 2, slots.Count); // 4 sảnh x 2 ca
        Assert.Equal(SlotStatus.Held, slots.Single(s => s.HallId == 1 && s.Shift == Shift.Dinner).Status);
        Assert.Equal(SlotStatus.Available, slots.Single(s => s.HallId == 2 && s.Shift == Shift.Dinner).Status);
    }

    [Fact]
    public async Task CancelHold_DepositedBooking_IsRejected()
    {
        using var db = NewDb();
        var svc = NewService(db);
        var b = await svc.HoldAsync(Req());
        db.Bookings.Single().Status = BookingStatus.Deposited;
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.CancelHoldAsync(b.Id));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }
}
