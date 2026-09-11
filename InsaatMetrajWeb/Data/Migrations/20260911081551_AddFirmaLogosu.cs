using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmaLogosu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirmaLogoIcerikTuru",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FirmaLogoVerisi",
                table: "AspNetUsers",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirmaLogoIcerikTuru",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirmaLogoVerisi",
                table: "AspNetUsers");
        }
    }
}
