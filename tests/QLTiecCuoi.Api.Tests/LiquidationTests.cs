using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

public class LiquidationTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2027, 1, 9);

    // Báo giá mẫu 40 bàn: sảnh 30.000.000; món 1 + 4 = 1.000.000 đ/bàn; MC 3.000.000 + 10 thùng bia 4.200.000.
    // Hợp đồng 84.920.000 đ, cọc 30% = 25.476.000 đ.
    private const decimal Deposit = 25_476_000;

    public LiquidationTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private ContractService Contracts(AppDbContext db, bool allowEarly = false) =>
        new(db, _clock, new ContractOptions { DepositPercent = 30, AllowLiquidationBeforeEvent = allowEarly });

    /// <summary>Giữ sảnh, báo giá, lập hợp đồng, thu đủ cọc; trả về id hợp đồng.</summary>
    private async Task<int> DepositedContractAsync(AppDbContext db, int hallId = 1, DateOnly? date = null, int tables = 40)
    {
        var d = date ?? Day;
        var booking = await new BookingService(db, _clock, new BookingOptions())
            .HoldAsync(new HoldRequest(hallId, d, Shift.Dinner, "Nguyễn An", "0900000000", tables, null));
        var quote = await new QuoteService(db, _clock, new QuoteOptions { VatPercent = 10 })
            .CreateAsync(new QuoteRequest(hallId, d, Shift.Dinner, tables, "Nguyễn An", "0900000000", [1, 4], [new(2, 1), new(5, 10)], booking.Id));
        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quote.Id!.Value, null, null, null));
        c = await Contracts(db).RecordPaymentAsync(c.Id, new PaymentRequest(c.DepositRequired, PaymentMethod.Cash, null));
        return c.Id;
    }

    private void GoToPartyDay() => _clock.Now = new DateTimeOffset(2027, 1, 9, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Liquidate_FewerTables_AndExtras_RecomputesTotal()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();

        var c = await Contracts(db).LiquidateAsync(id, new LiquidationRequest(38,
            [new("Thêm 3 thùng bia", 1_260_000), new("Phụ thu quá giờ", 2_000_000)], "Khách về đúng giờ"));

        var l = c.Liquidation!;
        Assert.Equal(ContractStatus.Liquidated, c.Status);
        Assert.Equal(1_000_000, l.MenuPerTable);
        Assert.Equal(38_000_000, l.MenuTotal);
        Assert.Equal(3_260_000, l.ExtraTotal);
        Assert.Equal(30_000_000 + 38_000_000 + 7_200_000 + 3_260_000, l.Subtotal); // 78.460.000
        Assert.Equal(7_846_000, l.VatAmount);
        Assert.Equal(86_306_000, l.FinalTotal);
        Assert.Equal(86_306_000 - Deposit, c.Remaining);
        Assert.Equal(Deposit, l.Paid);
    }

    [Fact]
    public async Task Liquidate_NoChanges_EqualsContractTotal()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();
        var c = await Contracts(db).LiquidateAsync(id, new LiquidationRequest(40, [], null));
        Assert.Equal(c.Total, c.Liquidation!.FinalTotal);
    }

    [Fact]
    public async Task Preview_DoesNotSave()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();
        var p = await Contracts(db).PreviewLiquidationAsync(id, new LiquidationRequest(45, [], null));
        Assert.Equal(45_000_000, p.MenuTotal);
        Assert.Equal(ContractStatus.Deposited, (await Contracts(db).GetAsync(id)).Status);
    }

    [Fact]
    public async Task Liquidate_BeforeEventDay_IsRejected_UnlessDemoOptionOn()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).LiquidateAsync(id, new LiquidationRequest(40, [], null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);

        var c = await Contracts(db, allowEarly: true).LiquidateAsync(id, new LiquidationRequest(40, [], null));
        Assert.Equal(ContractStatus.Liquidated, c.Status);
    }

    [Fact]
    public async Task Liquidate_Twice_IsRejected()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();
        await Contracts(db).LiquidateAsync(id, new LiquidationRequest(40, [], null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).LiquidateAsync(id, new LiquidationRequest(40, [], null)));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task Liquidate_ContractWithoutDeposit_IsRejected()
    {
        using var db = NewDb();
        var booking = await new BookingService(db, _clock, new BookingOptions())
            .HoldAsync(new HoldRequest(1, Day, Shift.Dinner, "A", "1", 40, null));
        var quote = await new QuoteService(db, _clock, new QuoteOptions())
            .CreateAsync(new QuoteRequest(1, Day, Shift.Dinner, 40, "A", "1", [1], [], booking.Id));
        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quote.Id!.Value, null, null, null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db, allowEarly: true).LiquidateAsync(c.Id, new LiquidationRequest(40, [], null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Theory]
    [InlineData(0, 100_000)]
    [InlineData(61, 100_000)]   // vượt sức chứa sảnh 60 bàn
    [InlineData(40, 0)]         // phát sinh 0 đồng
    [InlineData(40, -50_000)]
    public async Task Liquidate_InvalidInput_IsRejected(int tables, decimal extra)
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).LiquidateAsync(id, new LiquidationRequest(tables, [new("Phát sinh", extra)], null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task FinalPayment_UpToSettlement_ThenFullyPaid()
    {
        using var db = NewDb();
        var id = await DepositedContractAsync(db);
        GoToPartyDay();
        var c = await Contracts(db).LiquidateAsync(id, new LiquidationRequest(42, [], null));
        var remaining = c.Remaining;

        var over = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).RecordPaymentAsync(id, new PaymentRequest(remaining + 1, PaymentMethod.Cash, null)));
        Assert.Equal(ErrorKind.Validation, over.Kind);

        c = await Contracts(db).RecordPaymentAsync(id, new PaymentRequest(remaining, PaymentMethod.Transfer, null));
        Assert.Equal(0, c.Remaining);
        Assert.Equal(PaymentKind.Final, c.Payments.Last().Kind);

        var again = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).RecordPaymentAsync(id, new PaymentRequest(1, PaymentMethod.Cash, null)));
        Assert.Contains("đủ", again.Message);
    }

    [Fact]
    public async Task Liquidated_ReadBack_MatchesStoredTotals()
    {
        int id;
        using (var db = NewDb())
        {
            id = await DepositedContractAsync(db);
            GoToPartyDay();
            await Contracts(db).LiquidateAsync(id, new LiquidationRequest(38, [new("Thêm bia", 840_000)], null));
        }
        using (var db = NewDb())
        {
            var c = await Contracts(db).GetAsync(id);
            var stored = db.Contracts.Single(x => x.Id == id);
            Assert.Equal(stored.FinalTotal, c.Liquidation!.FinalTotal);
            Assert.Single(c.Liquidation.Extras);
        }
    }

    [Fact]
    public async Task RevenueReport_GroupsByMonthAndHall()
    {
        using var db = NewDb();
        var jan = await DepositedContractAsync(db, hallId: 1, date: Day);                         // thanh lý → doanh thu tháng 1
        var feb = await DepositedContractAsync(db, hallId: 2, date: new DateOnly(2027, 2, 14));   // đã cọc → sắp tới tháng 2
        await DepositedContractAsync(db, hallId: 3, date: new DateOnly(2027, 5, 1), tables: 30); // ngoài khoảng báo cáo
        GoToPartyDay();
        var liquidated = await Contracts(db).LiquidateAsync(jan, new LiquidationRequest(38, [], null));
        var febTotal = (await Contracts(db).GetAsync(feb)).Total;

        var r = await new ReportService(db).RevenueAsync(new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 1));

        Assert.Equal(3, r.ByMonth.Count);
        Assert.Equal("01/2027", r.ByMonth[0].Label);
        Assert.Equal(1, r.ByMonth[0].Events);
        Assert.Equal(38, r.ByMonth[0].Tables);
        Assert.Equal(liquidated.Liquidation!.FinalTotal, r.ByMonth[0].Revenue);
        Assert.Equal(1, r.ByMonth[1].UpcomingEvents);
        Assert.Equal(febTotal, r.ByMonth[1].UpcomingValue);
        Assert.Equal(0, r.ByMonth[2].Events + r.ByMonth[2].UpcomingEvents);
        Assert.Equal(liquidated.Liquidation.FinalTotal, r.ByHall.Single(h => h.Label == "Sảnh Kim Cương").Revenue);
        Assert.Equal(1, r.Total.Events);
        Assert.Equal(1, r.Total.UpcomingEvents);
    }

    [Fact]
    public async Task RevenueReport_RejectsReversedOrTooLongRange()
    {
        using var db = NewDb();
        var svc = new ReportService(db);
        await Assert.ThrowsAsync<AppException>(() => svc.RevenueAsync(new DateOnly(2027, 3, 1), new DateOnly(2027, 1, 1)));
        await Assert.ThrowsAsync<AppException>(() => svc.RevenueAsync(new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1)));
        Assert.Throws<AppException>(() => ReportService.ParseMonth("01-2027", "from"));
    }
}
