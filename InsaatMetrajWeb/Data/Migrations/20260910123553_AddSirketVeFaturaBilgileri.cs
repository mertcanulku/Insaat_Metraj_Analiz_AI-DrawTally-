using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSirketVeFaturaBilgileri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaturaAdresi",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FaturaBilgisiIstiyor",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FirmaAdi",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmaTelefonu",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KurumsalHesap",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VergiDairesi",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VergiKimlikNo",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YetkiliAdSoyad",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YetkiliEmail",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaturaAdresi",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FaturaBilgisiIstiyor",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirmaAdi",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FirmaTelefonu",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "KurumsalHesap",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VergiDairesi",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VergiKimlikNo",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "YetkiliAdSoyad",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "YetkiliEmail",
                table: "AspNetUsers");
        }
    }
}
