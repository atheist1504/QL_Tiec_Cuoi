using System.Globalization;
using System.Text.Json;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services.Chat;

/// <summary>Mô tả một công cụ (tool) cho Claude: tên, mô tả, JSON schema của tham số.</summary>
public record ChatToolSpec(string Name, string Description, Dictionary<string, object> Properties, string[] Required, bool Writes);

/// <summary>
/// Các công cụ chatbot được dùng. Công cụ đọc chạy ngay; công cụ ghi (giữ sảnh, lưu báo giá)
/// chỉ chạy sau khi nhân viên bấm xác nhận trên app (yêu cầu NFR06).
/// Mọi số liệu trả về lấy từ các service nghiệp vụ, chatbot không tự tính giá.
/// </summary>
public class ChatTools(BookingService bookings, CatalogService catalog, QuoteService quotes, TimeProvider clock)
{
    public const int MaxSearchDays = 31;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static object Str(string description) => new { type = "string", description };
    private static object Int(string description) => new { type = "integer", description };
    private static object ShiftProp(bool allowAny) => new
    {
        type = "string",
        @enum = allowAny ? new[] { "Lunch", "Dinner", "Any" } : new[] { "Lunch", "Dinner" },
        description = allowAny ? "Ca tiệc: Lunch = trưa, Dinner = tối, Any = cả hai" : "Ca tiệc: Lunch = trưa, Dinner = tối",
    };
    private static object IdList(string description) => new { type = "array", items = new { type = "integer" }, description };
    private static object ServiceList => new
    {
        type = "array",
        description = "Dịch vụ kèm số lượng; mảng rỗng nếu không chọn dịch vụ",
        items = new
        {
            type = "object",
            properties = new
            {
                service_item_id = new { type = "integer" },
                quantity = new { type = "integer", description = "Số lượng, lớn hơn 0" },
            },
            required = new[] { "service_item_id", "quantity" },
        },
    };

    public static readonly IReadOnlyList<ChatToolSpec> Specs =
    [
        new("list_halls",
            "Liệt kê các sảnh của nhà hàng: id, tên, sức chứa tối đa (bàn, mỗi bàn 10 khách), giá thuê mỗi ca. Dùng khi cần biết sảnh nào hợp với số khách.",
            new(), [], false),
        new("find_available_halls",
            $"Tìm các sảnh còn trống theo khoảng ngày (tối đa {MaxSearchDays} ngày), ca và số bàn tối thiểu. Chỉ trả về ô lịch còn trống.",
            new()
            {
                ["date_from"] = Str("Ngày bắt đầu, dạng yyyy-MM-dd"),
                ["date_to"] = Str("Ngày kết thúc, dạng yyyy-MM-dd; bằng date_from nếu chỉ hỏi một ngày"),
                ["shift"] = ShiftProp(allowAny: true),
                ["min_tables"] = Int("Số bàn khách cần; 0 nếu chưa biết"),
            },
            ["date_from", "date_to", "shift", "min_tables"], false),
        new("get_menu_and_services",
            "Lấy thực đơn đang bán (id, nhóm món, giá mỗi bàn) và dịch vụ (id, đơn vị, giá).",
            new(), [], false),
        new("estimate_quote",
            "Tính thử báo giá (không lưu): tiền sảnh + thực đơn × số bàn + dịch vụ + VAT. Trả về từng dòng, tổng tiền và tình trạng sảnh ngày đó.",
            new()
            {
                ["hall_id"] = Int("Id sảnh"),
                ["date"] = Str("Ngày tổ chức, dạng yyyy-MM-dd"),
                ["shift"] = ShiftProp(allowAny: false),
                ["tables"] = Int("Số bàn"),
                ["menu_item_ids"] = IdList("Id các món đã chọn, ít nhất 1 món"),
                ["services"] = ServiceList,
            },
            ["hall_id", "date", "shift", "tables", "menu_item_ids", "services"], false),
        new("hold_hall",
            "GHI DỮ LIỆU: giữ sảnh tạm 48 giờ cho khách. Chỉ gọi khi đã có đủ sảnh, ngày, ca, tên khách, số điện thoại, số bàn. Nhân viên sẽ xác nhận trên app trước khi thực hiện.",
            new()
            {
                ["hall_id"] = Int("Id sảnh"),
                ["date"] = Str("Ngày tổ chức, dạng yyyy-MM-dd"),
                ["shift"] = ShiftProp(allowAny: false),
                ["customer_name"] = Str("Họ tên khách"),
                ["customer_phone"] = Str("Số điện thoại khách"),
                ["expected_tables"] = Int("Số bàn dự kiến"),
            },
            ["hall_id", "date", "shift", "customer_name", "customer_phone", "expected_tables"], true),
        new("create_quote",
            "GHI DỮ LIỆU: lưu báo giá cho một lượt giữ sảnh đã có (dùng booking_id trả về từ hold_hall). Nhân viên sẽ xác nhận trên app trước khi thực hiện.",
            new()
            {
                ["booking_id"] = Int("Id lượt giữ sảnh"),
                ["menu_item_ids"] = IdList("Id các món đã chọn, ít nhất 1 món"),
                ["services"] = ServiceList,
            },
            ["booking_id", "menu_item_ids", "services"], true),
    ];

