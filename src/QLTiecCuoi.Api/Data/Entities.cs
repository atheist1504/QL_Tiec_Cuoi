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
