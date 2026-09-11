using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHakedisKredisiVeProjeAlani : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AlanM2",
                table: "Projeler",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "HakedisKredisiDonemi",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "KalanHakedisKredisi",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlanM2",
                table: "Projeler");

            migrationBuilder.DropColumn(
                name: "HakedisKredisiDonemi",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "KalanHakedisKredisi",
                table: "AspNetUsers");
        }
    }
}
