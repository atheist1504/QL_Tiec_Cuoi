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

public static class Labels
{
    public static string Of(Shift shift) => shift switch
    {
        Shift.Lunch => "Trưa",
        Shift.Dinner => "Tối",
        _ => shift.ToString(),
    };

    public static string Of(SlotStatus status) => status switch
    {
        SlotStatus.Available => "Còn trống",
        SlotStatus.Held => "Đang giữ chỗ",
        SlotStatus.Deposited => "Đã đặt cọc",
        _ => status.ToString(),
    };
}
