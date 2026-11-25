using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Data;

public class Hall
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MaxTables { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Booking
{
    public int Id { get; set; }
    public int HallId { get; set; }
    public Hall Hall { get; set; } = null!;
    public DateOnly EventDate { get; set; }
    public Shift Shift { get; set; }
    public BookingStatus Status { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public int ExpectedTables { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MenuItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public MenuCategory Category { get; set; }
    public decimal PricePerTable { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ServiceItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Báo giá. Giá được chụp lại tại thời điểm lập (snapshot) để khi thực đơn
/// đổi giá sau này, báo giá cũ vẫn giữ nguyên số tiền đã gửi khách.
/// Đổi thực đơn = lập báo giá mới cho cùng lượt đặt sảnh.
/// </summary>
public class Quote
{
    public int Id { get; set; }
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int HallId { get; set; }
    public Hall Hall { get; set; } = null!;
    public DateOnly EventDate { get; set; }
    public Shift Shift { get; set; }
    public int Tables { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public decimal HallPrice { get; set; }
    public decimal MenuTotal { get; set; }
    public decimal ServiceTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatPercent { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly ValidUntil { get; set; }
    public List<QuoteLine> Lines { get; set; } = [];
}

/// <summary>
/// Hợp đồng lập từ một báo giá đã gắn với lượt giữ sảnh. Giá trị hợp đồng lấy
/// từ báo giá (báo giá không đổi sau khi lưu). Đủ cọc thì sảnh chuyển sang "đã cọc".
/// </summary>
public class Contract
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public ContractStatus Status { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string? CustomerAddress { get; set; }
    public string? CustomerIdNumber { get; set; }
    public decimal Total { get; set; }
    public decimal DepositPercent { get; set; }
    public decimal DepositRequired { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Payment> Payments { get; set; } = [];

    // Thanh lý (điền khi Status = Liquidated)
    public DateTime? LiquidatedAt { get; set; }
    public int? ActualTables { get; set; }
    public decimal? FinalSubtotal { get; set; }
    public decimal? FinalVat { get; set; }
    public decimal? FinalTotal { get; set; }
    public string? LiquidationNote { get; set; }
    public List<ContractExtra> Extras { get; set; } = [];

    /// <summary>Số tiền khách phải trả: sau thanh lý là giá trị quyết toán, trước đó là giá trị hợp đồng.</summary>
    public decimal Payable => FinalTotal ?? Total;
}

/// <summary>Khoản phát sinh khi thanh lý (thêm đồ uống, phụ thu...).</summary>
public class ContractExtra
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public PaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Note { get; set; }
}

public enum QuoteLineKind { Menu = 1, Service = 2 }

public class QuoteLine
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public QuoteLineKind Kind { get; set; }
    public int? MenuItemId { get; set; }
    public int? ServiceItemId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}
