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

// Chatbot: API key lấy từ cấu hình "Anthropic:ApiKey" (user-secrets) hoặc biến môi trường ANTHROPIC_API_KEY.
// Key chỉ nằm trên máy chủ API, không nằm trong app desktop hay GitHub.
builder.Services.AddSingleton(builder.Configuration.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions());
builder.Services.AddSingleton<ChatConversationStore>();
builder.Services.AddSingleton<IChatModel>(_ =>
{
    var key = builder.Configuration["Anthropic:ApiKey"];
    if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    var configured = !string.IsNullOrWhiteSpace(key);
    var client = configured ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = "chua-cau-hinh" };
    return new ClaudeChatModel(client, configured);
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
