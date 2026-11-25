using Microsoft.AspNetCore.Mvc;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractsController(ContractService service, PdfService pdf) : ControllerBase
{
    /// <summary>Lập hợp đồng từ báo giá đã gắn với lượt giữ sảnh.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContractDto>> Create(CreateContractRequest req)
    {
        var c = await service.CreateFromQuoteAsync(req);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, c);
    }

    [HttpGet("{id:int}")]
    public Task<ContractDto> Get(int id) => service.GetAsync(id);

    [HttpGet]
    public Task<List<ContractDto>> List([FromQuery] int? bookingId) => service.ListAsync(bookingId);

    /// <summary>Ghi nhận tiền khách trả trước (đặt cọc). Đủ mức cọc thì sảnh được chốt.</summary>
    [HttpPost("{id:int}/payments")]
    public Task<ContractDto> Pay(int id, PaymentRequest req) => service.RecordPaymentAsync(id, req);

    [HttpPost("{id:int}/cancel")]
    public Task<ContractDto> Cancel(int id) => service.CancelAsync(id);

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var (bytes, name) = await pdf.ContractPdfAsync(id);
        return File(bytes, "application/pdf", name);
    }
}
