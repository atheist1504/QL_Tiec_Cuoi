using System.Globalization;
using System.Windows.Media;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop.ViewModels;

/// <summary>Một dòng trên lịch: một ngày + một ca, mỗi cột là một sảnh.</summary>
public class ScheduleRow
{
    private static readonly CultureInfo Vi = new("vi-VN");

    public required DateOnly Date { get; init; }
    public required Shift Shift { get; init; }
    public required List<SlotCell> Cells { get; init; }

    public string DateText => Date.ToString("dd/MM/yyyy");
    public string DayText => Vi.TextInfo.ToTitleCase(Vi.DateTimeFormat.GetDayName(Date.DayOfWeek));
    public string ShiftText => Labels.Of(Shift);
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}

public class SlotCell(SlotDto slot)
{
    private static readonly Brush Free = Frozen("#E3F4E6");
    private static readonly Brush Held = Frozen("#FFF1C9");
    private static readonly Brush Deposited = Frozen("#F8D7DA");

    public SlotDto Slot { get; } = slot;

    public string Text => Slot.Status switch
    {
        SlotStatus.Available => "Còn trống",
        SlotStatus.Held => $"Giữ: {Slot.CustomerName}",
        SlotStatus.Deposited => $"Cọc: {Slot.CustomerName}",
        _ => "",
    };

    public string ToolTip => Slot.Status switch
    {
        SlotStatus.Available => "Bấm để giữ sảnh cho khách",
        SlotStatus.Held => $"Giữ chỗ đến {Slot.HoldExpiresAt?.ToLocalTime():HH:mm dd/MM/yyyy}. Bấm để hủy giữ chỗ.",
        SlotStatus.Deposited => "Đã đặt cọc",
        _ => "",
    };

    public Brush Background => Slot.Status switch
    {
        SlotStatus.Available => Free,
        SlotStatus.Held => Held,
        _ => Deposited,
    };

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
