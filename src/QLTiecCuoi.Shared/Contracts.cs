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

    public static string Money(decimal v) => v.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";

    public static string Of(SlotStatus status) => status switch
    {
        SlotStatus.Available => "Còn trống",
        SlotStatus.Held => "Đang giữ chỗ",
        SlotStatus.Deposited => "Đã đặt cọc",
        _ => status.ToString(),
    };
}
