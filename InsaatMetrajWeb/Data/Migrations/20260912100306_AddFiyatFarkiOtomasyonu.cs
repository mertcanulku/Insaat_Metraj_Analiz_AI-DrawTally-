using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFiyatFarkiOtomasyonu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SozlesmeTarihi",
                table: "Projeler",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiyatFarkiHesaplamaOzeti",
                table: "Hakedisler",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "FiyatFarkiOtomatikMi",
                table: "Hakedisler",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EndeksDonemleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Yil = table.Column<int>(type: "INTEGER", nullable: false),
                    Ay = table.Column<int>(type: "INTEGER", nullable: false),
                    TufeYiUfeDegeri = table.Column<decimal>(type: "TEXT", nullable: false),
                    BakanlikKatsayisi = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndeksDonemleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EndeksDonemleri_Yil_Ay",
                table: "EndeksDonemleri",
                columns: new[] { "Yil", "Ay" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EndeksDonemleri");

            migrationBuilder.DropColumn(
                name: "SozlesmeTarihi",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "FiyatFarkiHesaplamaOzeti",
                table: "Hakedisler");

            migrationBuilder.DropColumn(
                name: "FiyatFarkiOtomatikMi",
                table: "Hakedisler");
        }
    }
}
