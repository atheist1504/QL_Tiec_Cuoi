using System.ComponentModel;
using System.Runtime.CompilerServices;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>Một món trên màn hình báo giá, có ô chọn.</summary>
public class MenuChoice(MenuItemDto item) : Observable
{
    private bool _selected;
    public MenuItemDto Item { get; } = item;
    public string Group => Labels.Of(Item.Category);
    public string Text => $"{Item.Name} — {Labels.Money(Item.PricePerTable)}/bàn";
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>Một dịch vụ trên màn hình báo giá, có ô chọn và số lượng.</summary>
public class ServiceChoice(ServiceItemDto item) : Observable
{
    private bool _selected;
    private string _qty = "1";
    public ServiceItemDto Item { get; } = item;
    public string Text => $"{Item.Name} — {Labels.Money(Item.Price)}/{Item.Unit}";
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public string Quantity { get => _qty; set => Set(ref _qty, value); }
}

/// <summary>Một dòng trong bảng xem trước báo giá.</summary>
public record QuoteRow(string Group, string Name, string UnitPrice, string Quantity, string Amount);

/// <summary>Dòng sửa được trong màn hình danh mục món ăn.</summary>
public class MenuRow : Observable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public MenuCategory Category { get; set; } = MenuCategory.Main;
    public decimal PricePerTable { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Dòng sửa được trong màn hình danh mục dịch vụ.</summary>
public class ServiceRow : Observable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "gói";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