    public static bool IsWrite(string name) => Specs.FirstOrDefault(s => s.Name == name)?.Writes ?? false;

    /// <summary>Mô tả ngắn thao tác ghi để hiện cho nhân viên xác nhận.</summary>
    public async Task<ChatPendingActionDto> DescribeAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            switch (name)
            {
                case "hold_hall":
                {
                    var hall = (await bookings.GetHallsAsync()).FirstOrDefault(h => h.Id == GetInt(input, "hall_id"));
                    return new("Giữ sảnh",
                        $"{hall?.Name ?? "Sảnh #" + GetInt(input, "hall_id")}, ca {Labels.Of(GetShift(input, "shift")).ToLower()} " +
                        $"{GetDate(input, "date"):dd/MM/yyyy} cho {GetString(input, "customer_name")} ({GetString(input, "customer_phone")}), " +
                        $"{GetInt(input, "expected_tables")} bàn. Giữ chỗ 48 giờ.");
                }
                case "create_quote":
                {
                    var b = await bookings.GetAsync(GetInt(input, "booking_id"));
                    var menu = GetIntList(input, "menu_item_ids");
                    var services = GetServices(input);
                    return new("Lưu báo giá",
                        $"Cho {b.CustomerName}, {b.HallName} {b.Date:dd/MM/yyyy}: {menu.Count} món, {services.Count} dịch vụ.");
                }
            }
        }
        catch (Exception ex) when (ex is AppException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Mô tả không được thì vẫn cho xác nhận; lỗi thật sẽ báo khi thực hiện.
        }
        return new(name, JsonSerializer.Serialize(input, Json));
    }

    /// <summary>Chạy một công cụ, trả về JSON kết quả. Lỗi nghiệp vụ trả về dạng {"error": "..."}.</summary>
    public async Task<(string Result, bool IsError)> ExecuteAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            object result = name switch
            {
                "list_halls" => await bookings.GetHallsAsync(),
                "find_available_halls" => await FindAvailableAsync(input),
                "get_menu_and_services" => new
                {
                    menu = (await catalog.GetMenuAsync(false)).Select(m => new { m.Id, m.Name, category = Labels.Of(m.Category), price_per_table = m.PricePerTable }),
                    services = (await catalog.GetServicesAsync(false)).Select(s => new { s.Id, s.Name, s.Unit, s.Price }),
                },
                "estimate_quote" => Summarize(await quotes.PreviewAsync(new QuoteRequest(
                    GetInt(input, "hall_id"), GetDate(input, "date"), GetShift(input, "shift"), GetInt(input, "tables"),
                    "Khách (ước tính)", "-", GetIntList(input, "menu_item_ids"), GetServices(input)))),
                "hold_hall" => await HoldAsync(input),
                "create_quote" => await CreateQuoteAsync(input),
                _ => throw new AppException(ErrorKind.Validation, $"Không có công cụ {name}."),
            };
            return (JsonSerializer.Serialize(result, Json), false);
        }
        catch (AppException ex)
        {
            return (JsonSerializer.Serialize(new { error = ex.Message }, Json), true);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return (JsonSerializer.Serialize(new { error = $"Tham số không hợp lệ: {ex.Message}" }, Json), true);
        }
    }

    private async Task<object> FindAvailableAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var from = GetDate(input, "date_from");
        var to = GetDate(input, "date_to");
        if (to.DayNumber - from.DayNumber + 1 > MaxSearchDays)
            throw new AppException(ErrorKind.Validation, $"Chỉ tìm tối đa {MaxSearchDays} ngày một lần.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (from <= today) from = today.AddDays(1);
        if (to < from) return new { available = Array.Empty<object>(), note = "Chỉ nhận đặt từ ngày mai trở đi." };

        var shift = GetString(input, "shift");
        var minTables = input.TryGetValue("min_tables", out var mt) && mt.ValueKind == JsonValueKind.Number ? mt.GetInt32() : 0;
        var halls = (await bookings.GetHallsAsync()).ToDictionary(h => h.Id);
        var free = (await bookings.GetScheduleAsync(from, to))
            .Where(s => s.Status == SlotStatus.Available)
            .Where(s => shift == "Any" || s.Shift.ToString() == shift)
            .Where(s => halls[s.HallId].MaxTables >= minTables)
            .Select(s => new
            {
                hall_id = s.HallId, hall = s.HallName, date = s.Date.ToString("yyyy-MM-dd"),
                weekday = CultureInfo.GetCultureInfo("vi-VN").DateTimeFormat.GetDayName(s.Date.DayOfWeek),
                shift = s.Shift.ToString(), max_tables = halls[s.HallId].MaxTables, hall_price = halls[s.HallId].BasePrice,
            })
            .ToList();
        return new { count = free.Count, available = free.Take(60) };
    }

    private async Task<object> HoldAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var b = await bookings.HoldAsync(new HoldRequest(GetInt(input, "hall_id"), GetDate(input, "date"), GetShift(input, "shift"),
            GetString(input, "customer_name"), GetString(input, "customer_phone"), GetInt(input, "expected_tables"), "Tạo qua chatbot"));
        // Trả giờ địa phương để chatbot không đọc nhầm giờ UTC.
        var expires = b.HoldExpiresAt is { } utc
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone).ToString("HH:mm dd/MM/yyyy")
            : null;
        return new { booking_id = b.Id, hall = b.HallName, date = b.Date.ToString("yyyy-MM-dd"), shift = b.Shift.ToString(), hold_expires_at_local = expires };
    }

    private async Task<object> CreateQuoteAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var b = await bookings.GetAsync(GetInt(input, "booking_id"));
        var q = await quotes.CreateAsync(new QuoteRequest(b.HallId, b.Date, b.Shift, b.ExpectedTables, b.CustomerName, b.CustomerPhone,
            GetIntList(input, "menu_item_ids"), GetServices(input), b.Id, "Tạo qua chatbot"));
        return new { quote_code = $"BG-{q.Id:00000}", summary = Summarize(q) };
    }

    private static object Summarize(QuoteDto q) => new
    {
        hall = q.HallName, date = q.Date.ToString("yyyy-MM-dd"), shift = q.Shift.ToString(), tables = q.Tables,
        hall_price = q.HallPrice,
        menu = q.MenuLines.Select(l => new { l.Name, unit_price = l.UnitPrice, l.Quantity, l.Amount }),
        services = q.ServiceLines.Select(l => new { l.Name, unit_price = l.UnitPrice, l.Quantity, l.Unit, l.Amount }),
        subtotal = q.Subtotal, vat_percent = q.VatPercent, vat = q.VatAmount, total = q.Total,
        per_guest_approx = Math.Round(q.Total / (q.Tables * 10) / 1000m) * 1000m, // làm tròn đến nghìn đồng
        slot_status = q.SlotStatus.ToString(),
    };

    private static JsonElement Get(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"thiếu \"{key}\"");

    private static int GetInt(IReadOnlyDictionary<string, JsonElement> input, string key) => Get(input, key).GetInt32();

    private static string GetString(IReadOnlyDictionary<string, JsonElement> input, string key) => Get(input, key).GetString() ?? "";

    private static DateOnly GetDate(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        DateOnly.ParseExact(GetString(input, key), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Shift GetShift(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        Enum.TryParse<Shift>(GetString(input, key), out var s) && Enum.IsDefined(s) ? s
            : throw new AppException(ErrorKind.Validation, "Ca phải là Lunch hoặc Dinner.");

    private static List<int> GetIntList(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        Get(input, key).EnumerateArray().Select(x => x.GetInt32()).ToList();

    private static List<QuoteServiceRequest> GetServices(IReadOnlyDictionary<string, JsonElement> input) =>
        Get(input, "services").EnumerateArray()
            .Select(x => new QuoteServiceRequest(x.GetProperty("service_item_id").GetInt32(), x.GetProperty("quantity").GetInt32()))
            .ToList();
}
