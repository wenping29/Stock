using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FundAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class AddStockFinancialReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockFinancialReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReportType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Eps = table.Column<double>(type: "double precision", nullable: true),
                    Bps = table.Column<double>(type: "double precision", nullable: true),
                    Roe = table.Column<double>(type: "double precision", nullable: true),
                    Revenue = table.Column<double>(type: "double precision", nullable: true),
                    RevenueYoY = table.Column<double>(type: "double precision", nullable: true),
                    NetProfit = table.Column<double>(type: "double precision", nullable: true),
                    NetProfitYoY = table.Column<double>(type: "double precision", nullable: true),
                    GrossMargin = table.Column<double>(type: "double precision", nullable: true),
                    NetMargin = table.Column<double>(type: "double precision", nullable: true),
                    DebtRatio = table.Column<double>(type: "double precision", nullable: true),
                    OcfPerShare = table.Column<double>(type: "double precision", nullable: true),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockFinancialReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockFinancialReports_ReportDate",
                table: "StockFinancialReports",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockFinancialReports_Symbol_ReportDate",
                table: "StockFinancialReports",
                columns: new[] { "Symbol", "ReportDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockFinancialReports");
        }
    }
}
