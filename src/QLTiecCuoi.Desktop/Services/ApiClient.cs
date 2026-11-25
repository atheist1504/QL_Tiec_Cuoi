using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop.Services;

/// <summary>Lỗi nghiệp vụ trả về từ API (ví dụ 409 trùng sảnh).</summary>
public class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

public class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public ApiClient()
    {
        // Đổi địa chỉ API bằng biến môi trường QLTC_API_URL khi triển khai.
        var url = Environment.GetEnvironmentVariable("QLTC_API_URL") ?? "http://localhost:5100/";
        if (!url.EndsWith('/')) url += "/";
        _http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15) };
    }

    public string BaseUrl => _http.BaseAddress!.ToString();

    public Task<List<HallDto>> GetHallsAsync() => GetAsync<List<HallDto>>("api/halls");

    public Task<List<SlotDto>> GetScheduleAsync(DateOnly from, DateOnly to) =>
        GetAsync<List<SlotDto>>($"api/schedule?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    public Task<BookingDto> HoldAsync(HoldRequest req) => SendAsync<BookingDto>(HttpMethod.Post, "api/bookings/hold", req);

    public Task<BookingDto> CancelHoldAsync(int id) => SendAsync<BookingDto>(HttpMethod.Post, $"api/bookings/{id}/cancel", null);

    public Task<BookingDto> GetBookingAsync(int id) => GetAsync<BookingDto>($"api/bookings/{id}");

    public Task<List<MenuItemDto>> GetMenuAsync(bool includeInactive = false) =>
        GetAsync<List<MenuItemDto>>($"api/menu-items?includeInactive={includeInactive}");

    public Task<MenuItemDto> SaveMenuItemAsync(int? id, MenuItemUpsert req) => id is null
        ? SendAsync<MenuItemDto>(HttpMethod.Post, "api/menu-items", req)
        : SendAsync<MenuItemDto>(HttpMethod.Put, $"api/menu-items/{id}", req);

    public Task<List<ServiceItemDto>> GetServicesAsync(bool includeInactive = false) =>
        GetAsync<List<ServiceItemDto>>($"api/services?includeInactive={includeInactive}");

    public Task<ServiceItemDto> SaveServiceAsync(int? id, ServiceItemUpsert req) => id is null
        ? SendAsync<ServiceItemDto>(HttpMethod.Post, "api/services", req)
        : SendAsync<ServiceItemDto>(HttpMethod.Put, $"api/services/{id}", req);

    public Task<QuoteDto> PreviewQuoteAsync(QuoteRequest req) => SendAsync<QuoteDto>(HttpMethod.Post, "api/quotes/preview", req);

    public Task<QuoteDto> CreateQuoteAsync(QuoteRequest req) => SendAsync<QuoteDto>(HttpMethod.Post, "api/quotes", req);

    private Task<T> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body)
    {
        using var msg = new HttpRequestMessage(method, path);
        if (body is not null) msg.Content = JsonContent.Create(body, options: Json);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(msg);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable,
                $"Không kết nối được máy chủ API ({BaseUrl}). Hãy kiểm tra API đã chạy chưa.");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(HttpStatusCode.RequestTimeout, "Máy chủ API phản hồi quá lâu.");
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadFromJsonAsync<T>(Json))!;

            string message;
            try
            {
                message = (await res.Content.ReadFromJsonAsync<ErrorDto>(Json))?.Message ?? res.ReasonPhrase ?? "Lỗi không xác định.";
            }
            catch (JsonException)
            {
                message = $"Lỗi máy chủ ({(int)res.StatusCode}).";
            }
            throw new ApiException(res.StatusCode, message);
        }
    }
}
