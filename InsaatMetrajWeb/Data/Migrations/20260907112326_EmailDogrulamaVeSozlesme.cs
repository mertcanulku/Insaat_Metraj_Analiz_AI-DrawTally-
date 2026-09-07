using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailDogrulamaVeSozlesme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DogrulamaKodu",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DogrulamaKoduSonGecerlilik",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SozlesmeKabulTarihi",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DogrulamaKodu",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DogrulamaKoduSonGecerlilik",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SozlesmeKabulTarihi",
                table: "AspNetUsers");
        }
    }
}
