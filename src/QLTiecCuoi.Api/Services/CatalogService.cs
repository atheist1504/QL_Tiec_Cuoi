using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services;

/// <summary>Quản lý danh mục món ăn và dịch vụ.</summary>
public class CatalogService(AppDbContext db)
{
    public async Task<List<MenuItemDto>> GetMenuAsync(bool includeInactive) =>
        await db.MenuItems.Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Category).ThenBy(x => x.Name)
            .Select(x => new MenuItemDto(x.Id, x.Name, x.Category, x.PricePerTable, x.IsActive))
            .ToListAsync();

    public async Task<MenuItemDto> SaveMenuItemAsync(int? id, MenuItemUpsert req)
    {
        Validate(req.Name, req.PricePerTable);
        if (!Enum.IsDefined(req.Category))
            throw new AppException(ErrorKind.Validation, "Nhóm món không hợp lệ.");

        var item = id is null ? new MenuItem() : await db.MenuItems.FindAsync(id.Value)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy món ăn.");
        await EnsureUniqueName(db.MenuItems.Where(x => x.Id != item.Id).Select(x => x.Name), req.Name, "Món ăn");

        item.Name = req.Name.Trim();
        item.Category = req.Category;
        item.PricePerTable = req.PricePerTable;
        item.IsActive = req.IsActive;
        if (id is null) db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        return new MenuItemDto(item.Id, item.Name, item.Category, item.PricePerTable, item.IsActive);
    }

    public async Task<List<ServiceItemDto>> GetServicesAsync(bool includeInactive) =>
        await db.ServiceItems.Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new ServiceItemDto(x.Id, x.Name, x.Unit, x.Price, x.IsActive))
            .ToListAsync();

    public async Task<ServiceItemDto> SaveServiceAsync(int? id, ServiceItemUpsert req)
    {
        Validate(req.Name, req.Price);
        if (string.IsNullOrWhiteSpace(req.Unit))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập đơn vị tính.");

        var item = id is null ? new ServiceItem() : await db.ServiceItems.FindAsync(id.Value)
            ?? throw new AppException(ErrorKind.NotFound, "Không tìm thấy dịch vụ.");
        await EnsureUniqueName(db.ServiceItems.Where(x => x.Id != item.Id).Select(x => x.Name), req.Name, "Dịch vụ");

        item.Name = req.Name.Trim();
        item.Unit = req.Unit.Trim();
        item.Price = req.Price;
        item.IsActive = req.IsActive;
        if (id is null) db.ServiceItems.Add(item);
        await db.SaveChangesAsync();
        return new ServiceItemDto(item.Id, item.Name, item.Unit, item.Price, item.IsActive);
    }

    private static void Validate(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new AppException(ErrorKind.Validation, "Vui lòng nhập tên.");
        if (price <= 0 || price != decimal.Truncate(price))
            throw new AppException(ErrorKind.Validation, "Giá phải là số nguyên dương (đồng).");
    }

    private static async Task EnsureUniqueName(IQueryable<string> others, string name, string what)
    {
        var n = name.Trim();
        if (await others.AnyAsync(x => x == n))
            throw new AppException(ErrorKind.Conflict, $"{what} \"{n}\" đã có trong danh mục.");
    }
}
