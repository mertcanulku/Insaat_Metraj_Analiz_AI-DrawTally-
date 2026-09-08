using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InsaatMetrajWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class PozRayicAliasKutuphanesi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pozlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PozKodu = table.Column<string>(type: "TEXT", nullable: false),
                    Ad = table.Column<string>(type: "TEXT", nullable: false),
                    Birim = table.Column<string>(type: "TEXT", nullable: false),
                    Kaynak = table.Column<string>(type: "TEXT", nullable: false),
                    GecerlilikYili = table.Column<int>(type: "INTEGER", nullable: false),
                    ResmiFiyat = table.Column<decimal>(type: "TEXT", nullable: false),
                    AnalizGuvenilir = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pozlar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rayicler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kod = table.Column<string>(type: "TEXT", nullable: false),
                    Ad = table.Column<string>(type: "TEXT", nullable: false),
                    Birim = table.Column<string>(type: "TEXT", nullable: false),
                    Kategori = table.Column<string>(type: "TEXT", nullable: false),
                    Fiyat = table.Column<decimal>(type: "TEXT", nullable: false),
                    GecerlilikYili = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rayicler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Aliaslar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PozKaydiId = table.Column<int>(type: "INTEGER", nullable: false),
                    AliasMetin = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aliaslar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aliaslar_Pozlar_PozKaydiId",
                        column: x => x.PozKaydiId,
                        principalTable: "Pozlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PozAnalizSatirlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PozKaydiId = table.Column<int>(type: "INTEGER", nullable: false),
                    RayicKodu = table.Column<string>(type: "TEXT", nullable: false),
                    RayicKaydiId = table.Column<int>(type: "INTEGER", nullable: true),
                    Ad = table.Column<string>(type: "TEXT", nullable: false),
                    Birim = table.Column<string>(type: "TEXT", nullable: false),
                    Miktar = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PozAnalizSatirlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PozAnalizSatirlari_Pozlar_PozKaydiId",
                        column: x => x.PozKaydiId,
                        principalTable: "Pozlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PozAnalizSatirlari_Rayicler_RayicKaydiId",
                        column: x => x.RayicKaydiId,
                        principalTable: "Rayicler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aliaslar_PozKaydiId",
                table: "Aliaslar",
                column: "PozKaydiId");

            migrationBuilder.CreateIndex(
                name: "IX_PozAnalizSatirlari_PozKaydiId",
                table: "PozAnalizSatirlari",
                column: "PozKaydiId");

            migrationBuilder.CreateIndex(
                name: "IX_PozAnalizSatirlari_RayicKaydiId",
                table: "PozAnalizSatirlari",
                column: "RayicKaydiId");

            migrationBuilder.CreateIndex(
                name: "IX_Pozlar_PozKodu",
                table: "Pozlar",
                column: "PozKodu");

            migrationBuilder.CreateIndex(
                name: "IX_Rayicler_Kod",
                table: "Rayicler",
                column: "Kod");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aliaslar");

            migrationBuilder.DropTable(
                name: "PozAnalizSatirlari");

            migrationBuilder.DropTable(
                name: "Pozlar");

            migrationBuilder.DropTable(
                name: "Rayicler");
        }
    }
}
