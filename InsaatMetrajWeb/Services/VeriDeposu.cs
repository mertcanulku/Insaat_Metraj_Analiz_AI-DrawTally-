using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Models;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Poz/Rayiç/Alias kütüphanesi tüm kullanıcılar arasında ortak olduğu için PozKutuphanesi
/// (singleton, ÇŞB 2026 verisiyle bir kez veritabanından yüklenip bellekte tutulur) üzerinden
/// okunur — bkz. Program.cs, PozKutuphanesi.SeedVeYukleAsync. Projeler ve metraj kalemleri ise
/// kullanıcıya özel olduğundan veritabanında (ApplicationDbContext) saklanır — bkz. ProjeBul,
/// ProjeleriListele, ProjeEkle.
/// </summary>
public class VeriDeposu
{
    private readonly ApplicationDbContext _db;
    private readonly PozKutuphanesi _kutuphane;

    public List<Rayic> Rayicler => _kutuphane.Rayicler;
    public List<Poz> Pozlar => _kutuphane.Pozlar;
    public List<Alias> Aliaslar => _kutuphane.Aliaslar;

    public VeriDeposu(ApplicationDbContext db, PozKutuphanesi kutuphane)
    {
        _db = db;
        _kutuphane = kutuphane;
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

    /// <summary>Serbest metin ile poz arar — bkz. PozKutuphanesi.PozAra.</summary>
    public Poz? PozAra(string metin) => _kutuphane.PozAra(metin);

    /// <summary>
    /// CSV içeriğini satır satır işleyip verilen projeye metraj kalemleri ekler.
    /// Beklenen format: PozKoduVeyaAlias;OlcumDetayi;Miktar
    /// Her satır için sonuç (başarılı/başarısız + sebep) döner, böylece kullanıcı
    /// hangi satırların eşleşmediğini görüp elle düzeltebilir.
    /// </summary>
    public async Task<List<ImportSonucSatiri>> ProjeyeCsvImportEt(Proje proje, string csvIcerik)
    {
        var sonuclar = new List<ImportSonucSatiri>();
        var eklenenKayitlar = new List<(ImportSonucSatiri Sonuc, MetrajKalemiKaydi Kayit)>();
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

            if (!decimal.TryParse(miktarMetni, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var miktar))
            {
                sonuc.Basarili = false;
                sonuc.Mesaj = $"Miktar sayıya çevrilemedi: \"{parcalar[2]}\"";
                sonuclar.Add(sonuc);
                continue;
            }

            var eslesenPoz = PozAra(pozAramaMetni);
            if (eslesenPoz == null)
            {
                sonuc.Basarili = false;
                sonuc.Mesaj = $"\"{pozAramaMetni}\" hiçbir poz veya kısaltmaya eşleşmedi — aşağıdan elle poz seçip ekleyebilirsin";
                sonuc.PozEksik = true;
                sonuc.OlcumDetayiTaslak = olcumDetayi;
                sonuc.MiktarTaslak = miktar;
                sonuclar.Add(sonuc);
                continue;
            }

            var kayit = new MetrajKalemiKaydi
            {
                ProjeKaydiId = proje.Id,
                PozId = eslesenPoz.Id,
                OlcumDetayi = olcumDetayi,
                Miktar = miktar
            };
            _db.MetrajKalemleri.Add(kayit);

            sonuc.Basarili = true;
            sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
            sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad})";
            sonuclar.Add(sonuc);
            eklenenKayitlar.Add((sonuc, kayit));
        }

        await _db.SaveChangesAsync();
        foreach (var (sonuc, kayit) in eklenenKayitlar)
            sonuc.EklenenMetrajKalemiId = kayit.Id;

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
        var eklenenKayitlar = new List<(ImportSonucSatiri Sonuc, MetrajKalemiKaydi Kayit)>();
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

                // 2) Satırın sonundaki sayıyı miktar olarak al
                var miktarEslesme = sondakiSayiDeseni.Match(satir);
                if (!miktarEslesme.Success ||
                    !decimal.TryParse(miktarEslesme.Value.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var miktar))
                {
                    sonuc.Basarili = false;
                    sonuc.Mesaj = eslesenPoz == null
                        ? "Satırda bilinen bir poz kodu/kısaltma ve geçerli bir miktar bulunamadı"
                        : $"Poz bulundu ({eslesenPoz.PozKodu}) ama satır sonunda geçerli bir miktar bulunamadı";
                    sonuclar.Add(sonuc);
                    continue;
                }

                var olcumDetayi = satir;
                if (kodEslesme.Success) olcumDetayi = olcumDetayi.Replace(kodEslesme.Value, "").Trim();
                olcumDetayi = olcumDetayi.Replace(miktarEslesme.Value, "").Trim(' ', '-', ':', ';');
                if (olcumDetayi.Length == 0) olcumDetayi = "(PDF'den otomatik alındı)";

                if (eslesenPoz == null)
                {
                    sonuc.Basarili = false;
                    sonuc.Mesaj = "Satırda bilinen bir poz kodu veya kısaltma bulunamadı — aşağıdan elle poz seçip ekleyebilirsin";
                    sonuc.PozEksik = true;
                    sonuc.OlcumDetayiTaslak = olcumDetayi;
                    sonuc.MiktarTaslak = miktar;
                    sonuclar.Add(sonuc);
                    continue;
                }

                var kayit = new MetrajKalemiKaydi
                {
                    ProjeKaydiId = proje.Id,
                    PozId = eslesenPoz.Id,
                    OlcumDetayi = olcumDetayi,
                    Miktar = miktar
                };
                _db.MetrajKalemleri.Add(kayit);

                sonuc.Basarili = true;
                sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
                sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad}), miktar: {miktar}";
                sonuclar.Add(sonuc);
                eklenenKayitlar.Add((sonuc, kayit));
            }
        }

        await _db.SaveChangesAsync();
        foreach (var (sonuc, kayit) in eklenenKayitlar)
            sonuc.EklenenMetrajKalemiId = kayit.Id;

        return sonuclar;
    }

    /// <summary>Bir çizim analizi (PDF veya DWG) sonucu kullanıcı tarafından onaylandığında çağrılır — projeye metraj kalemi ekler.</summary>
    public async Task OdaAnalizSonucunuOnayla(Proje proje, CizimAnalizSonucu sonuc, Poz poz)
    {
        var katBilgisi = string.IsNullOrWhiteSpace(sonuc.KatAdi) ? "" : $", kat: {sonuc.KatAdi}";
        var kayit = new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = $"AI çizim analiziyle eklendi — oda: {sonuc.OdaAdi}{katBilgisi} ({sonuc.KaynakTuru})",
            Miktar = sonuc.AlanM2
        };
        _db.MetrajKalemleri.Add(kayit);
        await _db.SaveChangesAsync();
        sonuc.OnaylandiMi = true;
        sonuc.EklenenMetrajKalemiId = kayit.Id;
    }

    /// <summary>
    /// CSV/PDF import satırı ilk seferde hiçbir poz/kısaltmaya eşleşmediğinde (PozEksik=true),
    /// kullanıcı listeden elle bir poz seçtiğinde bu metotla tamamlanır.
    /// </summary>
    public async Task ImportSatiriniManuelPozIleTamamla(Proje proje, ImportSonucSatiri sonuc, Poz poz)
    {
        if (sonuc.MiktarTaslak is not { } miktar) return;

        var kayit = new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = sonuc.OlcumDetayiTaslak,
            Miktar = miktar
        };
        _db.MetrajKalemleri.Add(kayit);
        await _db.SaveChangesAsync();

        sonuc.Basarili = true;
        sonuc.PozEksik = false;
        sonuc.EslesenPozKodu = poz.PozKodu;
        sonuc.Mesaj = $"Elle poz seçilerek eklendi -> {poz.PozKodu} ({poz.Ad})";
        sonuc.EklenenMetrajKalemiId = kayit.Id;
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

    /// <summary>
    /// Bir metraj kalemini projeden siler (import/AI önerisiyle veya elle eklenmiş olabilir).
    /// Sadece kalemin ait olduğu proje verilen proje ile eşleşiyorsa siler — başka kullanıcının
    /// kalemini id tahmin ederek silmeyi engeller.
    /// </summary>
    public async Task<bool> MetrajKalemiSil(Proje proje, int metrajKalemiId)
    {
        var kayit = await _db.MetrajKalemleri.FirstOrDefaultAsync(k => k.Id == metrajKalemiId && k.ProjeKaydiId == proje.Id);
        if (kayit == null) return false;

        _db.MetrajKalemleri.Remove(kayit);
        await _db.SaveChangesAsync();
        return true;
    }
}
