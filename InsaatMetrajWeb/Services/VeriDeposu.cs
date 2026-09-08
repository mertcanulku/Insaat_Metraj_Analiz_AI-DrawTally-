using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Models;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Poz/Rayiç/Alias kütüphanesi tüm kullanıcılar arasında ortak olduğu için bellekte tutulur
/// (demo amaçlı sabit veri). Projeler ve metraj kalemleri ise kullanıcıya özel olduğundan
/// veritabanında (ApplicationDbContext) saklanır — bkz. ProjeBul, ProjeleriListele, ProjeEkle.
/// </summary>
public class VeriDeposu
{
    private readonly ApplicationDbContext _db;

    public List<Rayic> Rayicler { get; } = new();
    public List<Poz> Pozlar { get; } = new();
    public List<Alias> Aliaslar { get; } = new();

    public VeriDeposu(ApplicationDbContext db)
    {
        _db = db;
        // --- Rayiçler (ÇŞB 2026 örnek fiyatlar) ---
        var hazirBeton = new Rayic { Id = 1, PozKodu = "10.130.1310", Ad = "Hazır beton C12/15", Birim = "m3", Kategori = "malzeme", Fiyat = 2650m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var betonPompasi = new Rayic { Id = 2, PozKodu = "10.500.2001", Ad = "Beton pompası (saat)", Birim = "saat", Kategori = "makine", Fiyat = 1450m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var duzIsci = new Rayic { Id = 3, PozKodu = "10.100.1001", Ad = "Düz işçi (saat)", Birim = "saat", Kategori = "işçilik", Fiyat = 185m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var demir = new Rayic { Id = 4, PozKodu = "10.140.1005", Ad = "Nervürlü inşaat demiri", Birim = "kg", Kategori = "malzeme", Fiyat = 42m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var demirci = new Rayic { Id = 5, PozKodu = "10.100.1010", Ad = "Demirci ustası (saat)", Birim = "saat", Kategori = "işçilik", Fiyat = 260m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var duvarBrikeri = new Rayic { Id = 6, PozKodu = "10.200.2050", Ad = "Yatay delikli tuğla (8.5x19x19)", Birim = "adet", Kategori = "malzeme", Fiyat = 9.5m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        var duvarciUsta = new Rayic { Id = 7, PozKodu = "10.100.1020", Ad = "Duvarcı ustası (saat)", Birim = "saat", Kategori = "işçilik", Fiyat = 245m, GecerlilikTarihi = new DateOnly(2026, 9, 1) };
        Rayicler.AddRange(new[] { hazirBeton, betonPompasi, duzIsci, demir, demirci, duvarBrikeri, duvarciUsta });

        // --- Pozlar ---
        var betonPozu = new Poz
        {
            Id = 1,
            PozKodu = "15.150.1002",
            Ad = "C12/15 hazır beton dökülmesi (pompayla, nakil dahil)",
            Birim = "m3",
            AnalizSatirlari = new List<PozAnalizSatiri>
            {
                new() { Rayic = hazirBeton, Miktar = 1.02m },
                new() { Rayic = betonPompasi, Miktar = 0.15m },
                new() { Rayic = duzIsci, Miktar = 0.25m },
            }
        };

        var demirPozu = new Poz
        {
            Id = 2,
            PozKodu = "15.170.1003",
            Ad = "Nervürlü inşaat demiri işçiliği (kesme, bükme, yerine koyma)",
            Birim = "kg",
            AnalizSatirlari = new List<PozAnalizSatiri>
            {
                new() { Rayic = demir, Miktar = 1.03m },
                new() { Rayic = demirci, Miktar = 0.012m },
            }
        };

        var duvarPozu = new Poz
        {
            Id = 3,
            PozKodu = "16.150.1005",
            Ad = "8.5 cm delikli tuğla ile duvar yapılması",
            Birim = "m2",
            AnalizSatirlari = new List<PozAnalizSatiri>
            {
                new() { Rayic = duvarBrikeri, Miktar = 55m },
                new() { Rayic = duvarciUsta, Miktar = 0.45m },
            }
        };

        Pozlar.AddRange(new[] { betonPozu, demirPozu, duvarPozu });

        // --- Aliaslar (kısaltma / eski kod eşlemeleri) ---
        Aliaslar.AddRange(new[]
        {
            new Alias { Id = 1, PozId = betonPozu.Id, AliasMetin = "beton pompa" },
            new Alias { Id = 2, PozId = betonPozu.Id, AliasMetin = "Y.16.050/12" },
            new Alias { Id = 3, PozId = demirPozu.Id, AliasMetin = "demir iscilik" },
            new Alias { Id = 4, PozId = duvarPozu.Id, AliasMetin = "tugla duvar" },
        });
    }

    /// <summary>Bir kullanıcının tüm projelerini (en yeni önce) listeler.</summary>
    public async Task<List<Proje>> ProjeleriListele(string sahipId)
    {
        var kayitlar = await _db.Projeler
            .Where(p => p.SahipId == sahipId)
            .Include(p => p.MetrajKalemleri)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        return kayitlar.Select(KaydiProjeyeCevir).ToList();
    }

    /// <summary>Projeyi sadece belirtilen kullanıcıya aitse döner — başkasının projesine erişimi engeller.</summary>
    public async Task<Proje?> ProjeBul(string sahipId, int id)
    {
        var kayit = await _db.Projeler
            .Include(p => p.MetrajKalemleri)
            .FirstOrDefaultAsync(p => p.Id == id && p.SahipId == sahipId);

        return kayit == null ? null : KaydiProjeyeCevir(kayit);
    }

    public async Task<Proje> ProjeEkle(string sahipId, string ad)
    {
        var kayit = new ProjeKaydi { Ad = ad, SahipId = sahipId };
        _db.Projeler.Add(kayit);
        await _db.SaveChangesAsync();
        return KaydiProjeyeCevir(kayit);
    }

    private Proje KaydiProjeyeCevir(ProjeKaydi kayit) => new()
    {
        Id = kayit.Id,
        Ad = kayit.Ad,
        MetrajKalemleri = kayit.MetrajKalemleri
            .Select(k => PozIdIleBul(k.PozId) is { } poz
                ? new MetrajKalemi { Id = k.Id, Poz = poz, OlcumDetayi = k.OlcumDetayi, Miktar = k.Miktar }
                : null)
            .Where(k => k != null)
            .Select(k => k!)
            .ToList()
    };

    private Poz? PozIdIleBul(int id) => Pozlar.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Serbest metin ile poz arar: önce resmi poz koduna, sonra alias tablosuna,
    /// son olarak poz adının içine bakar. Kullanıcı hangi kısaltmayı/eski kodu
    /// kullanırsa kullansın doğru poza yönlendirilsin diye üç kademeli arama yapılır.
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

    /// <summary>
    /// CSV içeriğini satır satır işleyip verilen projeye metraj kalemleri ekler.
    /// Beklenen format: PozKoduVeyaAlias;OlcumDetayi;Miktar
    /// Her satır için sonuç (başarılı/başarısız + sebep) döner, böylece kullanıcı
    /// hangi satırların eşleşmediğini görüp elle düzeltebilir.
    /// </summary>
    public async Task<List<ImportSonucSatiri>> ProjeyeCsvImportEt(Proje proje, string csvIcerik)
    {
        var sonuclar = new List<ImportSonucSatiri>();
        var satirlar = csvIcerik.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < satirlar.Length; i++)
        {
            var satir = satirlar[i].Trim().TrimEnd('\r');
            if (satir.Length == 0) continue;

            // Başlık satırını atla (ilk sütun "poz" ile başlıyorsa)
            if (i == 0 && satir.StartsWith("poz", StringComparison.OrdinalIgnoreCase))
                continue;

            var parcalar = satir.Split(';');
            var sonuc = new ImportSonucSatiri { SatirNo = i + 1, HamMetin = satir };

            if (parcalar.Length < 3)
            {
                sonuc.Basarili = false;
                sonuc.Mesaj = "Sütun sayısı eksik (PozKodu;OlcumDetayi;Miktar bekleniyor)";
                sonuclar.Add(sonuc);
                continue;
            }

            var pozAramaMetni = parcalar[0].Trim();
            var olcumDetayi = parcalar[1].Trim();
            var miktarMetni = parcalar[2].Trim().Replace(",", ".");

            var eslesenPoz = PozAra(pozAramaMetni);
            if (eslesenPoz == null)
            {
                sonuc.Basarili = false;
                sonuc.Mesaj = $"\"{pozAramaMetni}\" hiçbir poz veya kısaltmaya eşleşmedi";
                sonuclar.Add(sonuc);
                continue;
            }

            if (!decimal.TryParse(miktarMetni, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var miktar))
            {
                sonuc.Basarili = false;
                sonuc.Mesaj = $"Miktar sayıya çevrilemedi: \"{parcalar[2]}\"";
                sonuclar.Add(sonuc);
                continue;
            }

            _db.MetrajKalemleri.Add(new MetrajKalemiKaydi
            {
                ProjeKaydiId = proje.Id,
                PozId = eslesenPoz.Id,
                OlcumDetayi = olcumDetayi,
                Miktar = miktar
            });

            sonuc.Basarili = true;
            sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
            sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad})";
            sonuclar.Add(sonuc);
        }

        await _db.SaveChangesAsync();
        return sonuclar;
    }

    /// <summary>
    /// Dijital olarak oluşturulmuş (metni seçilebilir) bir PDF'in içindeki metni satır satır
    /// tarar. Her satırda önce bilinen bir poz kodu (örn. 15.150.1002) veya alias metni,
    /// sonra satırın sonunda bir miktar (sayı) arar. Bu bir sezgisel (heuristic) ayrıştırma —
    /// PDF'in kesin bir tablo yapısı olmayabileceği için CSV kadar güvenilir değildir,
    /// bu yüzden her satırın sonucu (eşleşti/eşleşmedi) ayrı ayrı raporlanır.
    ///
    /// NOT: Taranmış (resim) PDF'lerde metin katmanı olmadığı için bu yöntem çalışmaz —
    /// o durum için ayrıca OCR (örn. Tesseract) entegrasyonu gerekir, bu demo'ya dahil değil.
    /// </summary>
    public async Task<List<ImportSonucSatiri>> ProjeyePdfImportEt(Proje proje, Stream pdfStream)
    {
        var sonuclar = new List<ImportSonucSatiri>();
        var pozKoduDeseni = new Regex(@"\d{2}\.\d{3}\.\d{4}");
        var sondakiSayiDeseni = new Regex(@"(\d+[.,]\d+|\d+)\s*$");

        using var pdf = PdfDocument.Open(pdfStream);
        int satirNo = 0;

        foreach (var sayfa in pdf.GetPages())
        {
            var sayfaMetni = sayfa.Text;
            var satirlar = sayfaMetni.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var hamSatir in satirlar)
            {
                var satir = hamSatir.Trim();
                if (satir.Length < 5) continue; // çok kısa satırları (başlık, sayfa no vb.) atla
                satirNo++;

                var sonuc = new ImportSonucSatiri { SatirNo = satirNo, HamMetin = satir };

                // 1) Önce resmi poz kodu deseni ara, yoksa alias tablosuna bak
                Poz? eslesenPoz = null;
                var kodEslesme = pozKoduDeseni.Match(satir);
                if (kodEslesme.Success)
                {
                    eslesenPoz = PozAra(kodEslesme.Value);
                }
                if (eslesenPoz == null)
                {
                    var aliasEslesme = Aliaslar.FirstOrDefault(a =>
                        satir.Contains(a.AliasMetin, StringComparison.OrdinalIgnoreCase));
                    if (aliasEslesme != null)
                        eslesenPoz = Pozlar.FirstOrDefault(p => p.Id == aliasEslesme.PozId);
                }

                if (eslesenPoz == null)
                {
                    sonuc.Basarili = false;
                    sonuc.Mesaj = "Satırda bilinen bir poz kodu veya kısaltma bulunamadı";
                    sonuclar.Add(sonuc);
                    continue;
                }

                // 2) Satırın sonundaki sayıyı miktar olarak al
                var miktarEslesme = sondakiSayiDeseni.Match(satir);
                if (!miktarEslesme.Success ||
                    !decimal.TryParse(miktarEslesme.Value.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var miktar))
                {
                    sonuc.Basarili = false;
                    sonuc.Mesaj = $"Poz bulundu ({eslesenPoz.PozKodu}) ama satır sonunda geçerli bir miktar bulunamadı";
                    sonuclar.Add(sonuc);
                    continue;
                }

                var olcumDetayi = satir;
                if (kodEslesme.Success) olcumDetayi = olcumDetayi.Replace(kodEslesme.Value, "").Trim();
                olcumDetayi = olcumDetayi.Replace(miktarEslesme.Value, "").Trim(' ', '-', ':', ';');

                _db.MetrajKalemleri.Add(new MetrajKalemiKaydi
                {
                    ProjeKaydiId = proje.Id,
                    PozId = eslesenPoz.Id,
                    OlcumDetayi = olcumDetayi.Length > 0 ? olcumDetayi : "(PDF'den otomatik alındı)",
                    Miktar = miktar
                });

                sonuc.Basarili = true;
                sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
                sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad}), miktar: {miktar}";
                sonuclar.Add(sonuc);
            }
        }

        await _db.SaveChangesAsync();
        return sonuclar;
    }

    /// <summary>Bir çizim analizi (PDF veya DWG) sonucu kullanıcı tarafından onaylandığında çağrılır — projeye metraj kalemi ekler.</summary>
    public async Task OdaAnalizSonucunuOnayla(Proje proje, CizimAnalizSonucu sonuc, Poz poz)
    {
        var katBilgisi = string.IsNullOrWhiteSpace(sonuc.KatAdi) ? "" : $", kat: {sonuc.KatAdi}";
        _db.MetrajKalemleri.Add(new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = $"AI çizim analiziyle eklendi — oda: {sonuc.OdaAdi}{katBilgisi} ({sonuc.KaynakTuru})",
            Miktar = sonuc.AlanM2
        });
        await _db.SaveChangesAsync();
        sonuc.OnaylandiMi = true;
    }

    /// <summary>Metraj Girişi sayfasından elle eklenen bir kalemi projeye kaydeder.</summary>
    public async Task MetrajKalemiEkle(Proje proje, Poz poz, string olcumDetayi, decimal miktar)
    {
        _db.MetrajKalemleri.Add(new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = olcumDetayi,
            Miktar = miktar
        });
        await _db.SaveChangesAsync();
    }
}
