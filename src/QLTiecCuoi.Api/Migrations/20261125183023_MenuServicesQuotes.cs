using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QLTiecCuoi.Api.Migrations
{
    /// <inheritdoc />
    public partial class MenuServicesQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    PricePerTable = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Shift = table.Column<int>(type: "int", nullable: false),
                    Tables = table.Column<int>(type: "int", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HallPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MenuTotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ServiceTotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuoteId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    MenuItemId = table.Column<int>(type: "int", nullable: true),
                    ServiceItemId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteLine_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MenuItems",
                columns: new[] { "Id", "Category", "IsActive", "Name", "PricePerTable" },
                values: new object[,]
                {
                    { 1, 1, true, "Gỏi ngó sen tôm thịt", 350000m },
                    { 2, 1, true, "Súp cua gà xé", 300000m },
                    { 3, 1, true, "Chả giò hải sản", 320000m },
                    { 4, 2, true, "Tôm sú rang muối", 650000m },
                    { 5, 2, true, "Gà hấp lá chanh", 550000m },
                    { 6, 2, true, "Bò lúc lắc khoai tây", 600000m },
                    { 7, 2, true, "Cá điêu hồng hấp xì dầu", 500000m },
                    { 8, 3, true, "Lẩu thái hải sản", 700000m },
                    { 9, 3, true, "Lẩu gà lá é", 600000m },
                    { 10, 4, true, "Chè hạt sen long nhãn", 200000m },
                    { 11, 4, true, "Trái cây theo mùa", 220000m }
                });

            migrationBuilder.InsertData(
                table: "ServiceItems",
                columns: new[] { "Id", "IsActive", "Name", "Price", "Unit" },
                values: new object[,]
                {
                    { 1, true, "Trang trí cổng hoa và sân khấu", 5000000m, "gói" },
                    { 2, true, "MC dẫn chương trình", 3000000m, "buổi" },
                    { 3, true, "Ban nhạc sống", 8000000m, "buổi" },
                    { 4, true, "Quay phim, chụp ảnh", 7000000m, "gói" },
                    { 5, true, "Bia lon (thùng 24 lon)", 420000m, "thùng" },
                    { 6, true, "Nước ngọt (thùng 24 lon)", 220000m, "thùng" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLine_QuoteId",
                table: "QuoteLine",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_BookingId",
                table: "Quotes",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CustomerPhone",
                table: "Quotes",
                column: "CustomerPhone");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_HallId",
                table: "Quotes",
                column: "HallId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItems");

            migrationBuilder.DropTable(
                name: "QuoteLine");

            migrationBuilder.DropTable(
                name: "ServiceItems");

            migrationBuilder.DropTable(
                name: "Quotes");
        }
    }
}
