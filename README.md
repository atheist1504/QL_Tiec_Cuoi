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

App desktop lấy địa chỉ API theo thứ tự: biến môi trường `QLTC_API_URL`, rồi file `api-url.txt` cạnh file `.exe` (dòng đầu tiên không bắt đầu bằng `#`), cuối cùng là `http://localhost:5100/`. File `api-url.txt` trong repo đang trỏ tới máy chủ đã triển khai (mục **Triển khai**); khi chạy API trên máy mình thì đặt biến môi trường trước khi mở app:

```powershell
$env:QLTC_API_URL = "http://localhost:5100/"
dotnet run --project src/QLTiecCuoi.Desktop
```

Ở môi trường Development, `appsettings.Development.json` bật `Contract:AllowLiquidationBeforeEvent` để demo được thanh lý trước ngày tiệc. Khi triển khai thật, tùy chọn này tắt: chỉ thanh lý từ ngày tổ chức trở đi.

## Kiểm thử

```powershell
dotnet test
```

## Chức năng đã có

- [x] Lịch sảnh theo ca (trưa, tối), giữ sảnh tạm 48 giờ, hủy giữ chỗ
- [x] Chống trùng sảnh: kiểm tra ở service + chỉ mục UNIQUE `(HallId, EventDate, Shift)` cho lượt chưa hủy
- [x] Danh mục thực đơn (giá theo bàn) và dịch vụ, thêm/sửa/ngừng bán
- [x] Báo giá: tiền sảnh + thực đơn × số bàn + dịch vụ + VAT 8% (đổi tại `Quote:VatPercent`), xem trước, lưu với giá tại thời điểm lập
- [x] Xuất báo giá PDF
- [x] Hợp đồng lập từ báo giá, ghi nhận đặt cọc (mặc định 30%), đủ cọc thì chốt sảnh; xuất hợp đồng PDF (QuestPDF)
- [x] Thanh lý hợp đồng: tính lại theo số bàn thực tế + phát sinh, trừ tiền cọc, thu tiền cuối hoặc hoàn lại
- [x] Báo cáo doanh thu mùa cưới theo tháng và theo sảnh (đã thanh lý và dự kiến)
- [x] Chatbot AI (tool calling): tra sảnh trống, thực đơn, tính báo giá; giữ sảnh và lưu báo giá sau khi nhân viên xác nhận. Mặc định dùng Gemini (gói miễn phí), đổi sang Claude bằng cấu hình

## Chatbot

Chatbot chạy trên back-end API, dùng tool calling. Nhà cung cấp LLM chọn bằng `Chat:Provider` trong `appsettings.json`:

| Provider | Kết nối | Key (user-secrets / biến môi trường) | Model mặc định |
| --- | --- | --- | --- |
| `Gemini` (mặc định) | API chuẩn OpenAI của Google, gọi bằng HttpClient | `Gemini:ApiKey` / `GEMINI_API_KEY` | `gemini-3.5-flash-lite` |
| `Claude` | SDK chính thức `Anthropic` (Messages API) | `Anthropic:ApiKey` / `ANTHROPIC_API_KEY` | `claude-opus-5-5` |
| `OpenAICompatible` | Bất kỳ dịch vụ chuẩn OpenAI (Groq, OpenRouter, Ollama...), đặt thêm `Chat:BaseUrl` | `Chat:ApiKey` / `CHAT_API_KEY` | đặt `Chat:Model` |

API key chỉ nằm trên máy chủ API, không nằm trong app desktop hay trong GitHub.

### Dùng Gemini (miễn phí)

