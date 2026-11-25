namespace QLTiecCuoi.Shared;

/// <summary>Ca tiệc trong ngày.</summary>
public enum Shift
{
    Lunch = 1,  // Trưa
    Dinner = 2, // Tối
}

/// <summary>Trạng thái một lượt đặt sảnh.</summary>
public enum BookingStatus
{
    Held = 1,      // Giữ chỗ tạm, có hạn
    Deposited = 2, // Đã đặt cọc, sảnh được chốt
    Cancelled = 3, // Đã hủy hoặc giữ chỗ hết hạn
}

/// <summary>Trạng thái của một ô (sảnh, ngày, ca) trên lịch.</summary>
public enum SlotStatus
{
    Available = 0,
    Held = 1,
    Deposited = 2,
}

public record HallDto(int Id, string Name, int MaxTables, decimal BasePrice);

public record SlotDto(
    int HallId,
    string HallName,
    DateOnly Date,
    Shift Shift,
    SlotStatus Status,
    int? BookingId,
    string? CustomerName,
    DateTime? HoldExpiresAt);

public record HoldRequest(
    int HallId,
    DateOnly Date,
    Shift Shift,
    string CustomerName,
    string CustomerPhone,
    int ExpectedTables,
    string? Note);

public record BookingDto(
    int Id,
    int HallId,
    string HallName,
    DateOnly Date,
    Shift Shift,
    BookingStatus Status,
    string CustomerName,
    string CustomerPhone,
    int ExpectedTables,
    DateTime? HoldExpiresAt,
    string? Note);

public record ErrorDto(string Message);

// ----- Thực đơn và dịch vụ -----

/// <summary>Nhóm món trong thực đơn tiệc.</summary>
public enum MenuCategory
{
    Appetizer = 1, // Khai vị
    Main = 2,      // Món chính
    Hotpot = 3,    // Lẩu
    Dessert = 4,   // Tráng miệng
}

/// <summary>Món ăn, giá tính cho một bàn (10 khách).</summary>
public record MenuItemDto(int Id, string Name, MenuCategory Category, decimal PricePerTable, bool IsActive);

public record MenuItemUpsert(string Name, MenuCategory Category, decimal PricePerTable, bool IsActive = true);

/// <summary>Dịch vụ đi kèm (trang trí, MC, đồ uống...), giá theo đơn vị.</summary>
public record ServiceItemDto(int Id, string Name, string Unit, decimal Price, bool IsActive);

public record ServiceItemUpsert(string Name, string Unit, decimal Price, bool IsActive = true);

// ----- Báo giá -----

public record QuoteServiceRequest(int ServiceItemId, int Quantity);

public record QuoteRequest(
    int HallId,
    DateOnly Date,
    Shift Shift,
    int Tables,
    string CustomerName,
    string CustomerPhone,
    List<int> MenuItemIds,
    List<QuoteServiceRequest> Services,
    int? BookingId = null,
    string? Note = null);

public record QuoteLineDto(string Name, decimal UnitPrice, int Quantity, string Unit, decimal Amount);

public record QuoteDto(
    int? Id,
    int? BookingId,
    int HallId,
    string HallName,
    DateOnly Date,
    Shift Shift,
    int Tables,
    string CustomerName,
    string CustomerPhone,
    decimal HallPrice,
    List<QuoteLineDto> MenuLines,
    List<QuoteLineDto> ServiceLines,
    decimal MenuTotal,
    decimal ServiceTotal,
    decimal Subtotal,
    decimal VatPercent,
    decimal VatAmount,
    decimal Total,
    SlotStatus SlotStatus,
    DateTime? CreatedAt,
    DateOnly? ValidUntil,
    string? Note);

// ----- Hợp đồng và thanh toán -----

public enum ContractStatus
{
    AwaitingDeposit = 1, // Đã lập, chờ đặt cọc
    Deposited = 2,       // Đã đủ cọc, sảnh được chốt
    Liquidated = 3,      // Đã thanh lý sau tiệc
    Cancelled = 4,       // Đã hủy
}

public enum PaymentMethod
{
    Cash = 1,     // Tiền mặt
    Transfer = 2, // Chuyển khoản
}

public enum PaymentKind
{
    Deposit = 1, // Đặt cọc
    Final = 2,   // Thanh toán khi thanh lý
}

