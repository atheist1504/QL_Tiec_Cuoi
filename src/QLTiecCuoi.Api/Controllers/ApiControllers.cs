using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Controllers;

/// <summary>Đổi AppException thành mã HTTP phù hợp (404, 409, 400).</summary>
public class AppExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not AppException ex) return;
        var status = ex.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        context.Result = new ObjectResult(new ErrorDto(ex.Message)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

[ApiController]
[Route("api/halls")]
public class HallsController(BookingService service) : ControllerBase
{
    [HttpGet]
    public Task<List<HallDto>> GetAll() => service.GetHallsAsync();
}

[ApiController]
[Route("api/schedule")]
public class ScheduleController(BookingService service) : ControllerBase
{
    /// <summary>Lịch sảnh theo ca, ví dụ /api/schedule?from=2027-01-01&amp;to=2027-01-07</summary>
    [HttpGet]
    public Task<List<SlotDto>> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to) =>
        service.GetScheduleAsync(from, to);
}

[ApiController]
[Route("api/bookings")]
public class BookingsController(BookingService service) : ControllerBase
{
    [HttpGet("{id:int}")]
    public Task<BookingDto> Get(int id) => service.GetAsync(id);

    [HttpPost("hold")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Hold(HoldRequest req)
    {
        var b = await service.HoldAsync(req);
        return CreatedAtAction(nameof(Get), new { id = b.Id }, b);
    }

    [HttpPost("{id:int}/cancel")]
    public Task<BookingDto> Cancel(int id) => service.CancelHoldAsync(id);
}
