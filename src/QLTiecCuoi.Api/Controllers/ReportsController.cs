using Microsoft.AspNetCore.Mvc;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController(ReportService service) : ControllerBase
{
    /// <summary>Doanh thu theo tháng và theo sảnh, ví dụ /api/reports/revenue?from=2026-09&amp;to=2027-03</summary>
    [HttpGet("revenue")]
    public Task<RevenueReportDto> Revenue([FromQuery] string from, [FromQuery] string to) =>
        service.RevenueAsync(ReportService.ParseMonth(from, "from"), ReportService.ParseMonth(to, "to"));
}
