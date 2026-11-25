using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Controllers;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Api.Services.Chat;
using Anthropic;

var builder = WebApplication.CreateBuilder(args);

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Tự áp dụng migration khi chạy ở máy dev.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) db.Database.Migrate();
}

app.MapControllers();

app.Run();

public partial class Program;
