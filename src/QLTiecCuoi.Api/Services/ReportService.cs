using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services;

/// <summary>Báo cáo doanh thu mùa cưới (UC06).</summary>
public class ReportService(AppDbContext db)
{
    public const int MaxMonths = 24;

    /// <summary>
    /// Doanh thu theo tháng tổ chức tiệc và theo sảnh.
    /// Doanh thu = giá trị quyết toán của hợp đồng đã thanh lý.
    /// "Sắp tới" = hợp đồng đã cọc nhưng chưa thanh lý (giá trị dự kiến theo hợp đồng).
    /// </summary>
    public async Task<RevenueReportDto> RevenueAsync(DateOnly fromMonth, DateOnly toMonth)
    {
        var from = new DateOnly(fromMonth.Year, fromMonth.Month, 1);
        var toStart = new DateOnly(toMonth.Year, toMonth.Month, 1);
        if (toStart < from)
            throw new AppException(ErrorKind.Validation, "Tháng kết thúc phải sau tháng bắt đầu.");
        var months = (toStart.Year - from.Year) * 12 + toStart.Month - from.Month + 1;
        if (months > MaxMonths)
            throw new AppException(ErrorKind.Validation, $"Chỉ xem tối đa {MaxMonths} tháng một lần.");
        var to = toStart.AddMonths(1).AddDays(-1);

        var contracts = await db.Contracts
            .Include(c => c.Booking).ThenInclude(b => b.Hall)
            .Where(c => (c.Status == ContractStatus.Liquidated || c.Status == ContractStatus.Deposited)
                        && c.Booking.EventDate >= from && c.Booking.EventDate <= to)
            .ToListAsync();

        var byMonth = Enumerable.Range(0, months)
            .Select(i => from.AddMonths(i))
            .Select(m => Row($"{m.Month:00}/{m.Year}",
                contracts.Where(c => c.Booking.EventDate.Year == m.Year && c.Booking.EventDate.Month == m.Month)))
            .ToList();

        var halls = await db.Halls.Where(h => h.IsActive).OrderBy(h => h.Id).ToListAsync();
        var byHall = halls.Select(h => Row(h.Name, contracts.Where(c => c.Booking.HallId == h.Id))).ToList();

        return new RevenueReportDto(from, to, byMonth, byHall, Row("Tổng", contracts));
    }

    public static DateOnly ParseMonth(string value, string name) =>
        DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new AppException(ErrorKind.Validation, $"Tham số {name} phải có dạng yyyy-MM, ví dụ 2027-01.");

    private static RevenueRowDto Row(string label, IEnumerable<Contract> items)
    {
        var list = items.ToList();
        var done = list.Where(c => c.Status == ContractStatus.Liquidated).ToList();
        var upcoming = list.Where(c => c.Status == ContractStatus.Deposited).ToList();
        return new RevenueRowDto(label,
            done.Count, done.Sum(c => c.ActualTables ?? 0), done.Sum(c => c.FinalTotal ?? 0),
            upcoming.Count, upcoming.Sum(c => c.Total));
    }
}
