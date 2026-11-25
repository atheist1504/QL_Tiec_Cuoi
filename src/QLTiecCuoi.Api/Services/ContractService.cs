using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services;

public class ContractOptions
{
    /// <summary>Tỷ lệ đặt cọc (%) trên giá trị hợp đồng để chốt sảnh.</summary>
    public decimal DepositPercent { get; set; } = 30;
}

/// <summary>Hợp đồng và đặt cọc (UC04).</summary>
public class ContractService(AppDbContext db, TimeProvider clock, ContractOptions options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Lập hợp đồng từ báo giá đã gắn với một lượt giữ sảnh còn hạn.</summary>
    public async Task<ContractDto> CreateFromQuoteAsync(CreateContractRequest req)
    {
        var quote = await db.Quotes.Include(q => q.Hall).Include(q => q.Booking!).ThenInclude(b => b.Hall)
            .FirstOrDefaultAsync(q => q.Id == req.QuoteId)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy báo giá.");
        if (quote.Booking is not { } booking)
            throw new AppException(ErrorKind.Validation, "Báo giá chưa gắn với lượt giữ sảnh. Hãy giữ sảnh rồi lập báo giá từ lượt giữ đó.");
        if (quote.ValidUntil < Today)
            throw new AppException(ErrorKind.Validation, $"Báo giá đã hết hiệu lực từ {quote.ValidUntil:dd/MM/yyyy}. Hãy lập báo giá mới.");
        EnsureHoldActive(booking);
        if (await db.Contracts.AnyAsync(c => c.BookingId == booking.Id && c.Status != ContractStatus.Cancelled))
            throw new AppException(ErrorKind.Conflict, "Lượt giữ sảnh này đã có hợp đồng.");

        var deposit = Math.Ceiling(quote.Total * options.DepositPercent / 100m / 1000m) * 1000m; // làm tròn lên nghìn đồng
        var contract = new Contract
        {
            Code = "TMP-" + Guid.NewGuid().ToString("N")[..20],
            Status = ContractStatus.AwaitingDeposit,
            BookingId = booking.Id,
            Booking = booking,
            QuoteId = quote.Id,
            Quote = quote,
            CustomerName = quote.CustomerName,
            CustomerPhone = quote.CustomerPhone,
            CustomerAddress = Clean(req.CustomerAddress),
            CustomerIdNumber = Clean(req.CustomerIdNumber),
            Total = quote.Total,
            DepositPercent = options.DepositPercent,
            DepositRequired = Math.Min(deposit, quote.Total),
            Note = Clean(req.Note),
            CreatedAt = Now,
        };
        db.Contracts.Add(contract);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new AppException(ErrorKind.Conflict, "Lượt giữ sảnh này vừa được lập hợp đồng ở máy khác.");
        }

        contract.Code = $"HD{contract.CreatedAt:yy}-{contract.Id:00000}";
        await db.SaveChangesAsync();
        return ToDto(contract);
    }

    /// <summary>Ghi nhận một khoản khách trả trước. Đủ cọc thì chốt sảnh.</summary>
    public async Task<ContractDto> RecordPaymentAsync(int id, PaymentRequest req)
    {
        var c = await LoadAsync(id);
        if (c.Status is ContractStatus.Cancelled or ContractStatus.Liquidated)
            throw new AppException(ErrorKind.Validation, $"Hợp đồng {Labels.Of(c.Status).ToLower()}, không nhận thêm tiền.");
        if (req.Amount <= 0 || req.Amount != decimal.Truncate(req.Amount))
            throw new AppException(ErrorKind.Validation, "Số tiền phải là số nguyên dương (đồng).");
        if (!Enum.IsDefined(req.Method))
            throw new AppException(ErrorKind.Validation, "Hình thức thanh toán không hợp lệ.");
        var paid = c.Payments.Sum(p => p.Amount);
        if (paid + req.Amount > c.Total)
            throw new AppException(ErrorKind.Validation,
                $"Số tiền vượt giá trị hợp đồng. Còn lại tối đa {Labels.Money(c.Total - paid)}.");
        if (c.Status == ContractStatus.AwaitingDeposit)
            EnsureHoldActive(c.Booking);

        c.Payments.Add(new Payment
        {
            Kind = PaymentKind.Deposit,
            Amount = req.Amount,
            Method = req.Method,
            PaidAt = Now,
            Note = Clean(req.Note),
        });

        if (c.Status == ContractStatus.AwaitingDeposit && paid + req.Amount >= c.DepositRequired)
        {
            c.Status = ContractStatus.Deposited;
            c.Booking.Status = BookingStatus.Deposited;
            c.Booking.HoldExpiresAt = null;
        }
        await db.SaveChangesAsync();
        return ToDto(c);
    }

    /// <summary>Hủy hợp đồng chưa nhận tiền. Lượt giữ sảnh vẫn giữ nguyên.</summary>
    public async Task<ContractDto> CancelAsync(int id)
    {
        var c = await LoadAsync(id);
        if (c.Status != ContractStatus.AwaitingDeposit || c.Payments.Count > 0)
            throw new AppException(ErrorKind.Validation,
                "Chỉ hủy được hợp đồng chưa nhận tiền. Hợp đồng đã cọc phải xử lý qua thanh lý.");
        c.Status = ContractStatus.Cancelled;
        await db.SaveChangesAsync();
        return ToDto(c);
    }

    public async Task<ContractDto> GetAsync(int id) => ToDto(await LoadAsync(id));

    public async Task<List<ContractDto>> ListAsync(int? bookingId)
    {
        var q = db.Contracts.Include(c => c.Booking).ThenInclude(b => b.Hall).Include(c => c.Quote).Include(c => c.Payments).AsQueryable();
        if (bookingId is not null) q = q.Where(c => c.BookingId == bookingId);
        var list = await q.OrderByDescending(c => c.Id).Take(100).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    internal async Task<Contract> LoadAsync(int id) =>
        await db.Contracts
            .Include(c => c.Booking).ThenInclude(b => b.Hall)
            .Include(c => c.Quote).ThenInclude(q => q.Lines)
            .Include(c => c.Payments)
            .FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy hợp đồng.");

    private void EnsureHoldActive(Booking b)
    {
        if (b.Status == BookingStatus.Cancelled || (b.Status == BookingStatus.Held && b.HoldExpiresAt <= Now))
            throw new AppException(ErrorKind.Validation,
                "Lượt giữ sảnh đã hết hạn hoặc đã hủy. Hãy giữ sảnh lại trước khi lập hợp đồng hoặc nhận cọc.");
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ContractDto ToDto(Contract c)
    {
        var paid = c.Payments.Sum(p => p.Amount);
        return new ContractDto(
            c.Id, c.Code, c.Status, c.BookingId, c.QuoteId,
            c.CustomerName, c.CustomerPhone, c.CustomerAddress, c.CustomerIdNumber,
            c.Booking.Hall.Name, c.Booking.EventDate, c.Booking.Shift, c.Quote.Tables,
            c.Total, c.DepositPercent, c.DepositRequired, paid, c.Total - paid,
            c.Status == ContractStatus.AwaitingDeposit ? c.Booking.HoldExpiresAt : null,
            c.CreatedAt, c.Note,
            c.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
                .Select(p => new PaymentDto(p.Id, p.Kind, p.Amount, p.Method, p.PaidAt, p.Note)).ToList());
    }
}
