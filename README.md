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

Ở môi trường Development, `appsettings.Development.json` bật `Contract:AllowLiquidationBeforeEvent` để demo được thanh lý trước ngày tiệc. Khi triển khai thật, tùy chọn này tắt: chỉ thanh lý từ ngày tổ chức trở đi.

## Kiểm thử

```powershell
dotnet test
```

## Chức năng đã có

- [x] Lịch sảnh theo ca (trưa, tối), giữ sảnh tạm 48 giờ, hủy giữ chỗ
- [x] Chống trùng sảnh: kiểm tra ở service + chỉ mục UNIQUE `(HallId, EventDate, Shift)` cho lượt chưa hủy
- [x] Danh mục thực đơn (giá theo bàn) và dịch vụ, thêm/sửa/ngừng bán
- [x] Báo giá: tiền sảnh + thực đơn × số bàn + dịch vụ + VAT 10%, xem trước, lưu với giá tại thời điểm lập
- [x] Xuất báo giá PDF
- [x] Hợp đồng lập từ báo giá, ghi nhận đặt cọc (mặc định 30%), đủ cọc thì chốt sảnh; xuất hợp đồng PDF (QuestPDF)
- [x] Thanh lý hợp đồng: tính lại theo số bàn thực tế + phát sinh, trừ tiền cọc, thu tiền cuối hoặc hoàn lại
- [x] Báo cáo doanh thu mùa cưới theo tháng và theo sảnh (đã thanh lý và dự kiến)
- [x] Chatbot AI (Claude API, tool calling): tra sảnh trống, thực đơn, tính báo giá; giữ sảnh và lưu báo giá sau khi nhân viên xác nhận

## Chatbot

Chatbot chạy trên back-end API, gọi Claude qua SDK chính thức `Anthropic` (Messages API + tool calling).
API key chỉ nằm trên máy chủ API, không nằm trong app desktop hay trong GitHub.

1. Tạo API key tại https://platform.claude.com (mục API keys) và nạp tiền vào tài khoản.
2. Lưu key bằng user-secrets (key nằm ngoài thư mục dự án nên không bị commit):

```powershell
dotnet user-secrets set "Anthropic:ApiKey" "<api key của bạn>" --project src/QLTiecCuoi.Api
```

   Hoặc đặt biến môi trường `ANTHROPIC_API_KEY` trên máy chủ.
3. Khởi động lại API, mở app, bấm **Trợ lý AI**.

| Cấu hình (`appsettings.json`, mục `Chat`) | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `Model` | `claude-opus-5-5` | Model Claude |
| `Effort` | `medium` | Mức suy nghĩ: `low` rẻ và nhanh hơn, `high` kỹ hơn |
| `MaxSteps` | 8 | Số lần gọi Claude tối đa cho một câu hỏi |
| `ConversationTtlMinutes` | 120 | Hội thoại bỏ không quá lâu thì bị xóa |

Công cụ chatbot dùng: `list_halls`, `find_available_halls`, `get_menu_and_services`, `estimate_quote` (chỉ đọc, chạy ngay);
`hold_hall`, `create_quote` (ghi dữ liệu, chỉ chạy khi nhân viên bấm **Xác nhận**). Mọi con số lấy từ API nghiệp vụ, chatbot không tự tính giá.
Yêu cầu gửi kèm tính năng dự phòng phía máy chủ (`server-side-fallback-2026-07-01`, `fallbacks: default`): nếu bộ lọc an toàn từ chối nhầm, Claude tự chuyển sang model dự phòng.

## Tài liệu quản lý dự án

- `docs/pm/jira-backlog.csv`: backlog để nhập vào Jira (*Project settings → External system import → CSV*). Nhánh WBS cấp 1 là Epic, gói công việc là Task; `Original Estimate` tính bằng giây.
- `docs/pm/wbs-msproject.csv`: danh sách công việc để mở bằng MS Project (*File → Open → CSV → Import Wizard*). Ngày theo dạng dd/MM/yyyy.
- Sửa WBS trong `docs/pm/export-wbs.ps1` rồi chạy lại để sinh lại hai file trên:

```powershell
powershell -ExecutionPolicy Bypass -File docs\pm\export-wbs.ps1
```

## Bảo mật

Không commit API key. Đặt khóa trong biến môi trường hoặc `dotnet user-secrets`.
