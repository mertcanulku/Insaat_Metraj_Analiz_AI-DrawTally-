using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSifreSifirlama : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SifreSifirlamaKodu",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SifreSifirlamaKoduSonGecerlilik",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SifreSifirlamaKodu",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SifreSifirlamaKoduSonGecerlilik",
                table: "AspNetUsers");
        }
    }
}
