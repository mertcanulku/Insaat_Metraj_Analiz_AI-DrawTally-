using System.Text.Json;
using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// ÇŞB rayiç/poz kütüphanesini (Data/PozSeed/*.json) veritabanına bir kez aktarır ve
/// tüm kullanıcılar arasında ortak olan bu kataloğu (13binin üzerinde poz) uygulama
/// ömrü boyunca bellekte tutar — her istek için veritabanına tekrar sorgu atmaz.
/// Singleton olarak kaydedilir (bkz. Program.cs); Proje/MetrajKalemi gibi kullanıcıya
/// özel veriler ise scoped VeriDeposu üzerinden veritabanından okunur.
/// </summary>
public class PozKutuphanesi
{
    private const int SeedGecerlilikYili = 2026;

    public List<Rayic> Rayicler { get; private set; } = new();
    public List<Poz> Pozlar { get; private set; } = new();
    public List<Alias> Aliaslar { get; private set; } = new();

    private record RayicSeedDto(string Kod, string Ad, string Birim, string Kategori, decimal Fiyat);
    private record AnalizSatirSeedDto(string RayicKod, string Ad, string Birim, decimal Miktar, bool RayicKoduEslesti);
    private record PozSeedDto(string PozKodu, string Ad, string Birim, decimal ResmiFiyat, bool Guvenilir, List<AnalizSatirSeedDto> Satirlar);

    /// <summary>Veritabanı boşsa Data/PozSeed/*.json içeriğini yükler, ardından kataloğu belleğe alır.</summary>
    public async Task SeedVeYukleAsync(ApplicationDbContext db, string icerikKlasoru)
    {
        if (!await db.Pozlar.AnyAsync())
            await SeedEtAsync(db, icerikKlasoru);

        await YukleAsync(db);
    }

    private static async Task SeedEtAsync(ApplicationDbContext db, string icerikKlasoru)
    {
        var seedKlasoru = Path.Combine(icerikKlasoru, "Data", "PozSeed");
        var rayicDto = JsonSerializer.Deserialize<List<RayicSeedDto>>(
            await File.ReadAllTextAsync(Path.Combine(seedKlasoru, "rayic.json"))) ?? new();
        var pozDto = JsonSerializer.Deserialize<List<PozSeedDto>>(
            await File.ReadAllTextAsync(Path.Combine(seedKlasoru, "pozlar.json"))) ?? new();

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var rayicKaydilari = rayicDto.Select(r => new RayicKaydi
            {
                Kod = r.Kod,
                Ad = r.Ad,
                Birim = r.Birim,
                Kategori = r.Kategori,
                Fiyat = r.Fiyat,
                GecerlilikYili = SeedGecerlilikYili
            }).ToList();
            db.Rayicler.AddRange(rayicKaydilari);
            await db.SaveChangesAsync();
            var rayicIdByKod = rayicKaydilari
                .GroupBy(r => r.Kod).Select(g => g.First())
                .ToDictionary(r => r.Kod, r => r.Id);

            var pozKaydilari = pozDto.Select(p => new PozKaydi
            {
                PozKodu = p.PozKodu,
                Ad = p.Ad,
                Birim = p.Birim,
                ResmiFiyat = p.ResmiFiyat,
                AnalizGuvenilir = p.Guvenilir,
                GecerlilikYili = SeedGecerlilikYili,
                Kaynak = "ÇŞB"
            }).ToList();
            db.Pozlar.AddRange(pozKaydilari);
            await db.SaveChangesAsync();
            var pozIdByKod = pozKaydilari.ToDictionary(p => p.PozKodu, p => p.Id);

            var satirKaydilari = new List<PozAnalizSatiriKaydi>();
            foreach (var p in pozDto)
            {
                if (p.Satirlar.Count == 0) continue;
                var pozId = pozIdByKod[p.PozKodu];
                foreach (var s in p.Satirlar)
                {
                    rayicIdByKod.TryGetValue(s.RayicKod, out var rayicId);
                    satirKaydilari.Add(new PozAnalizSatiriKaydi
                    {
                        PozKaydiId = pozId,
                        RayicKodu = s.RayicKod,
                        RayicKaydiId = rayicId == 0 ? null : rayicId,
                        Ad = s.Ad,
                        Birim = s.Birim,
                        Miktar = s.Miktar
                    });
                }
            }
            db.PozAnalizSatirlari.AddRange(satirKaydilari);
            await db.SaveChangesAsync();
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private async Task YukleAsync(ApplicationDbContext db)
    {
        var rayicKaydilari = await db.Rayicler.AsNoTracking().ToListAsync();
        var rayicById = rayicKaydilari.ToDictionary(r => r.Id, r => new Rayic
        {
            Id = r.Id,
            PozKodu = r.Kod,
            Ad = r.Ad,
            Birim = r.Birim,
            Kategori = r.Kategori,
            Fiyat = r.Fiyat,
            GecerlilikTarihi = new DateOnly(r.GecerlilikYili, 1, 1)
        });
        Rayicler = rayicById.Values.ToList();

        var pozKaydilari = await db.Pozlar.AsNoTracking()
            .Include(p => p.AnalizSatirlari)
            .ToListAsync();

        Pozlar = pozKaydilari.Select(p => new Poz
        {
            Id = p.Id,
            PozKodu = p.PozKodu,
            Ad = p.Ad,
            Birim = p.Birim,
            Kaynak = p.Kaynak,
            ResmiFiyat = p.ResmiFiyat,
            AnalizGuvenilir = p.AnalizGuvenilir,
            AnalizSatirlari = p.AnalizSatirlari
                .Where(s => s.RayicKaydiId != null && rayicById.ContainsKey(s.RayicKaydiId.Value))
                .Select(s => new PozAnalizSatiri { Rayic = rayicById[s.RayicKaydiId!.Value], Miktar = s.Miktar })
                .ToList()
        }).ToList();

        var aliasKaydilari = await db.Aliaslar.AsNoTracking().ToListAsync();
        Aliaslar = aliasKaydilari
            .Select(a => new Alias { Id = a.Id, PozId = a.PozKaydiId, AliasMetin = a.AliasMetin })
            .ToList();
    }

    /// <summary>
    /// Serbest metin ile poz arar: önce resmi poz koduna, sonra alias tablosuna,
    /// son olarak poz adının içine bakar.
    /// </summary>
    public Poz? PozAra(string metin)
    {
        var t = metin.Trim();
        if (t.Length == 0) return null;

        var kodEslesme = Pozlar.FirstOrDefault(p => p.PozKodu.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (kodEslesme != null) return kodEslesme;

        var aliasEslesme = Aliaslar.FirstOrDefault(a => a.AliasMetin.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (aliasEslesme != null) return Pozlar.FirstOrDefault(p => p.Id == aliasEslesme.PozId);

        return Pozlar.FirstOrDefault(p => p.Ad.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
