using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FundAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexMoneyFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndexMoneyFlows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MainNet = table.Column<double>(type: "double precision", nullable: false),
                    LargeNet = table.Column<double>(type: "double precision", nullable: false),
                    MidNet = table.Column<double>(type: "double precision", nullable: false),
                    SmallNet = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexMoneyFlows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexMoneyFlows_Code",
                table: "IndexMoneyFlows",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_IndexMoneyFlows_Code_Date",
                table: "IndexMoneyFlows",
                columns: new[] { "Code", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexMoneyFlows");
        }
    }
}
