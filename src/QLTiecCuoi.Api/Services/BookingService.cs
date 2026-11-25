using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services;

public enum ErrorKind { NotFound, Conflict, Validation, Unavailable }

public class AppException(ErrorKind kind, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
}

public class BookingOptions
{
    /// <summary>Số giờ giữ sảnh tạm trước khi khách phải đặt cọc.</summary>
    public int HoldHours { get; set; } = 48;

    /// <summary>Số ngày tối đa được xem lịch trong một lần truy vấn.</summary>
    public int MaxScheduleDays { get; set; } = 62;
}

public class BookingService(AppDbContext db, TimeProvider clock, BookingOptions options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<List<HallDto>> GetHallsAsync() =>
        await db.Halls.Where(h => h.IsActive).OrderBy(h => h.Id)
            .Select(h => new HallDto(h.Id, h.Name, h.MaxTables, h.BasePrice))
            .ToListAsync();

    /// <summary>Lịch sảnh theo ca trong khoảng [from, to].</summary>
    public async Task<List<SlotDto>> GetScheduleAsync(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new AppException(ErrorKind.Validation, "Ngày kết thúc phải sau ngày bắt đầu.");
        if (to.DayNumber - from.DayNumber + 1 > options.MaxScheduleDays)
            throw new AppException(ErrorKind.Validation, $"Chỉ xem tối đa {options.MaxScheduleDays} ngày một lần.");

        var halls = await db.Halls.Where(h => h.IsActive).OrderBy(h => h.Id).ToListAsync();
        var now = Now;
        var active = await db.Bookings
            .Where(x => x.EventDate >= from && x.EventDate <= to && x.Status != BookingStatus.Cancelled)
            .ToListAsync();
        // Giữ chỗ đã hết hạn coi như sảnh còn trống.
        var bySlot = active
            .Where(x => !IsExpiredHold(x, now))
            .ToDictionary(x => (x.HallId, x.EventDate, x.Shift));

        var result = new List<SlotDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
            foreach (var shift in Enum.GetValues<Shift>())
                foreach (var h in halls)
                {
                    bySlot.TryGetValue((h.Id, d, shift), out var bk);
                    result.Add(new SlotDto(h.Id, h.Name, d, shift,
                        bk is null ? SlotStatus.Available
                            : bk.Status == BookingStatus.Deposited ? SlotStatus.Deposited : SlotStatus.Held,
                        bk?.Id, bk?.CustomerName, bk?.HoldExpiresAt));
                }
        return result;
    }

    /// <summary>Giữ sảnh tạm cho khách. Báo lỗi Conflict nếu (sảnh, ngày, ca) đã có người giữ.</summary>
    public async Task<BookingDto> HoldAsync(HoldRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập tên khách hàng.");
        if (string.IsNullOrWhiteSpace(req.CustomerPhone))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập số điện thoại.");
        if (!Enum.IsDefined(req.Shift))
            throw new AppException(ErrorKind.Validation, "Ca tiệc không hợp lệ.");
        if (req.Date <= Today)
            throw new AppException(ErrorKind.Validation, "Chỉ nhận đặt sảnh từ ngày mai trở đi.");

        var hall = await db.Halls.FirstOrDefaultAsync(h => h.Id == req.HallId && h.IsActive)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy sảnh.");
        if (req.ExpectedTables <= 0 || req.ExpectedTables > hall.MaxTables)
            throw new AppException(ErrorKind.Validation,
                $"Số bàn phải từ 1 đến {hall.MaxTables} cho {hall.Name}.");

        var now = Now;
        var existing = await db.Bookings
            .Where(x => x.HallId == req.HallId && x.EventDate == req.Date && x.Shift == req.Shift
                        && x.Status != BookingStatus.Cancelled)
            .ToListAsync();

        // Giải phóng giữ chỗ quá hạn để ô lịch dùng lại được.
        var expired = existing.Where(x => IsExpiredHold(x, now)).ToList();
        foreach (var old in expired)
            old.Status = BookingStatus.Cancelled;
        if (expired.Count > 0)
            await db.SaveChangesAsync(); // lưu trước để chỉ mục UNIQUE nhả ô lịch

        if (existing.Any(x => x.Status != BookingStatus.Cancelled))
            throw SlotTaken(hall, req);

        var booking = new Booking
        {
            HallId = hall.Id,
            Hall = hall,
            EventDate = req.Date,
            Shift = req.Shift,
            Status = BookingStatus.Held,
            CustomerName = req.CustomerName.Trim(),
            CustomerPhone = req.CustomerPhone.Trim(),
            ExpectedTables = req.ExpectedTables,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            HoldExpiresAt = now.AddHours(options.HoldHours),
            CreatedAt = now,
        };
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Hai người giữ cùng một ô gần như cùng lúc: chỉ mục UNIQUE
            // trong CSDL chặn bản ghi thứ hai.
            db.ChangeTracker.Clear();
            throw SlotTaken(hall, req);
        }

        return ToDto(booking);
    }

    public async Task<BookingDto> GetAsync(int id)
    {
        var b = await db.Bookings.Include(x => x.Hall).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy lượt đặt sảnh.");
        return ToDto(b);
    }

    /// <summary>Hủy giữ chỗ. Lượt đã đặt cọc phải hủy qua nghiệp vụ hợp đồng (sprint sau).</summary>
    public async Task<BookingDto> CancelHoldAsync(int id)
    {
        var b = await db.Bookings.Include(x => x.Hall).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy lượt đặt sảnh.");
        if (b.Status == BookingStatus.Deposited)
            throw new AppException(ErrorKind.Validation,
                "Sảnh đã đặt cọc, không thể hủy giữ chỗ. Hãy xử lý qua hợp đồng.");
        b.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();
        return ToDto(b);
    }

    private static bool IsExpiredHold(Booking b, DateTime now) =>
        b.Status == BookingStatus.Held && b.HoldExpiresAt is { } exp && exp <= now;

    private static AppException SlotTaken(Hall hall, HoldRequest req) =>
        new(ErrorKind.Conflict,
            $"{hall.Name} ca {Labels.Of(req.Shift).ToLower()} ngày {req.Date:dd/MM/yyyy} đã có khách giữ hoặc đặt cọc.");

    private static BookingDto ToDto(Booking b) => new(
        b.Id, b.HallId, b.Hall.Name, b.EventDate, b.Shift, b.Status,
        b.CustomerName, b.CustomerPhone, b.ExpectedTables, b.HoldExpiresAt, b.Note);
}