1. Vào https://aistudio.google.com, đăng nhập Google, chọn **Get API key** → **Create API key**.
2. Lưu key bằng user-secrets (key nằm ngoài thư mục dự án nên không bị commit):

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<api key của bạn>" --project src/QLTiecCuoi.Api
```

3. Khởi động lại API, mở app, bấm **Trợ lý AI**.

Lưu ý gói miễn phí: giới hạn số lượt theo phút và theo ngày (xem https://aistudio.google.com/rate-limit); hết lượt thì chatbot báo "Đã hết lượt gọi…", đợi rồi thử lại.
Model hay bị quá tải (HTTP 503) vào giờ cao điểm; API tự thử lại 2 lần. Đo ngày 26/11/2026 với câu hỏi báo giá:
`gemini-3.5-flash-lite` khoảng 8 giây, không lỗi; `gemini-4-hydrogen` khoảng 33 giây vì bị 503 hai lần; `gemini-3.6-flash` quá tải liên tục.
Chatbot đặt `Effort` = `low` vì việc tra cứu không cần suy nghĩ sâu, và mọi con số đều do API nghiệp vụ tính.
Theo điều khoản, dữ liệu gửi qua gói miễn phí có thể được Google dùng để cải thiện sản phẩm, nên chỉ dùng dữ liệu mẫu, không đưa dữ liệu khách thật.
Nếu tên model không còn hỗ trợ, sửa `Chat:Model` theo danh sách ở https://aistudio.google.com/docs/models.

### Dùng Claude (trả phí, phương án dự phòng khi demo)

Đặt `"Provider": "Claude"` và `"Model": "claude-haiku-5-5"` (rẻ nhất) hoặc bỏ trống `Model` để dùng `claude-opus-5-5`, rồi:

```powershell
dotnet user-secrets set "Anthropic:ApiKey" "<api key của bạn>" --project src/QLTiecCuoi.Api
```

Với các model hỗ trợ (Opus, Sonnet 5.5, Fable), yêu cầu gửi kèm dự phòng phía máy chủ (`fallbacks: default`): bộ lọc an toàn từ chối nhầm thì tự chuyển sang model dự phòng. Haiku không có tính năng này.

### Cấu hình khác (mục `Chat`)

| Khóa | Mặc định | Ý nghĩa |
| --- | --- | --- |
| `Effort` | (theo model) | Mức suy nghĩ: `low`, `medium`, `high` |
| `MaxSteps` | 8 | Số lần gọi model tối đa cho một câu hỏi |
| `ConversationTtlMinutes` | 120 | Hội thoại bỏ không quá lâu thì bị xóa |
| `AllowNoApiKey` | false | Bật khi dùng Ollama trên máy (không cần key) |

Công cụ chatbot dùng: `list_halls`, `find_available_halls`, `find_bookings` (tra tiệc đã giữ chỗ / đã cọc theo ngày, tên hoặc số điện thoại khách),
`get_menu_and_services`, `estimate_quote` (chỉ đọc, chạy ngay);
`hold_hall`, `create_quote` (ghi dữ liệu, chỉ chạy khi nhân viên bấm **Xác nhận**). Mọi con số lấy từ API nghiệp vụ, chatbot không tự tính giá.

## Triển khai (SmarterASP.NET)

API và CSDL đang chạy ở gói dùng thử Windows 60 ngày của SmarterASP.NET (hết hạn **24/01/2027**):

- API: https://shiroiarika-001-site1.jtempurl.com/ (Swagger ở `/swagger`, kiểm tra tình trạng ở `/api/health`)
- CSDL: SQL Server 2022 `sql8011.site4now.net`, database `db_ad18cc_qltieccuoi`

Tài khoản SmarterASP phải bật xác thực hai yếu tố, nếu không địa chỉ tạm `*.jtempurl.com` bị khóa bằng mật khẩu và app desktop không gọi được API.

### Cấu hình trên máy chủ

- `appsettings.Production.json` (có trong repo): `Database:MigrateOnStartup = true` (tự áp dụng migration khi khởi động, lỗi CSDL không làm sập API) và `Swagger:Enabled = true`. Môi trường Production không bật `Contract:AllowLiquidationBeforeEvent`.
- `appsettings.Secrets.json` (**chỉ có trên máy chủ**, không commit, không nằm trong gói publish): chuỗi kết nối và key Gemini. Mẫu ở `src/QLTiecCuoi.Api/appsettings.Secrets.example.json`. Mật khẩu CSDL nên chỉ gồm chữ, số và `!` để khỏi phá chuỗi kết nối.

### Cập nhật API lên máy chủ

```powershell
dotnet publish src/QLTiecCuoi.Api -c Release -o publish/api
```

1. Trong Bảng điều khiển → Trang web → site1 → **Tệp**, mở `/site1`, tạo file `app_offline.htm` (nội dung tùy ý) để tạm dừng API; nếu không, file `.dll` đang chạy bị khóa và upload báo lỗi.
2. Xóa rồi upload lại các file đã đổi từ `publish/api` (thường chỉ `QLTiecCuoi.Api.dll`, `QLTiecCuoi.Shared.dll`). Thay nhiều file thì nén `publish/api` thành zip, upload rồi **Giải nén** vào `/site1`.
3. Xóa `app_offline.htm`, mở `/api/health` kiểm tra: `secretsFileFound` và `canConnect` phải là `true`.

Khi API báo *HTTP Error 500.30*, sửa `web.config` trên máy chủ thành `stdoutLogEnabled="true"`, tạo thư mục `logs`, mở lại trang rồi đọc file `logs/stdout_*.log`.

Áp migration từ máy dev (không cần chờ máy chủ tự chạy), chuỗi kết nối nạp từ file bí mật qua biến môi trường để không gõ mật khẩu vào lệnh:

```powershell
$env:ConnectionStrings__Default = (Get-Content publish\appsettings.Secrets.json -Raw | ConvertFrom-Json).ConnectionStrings.Default
dotnet ef database update --project src/QLTiecCuoi.Api
Remove-Item Env:ConnectionStrings__Default
```

Phương án dự phòng khi hết hạn dùng thử: chạy API trên laptop và mở ra Internet bằng Cloudflare Tunnel (`cloudflared tunnel --url http://localhost:5100`), rồi sửa `api-url.txt` theo địa chỉ `trycloudflare.com` mới.

## Tài liệu quản lý dự án

- `docs/pm/jira-backlog.csv`: backlog để nhập vào Jira (*Project settings → External system import → CSV*). Nhánh WBS cấp 1 là Epic, gói công việc là Task; `Original Estimate` tính bằng giây.
- `docs/pm/wbs-msproject.csv`: danh sách công việc để mở bằng MS Project (*File → Open → CSV → Import Wizard*). Ngày theo dạng dd/MM/yyyy.
- Sửa WBS trong `docs/pm/export-wbs.ps1` rồi chạy lại để sinh lại hai file trên:

```powershell
powershell -ExecutionPolicy Bypass -File docs\pm\export-wbs.ps1
```

## Bảo mật

Không commit API key. Đặt khóa trong biến môi trường hoặc `dotnet user-secrets`; trên máy chủ đặt trong `appsettings.Secrets.json` (đã có trong `.gitignore`).
