using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FundAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSentiment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketSentimentDailies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpCount = table.Column<int>(type: "integer", nullable: false),
                    DownCount = table.Column<int>(type: "integer", nullable: false),
                    FlatCount = table.Column<int>(type: "integer", nullable: false),
                    Up5Count = table.Column<int>(type: "integer", nullable: false),
                    Down5Count = table.Column<int>(type: "integer", nullable: false),
                    LimitUpCount = table.Column<int>(type: "integer", nullable: false),
                    LimitDownCount = table.Column<int>(type: "integer", nullable: false),
                    BrokenBoardCount = table.Column<int>(type: "integer", nullable: false),
                    MaxBoardStreak = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<double>(type: "double precision", nullable: false),
                    AvgChangePct = table.Column<double>(type: "double precision", nullable: false),
                    MedianChangePct = table.Column<double>(type: "double precision", nullable: false),
                    SentimentScore = table.Column<double>(type: "double precision", nullable: false),
                    BasisDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSentimentDailies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSentimentDailies_Date",
                table: "MarketSentimentDailies",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketSentimentDailies");
        }
    }
}
