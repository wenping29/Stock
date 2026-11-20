using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FundAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class AddSectorMoneyFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SectorMoneyFlows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MainNet = table.Column<double>(type: "double precision", nullable: false),
                    Amount = table.Column<double>(type: "double precision", nullable: false),
                    ChangePct = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SectorMoneyFlows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SectorMoneyFlows_Code_Date",
                table: "SectorMoneyFlows",
                columns: new[] { "Code", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SectorMoneyFlows_Date",
                table: "SectorMoneyFlows",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SectorMoneyFlows");
        }
    }
}
