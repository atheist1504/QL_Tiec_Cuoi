using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services;

public class QuoteOptions
{
    /// <summary>Thuế VAT (%) cộng vào tổng báo giá.</summary>
    public decimal VatPercent { get; set; } = 8;

    /// <summary>Số ngày báo giá còn hiệu lực.</summary>
    public int ValidDays { get; set; } = 7;
}

/// <summary>
/// Lập báo giá: tiền sảnh + thực đơn (giá/bàn × số bàn) + dịch vụ (giá × số lượng) + VAT.
/// </summary>
public class QuoteService(AppDbContext db, TimeProvider clock, QuoteOptions options)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Tính thử báo giá, không lưu (dùng cho màn hình xem trước).</summary>
    public async Task<QuoteDto> PreviewAsync(QuoteRequest req) => ToDto(await BuildAsync(req), await SlotStatusAsync(req));

    /// <summary>Lập và lưu báo giá.</summary>
    public async Task<QuoteDto> CreateAsync(QuoteRequest req)
    {
        var quote = await BuildAsync(req);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return ToDto(quote, await SlotStatusAsync(req));
    }

    public async Task<QuoteDto> GetAsync(int id)
    {
        var q = await db.Quotes.Include(x => x.Hall).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy báo giá.");
        var status = await SlotStatusAsync(new QuoteRequest(q.HallId, q.EventDate, q.Shift, q.Tables, "", "", [], [], q.BookingId));
        return ToDto(q, status);
    }

    /// <summary>Danh sách báo giá gần đây, lọc theo số điện thoại hoặc lượt đặt sảnh.</summary>
    public async Task<List<QuoteSummaryDto>> ListAsync(string? phone, int? bookingId, int take = 50)
    {
        var q = db.Quotes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(phone)) q = q.Where(x => x.CustomerPhone == phone.Trim());
        if (bookingId is not null) q = q.Where(x => x.BookingId == bookingId);
        return await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(Math.Clamp(take, 1, 200))
            .Select(x => new QuoteSummaryDto(x.Id, x.CustomerName, x.CustomerPhone, x.Hall.Name,
                x.EventDate, x.Shift, x.Tables, x.Total, x.CreatedAt))
            .ToListAsync();
    }

    private async Task<Quote> BuildAsync(QuoteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập tên khách hàng.");
        if (string.IsNullOrWhiteSpace(req.CustomerPhone))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập số điện thoại.");
        if (!Enum.IsDefined(req.Shift))
            throw new AppException(ErrorKind.Validation, "Ca tiệc không hợp lệ.");
        if (req.Date <= Today)
            throw new AppException(ErrorKind.Validation, "Ngày tổ chức phải từ ngày mai trở đi.");

        var hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == req.HallId && h.IsActive)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy sảnh.");
        if (req.Tables <= 0 || req.Tables > hall.MaxTables)
            throw new AppException(ErrorKind.Validation, $"Số bàn phải từ 1 đến {hall.MaxTables} cho {hall.Name}.");

        if (req.BookingId is { } bookingId)
        {
            var bk = await db.Bookings.FindAsync(bookingId)
                ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy lượt đặt sảnh.");
            if (bk.Status == BookingStatus.Cancelled)
                throw new AppException(ErrorKind.Validation, "Lượt đặt sảnh đã hủy, không lập báo giá được.");
            if (bk.HallId != req.HallId || bk.EventDate != req.Date || bk.Shift != req.Shift)
                throw new AppException(ErrorKind.Validation, "Sảnh, ngày hoặc ca không khớp với lượt đặt sảnh.");
        }

        // Thực đơn: mỗi món tính theo bàn, không chọn trùng món.
        var menuIds = (req.MenuItemIds ?? []).Distinct().ToList();
        if (menuIds.Count == 0)
            throw new AppException(ErrorKind.Validation, "Vui lòng chọn ít nhất một món trong thực đơn.");
        var menu = await db.MenuItems.Where(x => menuIds.Contains(x.Id) && x.IsActive).ToListAsync();
        if (menu.Count != menuIds.Count)
            throw new AppException(ErrorKind.Validation, "Có món không tồn tại hoặc đã ngừng phục vụ.");

        // Dịch vụ: gộp các dòng trùng, số lượng phải dương.
        var svcReq = (req.Services ?? [])
            .GroupBy(x => x.ServiceItemId)
            .Select(g => (Id: g.Key, Qty: g.Sum(x => x.Quantity)))
            .ToList();
        if (svcReq.Any(x => x.Qty <= 0))
            throw new AppException(ErrorKind.Validation, "Số lượng dịch vụ phải lớn hơn 0.");
        var svcIds = svcReq.Select(x => x.Id).ToList();
        var services = await db.ServiceItems.Where(x => svcIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id);
        if (services.Count != svcIds.Count)
            throw new AppException(ErrorKind.Validation, "Có dịch vụ không tồn tại hoặc đã ngừng cung cấp.");

        var lines = new List<QuoteLine>();
        foreach (var m in menu.OrderBy(x => x.Category).ThenBy(x => x.Name))
            lines.Add(new QuoteLine
            {
                Kind = QuoteLineKind.Menu, MenuItemId = m.Id, Name = m.Name, Unit = "bàn",
                UnitPrice = m.PricePerTable, Quantity = req.Tables, Amount = m.PricePerTable * req.Tables,
            });
        foreach (var (id, qty) in svcReq)
        {
            var s = services[id];
            lines.Add(new QuoteLine
            {
                Kind = QuoteLineKind.Service, ServiceItemId = s.Id, Name = s.Name, Unit = s.Unit,
                UnitPrice = s.Price, Quantity = qty, Amount = s.Price * qty,
            });
        }

        var menuTotal = lines.Where(x => x.Kind == QuoteLineKind.Menu).Sum(x => x.Amount);
        var serviceTotal = lines.Where(x => x.Kind == QuoteLineKind.Service).Sum(x => x.Amount);
        var subtotal = hall.BasePrice + menuTotal + serviceTotal;
        // Làm tròn VAT đến đồng.
        var vat = Math.Round(subtotal * options.VatPercent / 100m, 0, MidpointRounding.AwayFromZero);

        return new Quote
        {
            BookingId = req.BookingId,
            HallId = hall.Id,
            Hall = hall,
            EventDate = req.Date,
            Shift = req.Shift,
            Tables = req.Tables,
            CustomerName = req.CustomerName.Trim(),
            CustomerPhone = req.CustomerPhone.Trim(),
            HallPrice = hall.BasePrice,
            MenuTotal = menuTotal,
            ServiceTotal = serviceTotal,
            Subtotal = subtotal,
            VatPercent = options.VatPercent,
            VatAmount = vat,
            Total = subtotal + vat,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            ValidUntil = Today.AddDays(options.ValidDays),
            Lines = lines,
        };
    }

    /// <summary>Tình trạng ô lịch để nhân viên biết sảnh còn trống khi báo giá.</summary>
    private async Task<SlotStatus> SlotStatusAsync(QuoteRequest req)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var active = await db.Bookings
            .Where(x => x.HallId == req.HallId && x.EventDate == req.Date && x.Shift == req.Shift
                        && x.Status != BookingStatus.Cancelled)
            .ToListAsync();
        var bk = active.FirstOrDefault(x => !(x.Status == BookingStatus.Held && x.HoldExpiresAt <= now));
        return bk is null ? SlotStatus.Available
            : bk.Status == BookingStatus.Deposited ? SlotStatus.Deposited : SlotStatus.Held;
    }

    private static QuoteDto ToDto(Quote q, SlotStatus status)
    {
        static QuoteLineDto L(QuoteLine x) => new(x.Name, x.UnitPrice, x.Quantity, x.Unit, x.Amount);
        return new QuoteDto(
            q.Id == 0 ? null : q.Id, q.BookingId, q.HallId, q.Hall.Name, q.EventDate, q.Shift, q.Tables,
            q.CustomerName, q.CustomerPhone, q.HallPrice,
            q.Lines.Where(x => x.Kind == QuoteLineKind.Menu).Select(L).ToList(),
            q.Lines.Where(x => x.Kind == QuoteLineKind.Service).Select(L).ToList(),
            q.MenuTotal, q.ServiceTotal, q.Subtotal, q.VatPercent, q.VatAmount, q.Total, status,
            q.Id == 0 ? null : q.CreatedAt, q.Id == 0 ? null : q.ValidUntil, q.Note);
    }
}