public record CreateContractRequest(int QuoteId, string? CustomerAddress, string? CustomerIdNumber, string? Note);

public record PaymentRequest(decimal Amount, PaymentMethod Method, string? Note);

public record PaymentDto(int Id, PaymentKind Kind, decimal Amount, PaymentMethod Method, DateTime PaidAt, string? Note);

public record ContractDto(
    int Id,
    string Code,
    ContractStatus Status,
    int BookingId,
    int QuoteId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerAddress,
    string? CustomerIdNumber,
    string HallName,
    DateOnly Date,
    Shift Shift,
    int Tables,
    decimal Total,
    decimal DepositPercent,
    decimal DepositRequired,
    decimal Paid,
    decimal Remaining,
    DateTime? HoldExpiresAt,
    DateTime CreatedAt,
    string? Note,
    List<PaymentDto> Payments,
    LiquidationDto? Liquidation = null);

// ----- Thanh lý -----

public record ExtraChargeDto(string Description, decimal Amount);

public record LiquidationRequest(int ActualTables, List<ExtraChargeDto> Extras, string? Note);

/// <summary>Kết quả thanh lý: tính lại theo số bàn thực tế và phát sinh.</summary>
public record LiquidationDto(
    int ContractedTables,
    int ActualTables,
    decimal HallPrice,
    decimal MenuPerTable,
    decimal MenuTotal,
    decimal ServiceTotal,
    List<ExtraChargeDto> Extras,
    decimal ExtraTotal,
    decimal Subtotal,
    decimal VatPercent,
    decimal VatAmount,
    decimal FinalTotal,
    decimal ContractTotal,
    decimal Paid,
    decimal Remaining,
    DateTime? LiquidatedAt,
    string? Note);

// ----- Chatbot -----

public record ChatRequest(string? ConversationId, string Message);

public record ChatConfirmRequest(bool Approve);

/// <summary>Thao tác ghi dữ liệu chatbot đề xuất, chờ nhân viên xác nhận.</summary>
public record ChatPendingActionDto(string Title, string Details);

public record ChatReplyDto(
    string ConversationId,
    string Text,
    List<ChatPendingActionDto> PendingActions,
    List<string> ToolsUsed);

// ----- Báo cáo doanh thu -----

public record RevenueRowDto(string Label, int Events, int Tables, decimal Revenue, int UpcomingEvents, decimal UpcomingValue);

public record RevenueReportDto(DateOnly From, DateOnly To, List<RevenueRowDto> ByMonth, List<RevenueRowDto> ByHall, RevenueRowDto Total);

public record QuoteSummaryDto(int Id, string CustomerName, string CustomerPhone, string HallName,
    DateOnly Date, Shift Shift, int Tables, decimal Total, DateTime CreatedAt);

public static class Labels
{
    public static string Of(Shift shift) => shift switch
    {
        Shift.Lunch => "Trưa",
        Shift.Dinner => "Tối",
        _ => shift.ToString(),
    };

    public static string Of(MenuCategory c) => c switch
    {
        MenuCategory.Appetizer => "Khai vị",
        MenuCategory.Main => "Món chính",
        MenuCategory.Hotpot => "Lẩu",
        MenuCategory.Dessert => "Tráng miệng",
        _ => c.ToString(),
    };

    public static string Of(ContractStatus s) => s switch
    {
        ContractStatus.AwaitingDeposit => "Chờ đặt cọc",
        ContractStatus.Deposited => "Đã đặt cọc",
        ContractStatus.Liquidated => "Đã thanh lý",
        ContractStatus.Cancelled => "Đã hủy",
        _ => s.ToString(),
    };

    public static string Of(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.Transfer => "Chuyển khoản",
        _ => m.ToString(),
    };

    public static string Of(PaymentKind k) => k switch
    {
        PaymentKind.Deposit => "Đặt cọc",
        PaymentKind.Final => "Thanh toán cuối",
        _ => k.ToString(),
    };

    public static string Money(decimal v) => v.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";

    public static string Of(SlotStatus status) => status switch
    {
        SlotStatus.Available => "Còn trống",
        SlotStatus.Held => "Đang giữ chỗ",
        SlotStatus.Deposited => "Đã đặt cọc",
        _ => status.ToString(),
    };
}
