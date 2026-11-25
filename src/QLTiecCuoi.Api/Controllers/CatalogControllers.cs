using Microsoft.AspNetCore.Mvc;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Controllers;

[ApiController]
[Route("api/menu-items")]
public class MenuItemsController(CatalogService service) : ControllerBase
{
    [HttpGet]
    public Task<List<MenuItemDto>> GetAll([FromQuery] bool includeInactive = false) => service.GetMenuAsync(includeInactive);

    [HttpPost]
    public Task<MenuItemDto> Create(MenuItemUpsert req) => service.SaveMenuItemAsync(null, req);

    [HttpPut("{id:int}")]
    public Task<MenuItemDto> Update(int id, MenuItemUpsert req) => service.SaveMenuItemAsync(id, req);
}

[ApiController]
[Route("api/services")]
public class ServiceItemsController(CatalogService service) : ControllerBase
{
    [HttpGet]
    public Task<List<ServiceItemDto>> GetAll([FromQuery] bool includeInactive = false) => service.GetServicesAsync(includeInactive);

    [HttpPost]
    public Task<ServiceItemDto> Create(ServiceItemUpsert req) => service.SaveServiceAsync(null, req);

    [HttpPut("{id:int}")]
    public Task<ServiceItemDto> Update(int id, ServiceItemUpsert req) => service.SaveServiceAsync(id, req);
}

[ApiController]
[Route("api/quotes")]
public class QuotesController(QuoteService service, PdfService pdf) : ControllerBase
{
    /// <summary>Tải báo giá dạng PDF để gửi khách.</summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var (bytes, name) = await pdf.QuotePdfAsync(id);
        return File(bytes, "application/pdf", name);
    }

    /// <summary>Tính thử báo giá, không lưu.</summary>
    [HttpPost("preview")]
    public Task<QuoteDto> Preview(QuoteRequest req) => service.PreviewAsync(req);

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<QuoteDto>> Create(QuoteRequest req)
    {
        var q = await service.CreateAsync(req);
        return CreatedAtAction(nameof(Get), new { id = q.Id }, q);
    }

    [HttpGet("{id:int}")]
    public Task<QuoteDto> Get(int id) => service.GetAsync(id);

    [HttpGet]
    public Task<List<QuoteSummaryDto>> List([FromQuery] string? phone, [FromQuery] int? bookingId) =>
        service.ListAsync(phone, bookingId);
}
