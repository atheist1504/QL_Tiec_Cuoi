using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

public class ContractServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2027, 1, 9);

    // Báo giá mẫu: 40 bàn, món 1 + 4, MC + 10 thùng bia → tổng 84.920.000 đ; cọc 30% = 25.476.000 đ.
    private const decimal QuoteTotal = 84_920_000;
    private const decimal Deposit = 25_476_000;

    public ContractServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private ContractService Contracts(AppDbContext db) => new(db, _clock, new ContractOptions { DepositPercent = 30 });

    /// <summary>Giữ sảnh rồi lập báo giá gắn với lượt giữ đó.</summary>
    private async Task<(int BookingId, int QuoteId)> HoldAndQuoteAsync(AppDbContext db, Shift shift = Shift.Dinner)
    {
        var booking = await new BookingService(db, _clock, new BookingOptions { HoldHours = 48 })
            .HoldAsync(new HoldRequest(1, Day, shift, "Nguyễn An", "0900000000", 40, null));
        var quote = await new QuoteService(db, _clock, new QuoteOptions { VatPercent = 10, ValidDays = 7 })
            .CreateAsync(new QuoteRequest(1, Day, shift, 40, "Nguyễn An", "0900000000", [1, 4], [new(2, 1), new(5, 10)], booking.Id));
        return (booking.Id, quote.Id!.Value);
    }

    [Fact]
    public async Task Create_FromQuote_ComputesDepositAndCode()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);

        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, "Q1, TP.HCM", null, null));

        Assert.Equal(ContractStatus.AwaitingDeposit, c.Status);
        Assert.Equal(QuoteTotal, c.Total);
        Assert.Equal(Deposit, c.DepositRequired);
        Assert.Equal($"HD26-{c.Id:00000}", c.Code);
        Assert.NotNull(c.HoldExpiresAt);
    }

    [Fact]
    public async Task Create_FromQuoteWithoutBooking_IsRejected()
    {
        using var db = NewDb();
        var quote = await new QuoteService(db, _clock, new QuoteOptions())
            .CreateAsync(new QuoteRequest(1, Day, Shift.Dinner, 40, "A", "1", [1], [], null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quote.Id!.Value, null, null, null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Create_SecondContractForSameBooking_IsRejected()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null)));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task Create_AfterHoldExpired_IsRejected()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        _clock.Now = _clock.Now.AddHours(49);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Create_FromExpiredQuote_IsRejected()
    {
        using var db = NewDb();
        var (bookingId, quoteId) = await HoldAndQuoteAsync(db);
        // Báo giá hết hiệu lực sau 7 ngày; gia hạn giữ chỗ để chỉ kiểm tra hạn báo giá.
        db.Bookings.Single(b => b.Id == bookingId).HoldExpiresAt = _clock.Now.UtcDateTime.AddDays(30);
        await db.SaveChangesAsync();
        _clock.Now = _clock.Now.AddDays(8);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null)));
        Assert.Contains("hết hiệu lực", ex.Message);
    }

    [Fact]
    public async Task PartialDeposit_KeepsHold_FullDeposit_LocksHall()
    {
        using var db = NewDb();
        var (bookingId, quoteId) = await HoldAndQuoteAsync(db);
        var svc = Contracts(db);
        var c = await svc.CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));

        c = await svc.RecordPaymentAsync(c.Id, new PaymentRequest(10_000_000, PaymentMethod.Cash, null));
        Assert.Equal(ContractStatus.AwaitingDeposit, c.Status);
        Assert.Equal(BookingStatus.Held, db.Bookings.Single(b => b.Id == bookingId).Status);

        c = await svc.RecordPaymentAsync(c.Id, new PaymentRequest(Deposit - 10_000_000, PaymentMethod.Transfer, "CK Vietcombank"));
        Assert.Equal(ContractStatus.Deposited, c.Status);
        Assert.Equal(Deposit, c.Paid);
        Assert.Equal(QuoteTotal - Deposit, c.Remaining);
        Assert.Null(c.HoldExpiresAt);

        using var check = NewDb();
        var booking = check.Bookings.Single(b => b.Id == bookingId);
        Assert.Equal(BookingStatus.Deposited, booking.Status);
        Assert.Null(booking.HoldExpiresAt);

        // Lịch sảnh hiện "đã cọc" và vẫn đúng sau khi qua hạn giữ chỗ cũ.
        _clock.Now = _clock.Now.AddHours(100);
        var slots = await new BookingService(check, _clock, new BookingOptions()).GetScheduleAsync(Day, Day);
        Assert.Equal(SlotStatus.Deposited, slots.Single(s => s.HallId == 1 && s.Shift == Shift.Dinner).Status);
    }

    [Fact]
    public async Task Payment_AfterHoldExpired_BeforeDeposit_IsRejected()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        _clock.Now = _clock.Now.AddHours(49);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).RecordPaymentAsync(c.Id, new PaymentRequest(Deposit, PaymentMethod.Cash, null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(84_920_001)] // vượt giá trị hợp đồng
    public async Task Payment_InvalidAmount_IsRejected(decimal amount)
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Contracts(db).RecordPaymentAsync(c.Id, new PaymentRequest(amount, PaymentMethod.Cash, null)));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Cancel_UnpaidContract_AllowsNewContract_ButPaidCannotCancel()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        var svc = Contracts(db);
        var first = await svc.CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        await svc.CancelAsync(first.Id);

        var second = await svc.CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        await svc.RecordPaymentAsync(second.Id, new PaymentRequest(1_000_000, PaymentMethod.Cash, null));
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.CancelAsync(second.Id));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task DepositedBooking_CannotBeCancelledAsHold()
    {
        using var db = NewDb();
        var (bookingId, quoteId) = await HoldAndQuoteAsync(db);
        var c = await Contracts(db).CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        await Contracts(db).RecordPaymentAsync(c.Id, new PaymentRequest(Deposit, PaymentMethod.Cash, null));
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            new BookingService(db, _clock, new BookingOptions()).CancelHoldAsync(bookingId));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Pdf_ContractAndQuote_AreGenerated()
    {
        using var db = NewDb();
        var (_, quoteId) = await HoldAndQuoteAsync(db);
        var contracts = Contracts(db);
        var c = await contracts.CreateFromQuoteAsync(new CreateContractRequest(quoteId, null, null, null));
        var pdf = new PdfService(db, contracts, _clock, new RestaurantOptions());

        var (contractPdf, contractName) = await pdf.ContractPdfAsync(c.Id);
        var (quotePdf, quoteName) = await pdf.QuotePdfAsync(quoteId);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(contractPdf, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(quotePdf, 0, 4));
        Assert.Equal($"{c.Code}.pdf", contractName);
        Assert.Equal($"BG-{quoteId:00000}.pdf", quoteName);
    }
}

public class VietnameseNumberTests
{
    [Theory]
    [InlineData(0, "không")]
    [InlineData(5, "năm")]
    [InlineData(10, "mười")]
    [InlineData(15, "mười lăm")]
    [InlineData(21, "hai mươi mốt")]
    [InlineData(25, "hai mươi lăm")]
    [InlineData(105, "một trăm linh năm")]
    [InlineData(110, "một trăm mười")]
    [InlineData(1_000_000, "một triệu")]
    [InlineData(1_005_000, "một triệu không trăm linh năm nghìn")]
    [InlineData(25_476_000, "hai mươi lăm triệu bốn trăm bảy mươi sáu nghìn")]
    [InlineData(124_520_000, "một trăm hai mươi bốn triệu năm trăm hai mươi nghìn")]
    [InlineData(2_000_000_001, "hai tỷ không trăm linh một")]
    public void ToWords_ReadsVietnameseCorrectly(long n, string expected) =>
        Assert.Equal(expected, VietnameseNumber.ToWords(n));

    [Fact]
    public void ToMoneyWords_CapitalizesAndAddsCurrency() =>
        Assert.Equal("Tám mươi bốn triệu chín trăm hai mươi nghìn đồng", VietnameseNumber.ToMoneyWords(84_920_000));
}
