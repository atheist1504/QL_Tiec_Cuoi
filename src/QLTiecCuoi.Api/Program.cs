using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Controllers;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Api.Services.Chat;
using Anthropic;

var builder = WebApplication.CreateBuilder(args);

// Trên máy chủ hosting (SmarterASP.NET) không có user-secrets: chuỗi kết nối và API key nằm trong
// file appsettings.Secrets.json tạo tay trên máy chủ. File này không có trong git và không bị publish ghi đè.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: false);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("Booking").Get<BookingOptions>() ?? new BookingOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Quote").Get<QuoteOptions>() ?? new QuoteOptions());
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<QuoteService>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Contract").Get<ContractOptions>() ?? new ContractOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Restaurant").Get<RestaurantOptions>() ?? new RestaurantOptions());
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<PdfService>();
builder.Services.AddScoped<ReportService>();

// Chatbot: chọn nhà cung cấp bằng "Chat:Provider". API key lấy từ user-secrets hoặc biến môi trường,
// chỉ nằm trên máy chủ API, không nằm trong app desktop hay GitHub.
var chatOptions = builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
builder.Services.AddSingleton(chatOptions);
builder.Services.AddSingleton<ChatConversationStore>();
builder.Services.AddSingleton<IChatModel>(sp =>
{
    var chatLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Chat");
    string? Key(string configKey, string envVar)
    {
        var k = builder.Configuration[configKey];
        if (string.IsNullOrWhiteSpace(k)) k = Environment.GetEnvironmentVariable(envVar);
        // Bỏ ký tự thừa hay gõ nhầm khi chép key từ hướng dẫn: dấu cách, ngoặc kép, dấu < >.
        return k?.Trim().Trim('"', '\'', '<', '>', ' ');
    }
    switch (chatOptions.Provider.Trim().ToLowerInvariant())
    {
        case "claude":
            var claudeKey = Key("Anthropic:ApiKey", "ANTHROPIC_API_KEY");
            var configured = !string.IsNullOrWhiteSpace(claudeKey);
            return new ClaudeChatModel(new AnthropicClient { ApiKey = configured ? claudeKey : "chua-cau-hinh" }, chatOptions, configured);
        case "openaicompatible":
            return new OpenAiCompatibleChatModel(new HttpClient { Timeout = TimeSpan.FromSeconds(90) }, chatOptions,
                Key("Chat:ApiKey", "CHAT_API_KEY"), "Dịch vụ AI", chatLogger);
        default: // Gemini
            return new OpenAiCompatibleChatModel(new HttpClient { Timeout = TimeSpan.FromSeconds(90) }, chatOptions,
                Key("Gemini:ApiKey", "GEMINI_API_KEY"), "Gemini", chatLogger);
    }
});
builder.Services.AddScoped<ChatTools>();
builder.Services.AddScoped<ChatService>();

builder.Services.AddControllers(o => o.Filters.Add<AppExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger: luôn bật ở máy dev; trên máy chủ bật bằng cấu hình "Swagger:Enabled" = true để demo.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Mở địa chỉ gốc thì chuyển sang trang tài liệu API.
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

// Migration: tự áp dụng ở máy dev; trên máy chủ bật bằng "Database:MigrateOnStartup" = true.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) db.Database.Migrate();
}
else if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Trên máy chủ: lỗi CSDL không làm sập API, xem nguyên nhân ở /api/health.
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Không áp dụng được migration khi khởi động");
    }
}

// Kiểm tra tình trạng máy chủ: có file bí mật không, kết nối CSDL được không, đã có key chatbot chưa.
// Không trả về mật khẩu hay key.
app.MapGet("/api/health", async (AppDbContext db, IConfiguration cfg, IWebHostEnvironment env) =>
{
    string? dbError = null;
    var canConnect = false;
    try
    {
        canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
        }
    }
    catch (Exception ex) { dbError = (ex.InnerException ?? ex).Message; }
    var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cfg.GetConnectionString("Default") ?? "");
    return Results.Ok(new
    {
        environment = env.EnvironmentName,
        secretsFileFound = File.Exists(Path.Combine(env.ContentRootPath, "appsettings.Secrets.json")),
        dbServer = cs.DataSource,
        dbName = cs.InitialCatalog,
        dbUser = cs.UserID,
        canConnect,
        dbError,
        chatKeyConfigured = !string.IsNullOrWhiteSpace(cfg["Gemini:ApiKey"]) ||
                            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
    });
}).ExcludeFromDescription();

app.MapControllers();

app.Run();

public partial class Program;
