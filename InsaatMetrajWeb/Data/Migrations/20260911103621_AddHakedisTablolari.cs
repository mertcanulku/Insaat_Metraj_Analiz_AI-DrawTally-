using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHakedisTablolari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SozlesmeBedeli",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VarsayilanAvansOrani",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VarsayilanKdvOrani",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VarsayilanStopajOrani",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VarsayilanTeminatOrani",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Hakedisler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjeKaydiId = table.Column<int>(type: "INTEGER", nullable: false),
                    HakedisNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Tarih = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AvansOrani = table.Column<decimal>(type: "TEXT", nullable: false),
                    TeminatOrani = table.Column<decimal>(type: "TEXT", nullable: false),
                    StopajOrani = table.Column<decimal>(type: "TEXT", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "TEXT", nullable: false),
                    FiyatFarkiOrani = table.Column<decimal>(type: "TEXT", nullable: false),
                    FiyatFarkiTutari = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hakedisler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hakedisler_Projeler_ProjeKaydiId",
                        column: x => x.ProjeKaydiId,
                        principalTable: "Projeler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HakedisKalemleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HakedisKaydiId = table.Column<int>(type: "INTEGER", nullable: false),
                    MetrajKalemiKaydiId = table.Column<int>(type: "INTEGER", nullable: false),
                    KumulatifYuzde = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HakedisKalemleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HakedisKalemleri_Hakedisler_HakedisKaydiId",
                        column: x => x.HakedisKaydiId,
                        principalTable: "Hakedisler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HakedisKalemleri_HakedisKaydiId",
                table: "HakedisKalemleri",
                column: "HakedisKaydiId");

            migrationBuilder.CreateIndex(
                name: "IX_Hakedisler_ProjeKaydiId",
                table: "Hakedisler",
                column: "ProjeKaydiId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HakedisKalemleri");

            migrationBuilder.DropTable(
                name: "Hakedisler");

            migrationBuilder.DropColumn(
                name: "SozlesmeBedeli",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "VarsayilanAvansOrani",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "VarsayilanKdvOrani",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "VarsayilanStopajOrani",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "VarsayilanTeminatOrani",
                table: "Projeler");
        }
    }
}
