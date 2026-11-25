using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QLTiecCuoi.Api.Migrations
{
    /// <inheritdoc />
    public partial class Liquidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualTables",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalSubtotal",
                table: "Contracts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalTotal",
                table: "Contracts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalVat",
                table: "Contracts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiquidatedAt",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiquidationNote",
                table: "Contracts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractExtra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractExtra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractExtra_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractExtra_ContractId",
                table: "ContractExtra",
                column: "ContractId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractExtra");

            migrationBuilder.DropColumn(
                name: "ActualTables",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FinalSubtotal",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FinalTotal",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FinalVat",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LiquidatedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LiquidationNote",
                table: "Contracts");
        }
    }
}
