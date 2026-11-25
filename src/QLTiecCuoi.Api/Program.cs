using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Controllers;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("Booking").Get<BookingOptions>() ?? new BookingOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Quote").Get<QuoteOptions>() ?? new QuoteOptions());
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<QuoteService>();

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
