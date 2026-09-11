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
        SozlesmeBedeli = kayit.SozlesmeBedeli,
        VarsayilanAvansOrani = kayit.VarsayilanAvansOrani,
        VarsayilanTeminatOrani = kayit.VarsayilanTeminatOrani,
        VarsayilanStopajOrani = kayit.VarsayilanStopajOrani,
        VarsayilanKdvOrani = kayit.VarsayilanKdvOrani,
        AlanM2 = kayit.AlanM2,
        MetrajKalemleri = kayit.MetrajKalemleri
            .Select(k => PozIdIleBul(k.PozId) is { } poz
                ? new MetrajKalemi { Id = k.Id, Poz = poz, OlcumDetayi = k.OlcumDetayi, Miktar = k.Miktar, Disiplin = k.Disiplin }
                : null)
            .Where(k => k != null)
            .Select(k => k!)
            .ToList()
    };

    /// <summary>Hakediş hesaplarında kullanılan sözleşme bedeli, varsayılan oranları ve proje alanını (m² — hakediş kredisi hesabında kullanılır) günceller.</summary>
    public async Task ProjeAyarlariGuncelle(string sahipId, int projeId, decimal sozlesmeBedeli, decimal avansOrani, decimal teminatOrani, decimal stopajOrani, decimal kdvOrani, decimal alanM2)
    {
        var kayit = await _db.Projeler.FirstOrDefaultAsync(p => p.Id == projeId && p.SahipId == sahipId);
        if (kayit == null) return;

        kayit.SozlesmeBedeli = sozlesmeBedeli;
        kayit.VarsayilanAvansOrani = avansOrani;
        kayit.VarsayilanTeminatOrani = teminatOrani;
        kayit.VarsayilanStopajOrani = stopajOrani;
        kayit.VarsayilanKdvOrani = kdvOrani;
        kayit.AlanM2 = alanM2;
        await _db.SaveChangesAsync();
    }

    /// <summary>Ay değiştiyse kullanıcının hakediş kredisini planının aylık limitine sıfırlar (tembel/lazy yenileme — arka planda zamanlanmış bir iş yok).</summary>
    private static void KrediyiGerekirseYenile(ApplicationUser kullanici)
    {
        var buAy = DateTime.UtcNow.Year * 100 + DateTime.UtcNow.Month;
        if (kullanici.HakedisKredisiDonemi != buAy)
        {
            kullanici.HakedisKredisiDonemi = buAy;
            kullanici.KalanHakedisKredisi = UyelikServisi.AylikHakedisKredisi(kullanici.EtkinPlan);
        }
    }

    /// <summary>
    /// Kredi harcamadan, bir projede yeni hakediş oluşturmanın kaç krediye mal olacağını ve
    /// kullanıcının bu ay kalan kredisini döner — form/liste sayfalarında önizleme için kullanılır.
    /// </summary>
    public async Task<(int GerekenKredi, int KalanKredi, bool Yetiyor)> HakedisKrediDurumu(string sahipId, int projeId)
    {
        var proje = await _db.Projeler.FirstOrDefaultAsync(p => p.Id == projeId && p.SahipId == sahipId);
        var kullanici = await _db.Users.FirstOrDefaultAsync(u => u.Id == sahipId);
        if (proje == null || kullanici == null) return (1, 0, false);

        KrediyiGerekirseYenile(kullanici);
        await _db.SaveChangesAsync();

        var gereken = UyelikServisi.HakedisKrediMaliyeti(proje.AlanM2);
        return (gereken, kullanici.KalanHakedisKredisi, kullanici.KalanHakedisKredisi >= gereken);
    }

    /// <summary>Kullanıcının bu ayki kalan hakediş kredisini döner (gerekirse tembel olarak yeniler) — üst bar gibi genel görünürlük için.</summary>
    public async Task<int> KullaniciKalanKredisi(string sahipId)
    {
        var kullanici = await _db.Users.FirstOrDefaultAsync(u => u.Id == sahipId);
        if (kullanici == null) return 0;

        KrediyiGerekirseYenile(kullanici);
        await _db.SaveChangesAsync();
        return kullanici.KalanHakedisKredisi;
    }

    /// <summary>
    /// Ek hakediş kredisi ekler (Profil sayfasındaki kredi paketleri). Ödeme entegrasyonu henüz
    /// yok — plan değişimindeki gibi (bkz. Profil.razor PlanSec) şimdilik anında ve ücretsiz uygulanır.
    /// </summary>
    public async Task<int> KrediSatinAl(string sahipId, int miktar)
    {
        var kullanici = await _db.Users.FirstOrDefaultAsync(u => u.Id == sahipId);
        if (kullanici == null) return 0;

        KrediyiGerekirseYenile(kullanici);
        kullanici.KalanHakedisKredisi += miktar;
        await _db.SaveChangesAsync();
        return kullanici.KalanHakedisKredisi;
    }

    private Poz? PozIdIleBul(int id) => Pozlar.FirstOrDefault(p => p.Id == id);

    /// <summary>Serbest metin ile poz arar — bkz. PozKutuphanesi.PozAra.</summary>
    public Poz? PozAra(string metin) => _kutuphane.PozAra(metin);

    /// <summary>
    /// CSV içeriğini satır satır işleyip verilen projeye metraj kalemleri ekler.
    /// Beklenen format: PozKoduVeyaAlias;OlcumDetayi;Miktar
    /// Her satır için sonuç (başarılı/başarısız + sebep) döner, böylece kullanıcı
    /// hangi satırların eşleşmediğini görüp elle düzeltebilir.
    /// </summary>
    public async Task<List<ImportSonucSatiri>> ProjeyeCsvImportEt(Proje proje, string csvIcerik, ProjeDisiplini disiplin = ProjeDisiplini.Bilinmiyor)
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
                Miktar = miktar,
                Disiplin = disiplin
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
    public async Task<List<ImportSonucSatiri>> ProjeyePdfImportEt(Proje proje, Stream pdfStream, ProjeDisiplini disiplin = ProjeDisiplini.Bilinmiyor)
    {
        var sonuclar = new List<ImportSonucSatiri>();
        var eklenenKayitlar = new List<(ImportSonucSatiri Sonuc, MetrajKalemiKaydi Kayit)>();
        var pozKoduDeseni = new Regex(@"\d{2}\.\d{3}\.\d{4}");
        var sondakiSayiDeseni = new Regex(@"(\d+[.,]\d+|\d+)\s*$");

        using var pdf = PdfDocument.Open(pdfStream);
        int satirNo = 0;

        foreach (var sayfa in pdf.GetPages())
        {
            // NOT: sayfa.Text (PdfPig'in ham metni) KULLANILMIYOR — o, sayfadaki metni içerik akışı
            // sırasına göre birleştirir; mimari bir çizim gibi lineer olmayan bir sayfada bu, sayfanın
            // tamamen farklı yerlerindeki metinlerin (ör. "MUTFAK" etiketiyle uzak bir notun) yan yana
            // yapışmasına yol açar. Bunun yerine kelimeler X/Y konumuna göre fiziksel satırlara
            // gruplanır (bkz. CizimAnalizServisi.DuzMetinSatirlariniCikar) — bu aynı zamanda döndürülmüş
            // (90/270°) ölçü rakamlarını eler ve 180° döndürülmüş etiketlerin ters karakter/kelime
            // sırasını düzeltir.
            var satirlar = CizimAnalizServisi.DuzMetinSatirlariniCikar(sayfa);

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
                    Miktar = miktar,
                    Disiplin = disiplin
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
        // Satır alan (AlanM2) değil adet (blok sayımı) veya uzunluk (kablo/hat) taşıyorsa, metraj
        // miktarı olarak onlar kullanılır — AlanM2 bu satırlarda 0 kalır ve anlamsızdır.
        var miktar = sonuc.Adet.HasValue ? (decimal)sonuc.Adet.Value : sonuc.Uzunluk ?? sonuc.AlanM2;
        var kayit = new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = $"AI çizim analiziyle eklendi — oda: {sonuc.OdaAdi}{katBilgisi} ({sonuc.KaynakTuru})",
            Miktar = miktar,
            Disiplin = sonuc.Disiplin
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
    public async Task ImportSatiriniManuelPozIleTamamla(Proje proje, ImportSonucSatiri sonuc, Poz poz, ProjeDisiplini disiplin = ProjeDisiplini.Bilinmiyor)
    {
        if (sonuc.MiktarTaslak is not { } miktar) return;

        var kayit = new MetrajKalemiKaydi
        {
            ProjeKaydiId = proje.Id,
            PozId = poz.Id,
            OlcumDetayi = sonuc.OlcumDetayiTaslak,
            Miktar = miktar,
            Disiplin = disiplin
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

    /// <summary>Starter kullanıcıya yükseltme daveti gösterip göstermeyeceğine karar vermek için — ilerleme verisini hiç çekmeden, projede en az bir hakediş var mı diye bakar.</summary>
    public async Task<bool> HakedisVarMi(string sahipId, int projeId)
    {
        var projeVarMi = await _db.Projeler.AnyAsync(p => p.Id == projeId && p.SahipId == sahipId);
        if (!projeVarMi) return false;

        return await _db.Hakedisler.AnyAsync(h => h.ProjeKaydiId == projeId);
    }

    /// <summary>
    /// Bir projenin en güncel (en yüksek HakedisNo'lu) hakedişindeki kalem bazlı kümülatif tamamlanma
    /// yüzdelerini döner — Keşif Özeti'nde salt-okunur "tamamlanan/kalan" görünümü için kullanılır.
    /// Hiç hakediş yoksa null döner (metraj kayıtlarına hiç dokunulmaz, sadece bu görünüm türetilir).
    /// </summary>
    public async Task<Dictionary<int, decimal>?> SonHakedisKalemYuzdeleri(string sahipId, int projeId)
    {
        var projeVarMi = await _db.Projeler.AnyAsync(p => p.Id == projeId && p.SahipId == sahipId);
        if (!projeVarMi) return null;

        var sonKayit = await _db.Hakedisler
            .Where(h => h.ProjeKaydiId == projeId)
            .Include(h => h.Kalemler)
            .OrderByDescending(h => h.HakedisNo)
            .FirstOrDefaultAsync();

        return sonKayit?.Kalemler.ToDictionary(k => k.MetrajKalemiKaydiId, k => k.KumulatifYuzde);
    }

    /// <summary>Bir projenin hakedişlerini hakediş no sırasıyla özet olarak listeler.</summary>
    public async Task<List<HakedisOzeti>> HakedisleriListele(string sahipId, int projeId)
    {
        var projeVarMi = await _db.Projeler.AnyAsync(p => p.Id == projeId && p.SahipId == sahipId);
        if (!projeVarMi) return new();

        var kayitlar = await _db.Hakedisler
            .Where(h => h.ProjeKaydiId == projeId)
            .Include(h => h.Kalemler)
            .OrderBy(h => h.HakedisNo)
            .ToListAsync();

        var proje = await ProjeBul(sahipId, projeId);
        if (proje == null) return new();

        return kayitlar
            .Select(k => HakedisiDomaineCevir(k, proje, kayitlar))
            .Select(h => new HakedisOzeti { Id = h.Id, HakedisNo = h.HakedisNo, Tarih = h.Tarih, NetOdenecekTutar = h.NetOdenecekTutar() })
            .ToList();
    }

    /// <summary>Belirli bir hakedişi (kalemleriyle, önceki hakedişe göre kümülatif farkı hesaplanmış olarak) döner.</summary>
    public async Task<Hakedis?> HakedisBul(string sahipId, int projeId, int hakedisId)
    {
        var proje = await ProjeBul(sahipId, projeId);
        if (proje == null) return null;

        var kayit = await _db.Hakedisler
            .Include(h => h.Kalemler)
            .FirstOrDefaultAsync(h => h.Id == hakedisId && h.ProjeKaydiId == projeId);
        if (kayit == null) return null;

        var hepsi = await _db.Hakedisler.Where(h => h.ProjeKaydiId == projeId).Include(h => h.Kalemler).ToListAsync();
        return HakedisiDomaineCevir(kayit, proje, hepsi);
    }

    private Hakedis HakedisiDomaineCevir(HakedisKaydi kayit, Proje proje, List<HakedisKaydi> ayniProjedekiHepsi)
    {
        var oncekiKayit = ayniProjedekiHepsi
            .Where(h => h.HakedisNo < kayit.HakedisNo)
            .OrderByDescending(h => h.HakedisNo)
            .FirstOrDefault();

        return new Hakedis
        {
            Id = kayit.Id,
            HakedisNo = kayit.HakedisNo,
            Tarih = kayit.Tarih,
            AvansOrani = kayit.AvansOrani,
            TeminatOrani = kayit.TeminatOrani,
            StopajOrani = kayit.StopajOrani,
            KdvOrani = kayit.KdvOrani,
            FiyatFarkiOrani = kayit.FiyatFarkiOrani,
            FiyatFarkiTutari = kayit.FiyatFarkiTutari,
            Kalemler = kayit.Kalemler
                .Select(hk => proje.MetrajKalemleri.FirstOrDefault(mk => mk.Id == hk.MetrajKalemiKaydiId) is { } metrajKalemi
                    ? new HakedisKalemi
                    {
                        MetrajKalemi = metrajKalemi,
                        KumulatifYuzde = hk.KumulatifYuzde,
                        OncekiKumulatifYuzde = oncekiKayit?.Kalemler.FirstOrDefault(o => o.MetrajKalemiKaydiId == hk.MetrajKalemiKaydiId)?.KumulatifYuzde ?? 0
                    }
                    : null)
                .Where(hk => hk != null)
                .Select(hk => hk!)
                .ToList()
        };
    }

    /// <summary>
    /// Hakediş formunun başlangıç durumunu hazırlar. hakedisId verilmişse var olan hakedişi
    /// (HakedisBul ile) döner; null ise proje varsayılan oranlarıyla ve bir önceki hakedişin
    /// kümülatif yüzdeleriyle (değişiklik yoksa aynı kalır) başlayan boş bir taslak oluşturur.
    /// </summary>
    public async Task<Hakedis?> HakedisFormuHazirla(string sahipId, int projeId, int? hakedisId)
    {
        if (hakedisId is { } id) return await HakedisBul(sahipId, projeId, id);

        var proje = await ProjeBul(sahipId, projeId);
        if (proje == null) return null;

        var hepsi = await _db.Hakedisler.Where(h => h.ProjeKaydiId == projeId).Include(h => h.Kalemler).ToListAsync();
        var sonKayit = hepsi.OrderByDescending(h => h.HakedisNo).FirstOrDefault();

        return new Hakedis
        {
            HakedisNo = (sonKayit?.HakedisNo ?? 0) + 1,
            Tarih = DateOnly.FromDateTime(DateTime.Now),
            AvansOrani = proje.VarsayilanAvansOrani,
            TeminatOrani = proje.VarsayilanTeminatOrani,
            StopajOrani = proje.VarsayilanStopajOrani,
            KdvOrani = proje.VarsayilanKdvOrani,
            Kalemler = proje.MetrajKalemleri.Select(mk =>
            {
                var oncekiYuzde = sonKayit?.Kalemler.FirstOrDefault(k => k.MetrajKalemiKaydiId == mk.Id)?.KumulatifYuzde ?? 0;
                return new HakedisKalemi { MetrajKalemi = mk, OncekiKumulatifYuzde = oncekiYuzde, KumulatifYuzde = oncekiYuzde };
            }).ToList()
        };
    }

    /// <summary>
    /// Verilen kümülatif yüzdelerle projede yeni bir hakediş oluşturur (mevcutHakedisId null ise)
    /// veya var olan bir hakedişi günceller. Bir kalemin kümülatif yüzdesi bir önceki hakedişten
    /// düşük giriliyorsa kaydetmeyi reddeder — kümülatif tamamlanma geriye gidemez.
    /// </summary>
    public async Task<HakedisKaydetSonucu> HakedisKaydet(
        string sahipId, int projeId, int? mevcutHakedisId, DateOnly tarih,
        decimal avansOrani, decimal teminatOrani, decimal stopajOrani, decimal kdvOrani,
        decimal fiyatFarkiOrani, decimal fiyatFarkiTutari,
        Dictionary<int, decimal> kalemYuzdeleri)
    {
        var projeKaydi = await _db.Projeler.FirstOrDefaultAsync(p => p.Id == projeId && p.SahipId == sahipId);
        if (projeKaydi == null) return new HakedisKaydetSonucu { Basarili = false, Mesaj = "Proje bulunamadı." };

        var digerHakedisler = await _db.Hakedisler
            .Where(h => h.ProjeKaydiId == projeId && h.Id != (mevcutHakedisId ?? 0))
            .Include(h => h.Kalemler)
            .ToListAsync();

        HakedisKaydi kayit;
        if (mevcutHakedisId is { } id)
        {
            var bulunan = await _db.Hakedisler.Include(h => h.Kalemler).FirstOrDefaultAsync(h => h.Id == id && h.ProjeKaydiId == projeId);
            if (bulunan == null) return new HakedisKaydetSonucu { Basarili = false, Mesaj = "Hakediş bulunamadı." };
            kayit = bulunan;
        }
        else
        {
            kayit = new HakedisKaydi
            {
                ProjeKaydiId = projeId,
                HakedisNo = digerHakedisler.Count == 0 ? 1 : digerHakedisler.Max(h => h.HakedisNo) + 1
            };
        }

        var oncekiKayit = digerHakedisler
            .Where(h => h.HakedisNo < kayit.HakedisNo)
            .OrderByDescending(h => h.HakedisNo)
            .FirstOrDefault();

        foreach (var (metrajKalemiId, yeniYuzde) in kalemYuzdeleri)
        {
            var oncekiYuzde = oncekiKayit?.Kalemler.FirstOrDefault(k => k.MetrajKalemiKaydiId == metrajKalemiId)?.KumulatifYuzde ?? 0;
            if (yeniYuzde < oncekiYuzde)
            {
                var kalemAdi = (await _db.MetrajKalemleri.FindAsync(metrajKalemiId))?.OlcumDetayi ?? $"#{metrajKalemiId}";
                return new HakedisKaydetSonucu
                {
                    Basarili = false,
                    Mesaj = $"\"{kalemAdi}\" kalemi için girilen kümülatif yüzde (%{yeniYuzde:0.##}) bir önceki hakedişten (%{oncekiYuzde:0.##}) düşük olamaz."
                };
            }
        }

        // Kredi sadece YENİ hakediş oluştururken harcanır — düzenleme/görüntüleme/export ücretsiz.
        if (mevcutHakedisId == null)
        {
            var kullanici = await _db.Users.FirstOrDefaultAsync(u => u.Id == sahipId);
            if (kullanici == null) return new HakedisKaydetSonucu { Basarili = false, Mesaj = "Kullanıcı bulunamadı." };

            KrediyiGerekirseYenile(kullanici);
            var gerekenKredi = UyelikServisi.HakedisKrediMaliyeti(projeKaydi.AlanM2);
            if (kullanici.KalanHakedisKredisi < gerekenKredi)
            {
                return new HakedisKaydetSonucu
                {
                    Basarili = false,
                    Mesaj = $"Bu ay için hakediş krediniz yetersiz (gereken: {gerekenKredi}, kalan: {kullanici.KalanHakedisKredisi}). Krediniz bir sonraki ay yenilenecek."
                };
            }
            kullanici.KalanHakedisKredisi -= gerekenKredi;
        }

        kayit.Tarih = tarih;
        kayit.AvansOrani = avansOrani;
        kayit.TeminatOrani = teminatOrani;
        kayit.StopajOrani = stopajOrani;
        kayit.KdvOrani = kdvOrani;
        kayit.FiyatFarkiOrani = fiyatFarkiOrani;
        kayit.FiyatFarkiTutari = fiyatFarkiTutari;

        kayit.Kalemler.Clear();
        foreach (var (metrajKalemiId, yeniYuzde) in kalemYuzdeleri)
        {
            kayit.Kalemler.Add(new HakedisKalemiKaydi { MetrajKalemiKaydiId = metrajKalemiId, KumulatifYuzde = yeniYuzde });
        }

        if (mevcutHakedisId == null)
            _db.Hakedisler.Add(kayit);

        await _db.SaveChangesAsync();
        return new HakedisKaydetSonucu { Basarili = true, Mesaj = "Hakediş kaydedildi.", HakedisId = kayit.Id };
    }
}
