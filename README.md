# Phần mềm quản lý nhà hàng tiệc cưới (QL34-D, nhóm T1)

Ứng dụng desktop Windows + back-end API quản lý sảnh, thực đơn, hợp đồng, tích hợp chatbot AI.

Thành viên: Nguyễn Văn Đạt, Trần Quang Đức.

## Cấu trúc

| Thư mục | Nội dung |
| --- | --- |
| `src/QLTiecCuoi.Api` | ASP.NET Core 8 Web API, EF Core, SQL Server |
| `src/QLTiecCuoi.Desktop` | Ứng dụng WPF cho nhân viên |
| `src/QLTiecCuoi.Shared` | Kiểu dữ liệu dùng chung (DTO, enum) |
| `tests/QLTiecCuoi.Api.Tests` | Kiểm thử xUnit (SQLite in-memory) |

## Yêu cầu

- .NET 8 SDK
- SQL Server Express (instance `.\SQLEXPRESS`), hoặc sửa `ConnectionStrings:Default` trong `src/QLTiecCuoi.Api/appsettings.json`

## Chạy lần đầu

```powershell
dotnet tool restore
dotnet ef database update --project src/QLTiecCuoi.Api
dotnet run --project src/QLTiecCuoi.Api --launch-profile http
```

API chạy ở http://localhost:5100, tài liệu Swagger ở http://localhost:5100/swagger.

Mở cửa sổ PowerShell khác để chạy app desktop:

```powershell
dotnet run --project src/QLTiecCuoi.Desktop
```

Đổi địa chỉ API của app desktop bằng biến môi trường `QLTC_API_URL`.

## Kiểm thử

```powershell
dotnet test
```

## Chức năng đã có

- [x] Lịch sảnh theo ca (trưa, tối), giữ sảnh tạm 48 giờ, hủy giữ chỗ
- [x] Chống trùng sảnh: kiểm tra ở service + chỉ mục UNIQUE `(HallId, EventDate, Shift)` cho lượt chưa hủy
- [ ] Thực đơn, dịch vụ, báo giá
- [ ] Hợp đồng, đặt cọc
- [ ] Thanh lý hợp đồng
- [ ] Báo cáo doanh thu mùa cưới
- [ ] Chatbot AI (Claude API)

## Bảo mật

Không commit API key. Đặt khóa trong biến môi trường hoặc `dotnet user-secrets`.
