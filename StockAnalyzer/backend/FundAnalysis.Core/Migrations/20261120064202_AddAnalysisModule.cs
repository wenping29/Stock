using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FundAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BacktestRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    MetricsJson = table.Column<string>(type: "text", nullable: false),
                    EquityCurveJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketSeriesPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Open = table.Column<double>(type: "double precision", nullable: false),
                    High = table.Column<double>(type: "double precision", nullable: false),
                    Low = table.Column<double>(type: "double precision", nullable: false),
                    Close = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSeriesPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Cron = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonitoringRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EntrySignalsJson = table.Column<string>(type: "text", nullable: false),
                    ExitSignalsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacktestTradeRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BacktestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    EntryDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EntryPrice = table.Column<double>(type: "double precision", nullable: false),
                    ExitDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExitPrice = table.Column<double>(type: "double precision", nullable: false),
                    Shares = table.Column<double>(type: "double precision", nullable: false),
                    Pnl = table.Column<double>(type: "double precision", nullable: false),
                    PnlPct = table.Column<double>(type: "double precision", nullable: false),
                    ExitReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestTradeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacktestTradeRecords_BacktestRecords_BacktestId",
                        column: x => x.BacktestId,
                        principalTable: "BacktestRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BacktestTradeRecords_BacktestId",
                table: "BacktestTradeRecords",
                column: "BacktestId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSeriesPoints_Key_Period",
                table: "MarketSeriesPoints",
                columns: new[] { "Key", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSeriesPoints_Key_Period_Date",
                table: "MarketSeriesPoints",
                columns: new[] { "Key", "Period", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringRuns_JobId_RunAt",
                table: "MonitoringRuns",
                columns: new[] { "JobId", "RunAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestTradeRecords");

            migrationBuilder.DropTable(
                name: "MarketSeriesPoints");

            migrationBuilder.DropTable(
                name: "MonitoringJobs");

            migrationBuilder.DropTable(
                name: "MonitoringRuns");

            migrationBuilder.DropTable(
                name: "BacktestRecords");
        }
    }
}
