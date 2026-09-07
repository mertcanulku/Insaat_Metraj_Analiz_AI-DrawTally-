using InsaatMetrajWeb.Models;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.IO;
using ACadSharp.Entities;
using CSMath;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Demo amaçlı in-memory veri deposu. Gerçek üründe bu sınıfın yerini
/// bir veritabanı (EF Core + PostgreSQL/SQL Server) alacak, ama arayüz
/// katmanı (Razor sayfaları) bu servise bağlı kalacağı için değişiklik
/// sadece bu sınıfın içinde kalır.
/// </summary>
public class VeriDeposu
{
    public List<Rayic> Rayicler { get; } = new();
    public List<Poz> Pozlar { get; } = new();
    public List<Alias> Aliaslar { get; } = new();
    public List<Proje> Projeler { get; } = new();

    public VeriDeposu()
    {
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

        // --- Örnek proje ---
        var proje = new Proje { Id = 1, Ad = "Örnek Konut Projesi - A Blok" };
        proje.MetrajKalemleri.Add(new MetrajKalemi
        {
            Id = 1,
            Poz = betonPozu,
            OlcumDetayi = "Temel radye: 12.00m x 8.00m x 0.30m",
            Miktar = 12.00m * 8.00m * 0.30m
        });
        Projeler.Add(proje);

        var proje2 = new Proje { Id = 2, Ad = "Ticari Ofis Binası - Kaba İnşaat" };
        proje2.MetrajKalemleri.Add(new MetrajKalemi
        {
            Id = 1,
            Poz = duvarPozu,
            OlcumDetayi = "Zemin kat dış cephe duvarı",
            Miktar = 210.5m
        });
        Projeler.Add(proje2);

        var proje3 = new Proje { Id = 3, Ad = "Villa Projesi - Isıköy" };
        Projeler.Add(proje3); // henüz metraj kalemi girilmemiş yeni proje örneği
    }

    public Proje? ProjeBul(int id) => Projeler.FirstOrDefault(p => p.Id == id);

    public Proje ProjeEkle(string ad)
    {
        var yeniId = Projeler.Count == 0 ? 1 : Projeler.Max(p => p.Id) + 1;
        var yeniProje = new Proje { Id = yeniId, Ad = ad };
        Projeler.Add(yeniProje);
        return yeniProje;
    }

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
    public List<ImportSonucSatiri> ProjeyeCsvImportEt(Proje proje, string csvIcerik)
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

            var yeniId = proje.MetrajKalemleri.Count == 0 ? 1 : proje.MetrajKalemleri.Max(k => k.Id) + 1;
            proje.MetrajKalemleri.Add(new MetrajKalemi
            {
                Id = yeniId,
                Poz = eslesenPoz,
                OlcumDetayi = olcumDetayi,
                Miktar = miktar
            });

            sonuc.Basarili = true;
            sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
            sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad})";
            sonuclar.Add(sonuc);
        }

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
    public List<ImportSonucSatiri> ProjeyePdfImportEt(Proje proje, Stream pdfStream)
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

                var yeniId = proje.MetrajKalemleri.Count == 0 ? 1 : proje.MetrajKalemleri.Max(k => k.Id) + 1;
                proje.MetrajKalemleri.Add(new MetrajKalemi
                {
                    Id = yeniId,
                    Poz = eslesenPoz,
                    OlcumDetayi = olcumDetayi.Length > 0 ? olcumDetayi : "(PDF'den otomatik alındı)",
                    Miktar = miktar
                });

                sonuc.Basarili = true;
                sonuc.EslesenPozKodu = eslesenPoz.PozKodu;
                sonuc.Mesaj = $"Eklendi -> {eslesenPoz.PozKodu} ({eslesenPoz.Ad}), miktar: {miktar}";
                sonuclar.Add(sonuc);
            }
        }

        return sonuclar;
    }

    /// <summary>
    /// Bir DWG veya DXF dosyasını okuyup, içindeki her katmandaki (layer)
    /// entity'lerden yapay zekanın karar vermesi için geometrik sinyaller
    /// çıkarır: kapalılık, çizgi kalınlığı, ortalama segment uzunluğu,
    /// en/boy oranı, toplam uzunluk/alan. Katman ismiyle sınıflandırma
    /// YAPMAZ — bu iş kasıtlı olarak AiSiniflandirmaServisi'ne bırakılmıştır,
    /// çünkü her proje farklı katman isimlendirmesi kullanabiliyor.
    ///
    /// NOT: ACadSharp kütüphanesi henüz alpha aşamasında ve bazı entity tipleri
    /// (HATCH, karmaşık bloklar vb.) tam desteklenmiyor olabilir — bu ilk sürüm
    /// Line, LwPolyline ve Polyline (2D) varlıklarını kapsıyor. Çizgi kalınlığı
    /// tespiti LwPolyline'ın ConstantWidth alanına dayanıyor; bu alan boşsa
    /// kalınlık 0 olarak raporlanır (AI o durumda diğer sinyallere ağırlık verir).
    /// </summary>
    public List<CizimKatmanSinyali> CizimSinyalleriniCikar(Stream dosyaStream, string dosyaAdi)
    {
        CadDocument doc = dosyaAdi.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
            ? DxfReader.Read(dosyaStream)
            : DwgReader.Read(dosyaStream);

        var modelSpace = doc.BlockRecords["*Model_Space"];

        var katmanGruplari = new Dictionary<string, (
            int adet, decimal uzunluk, decimal alan, List<double> segmentUzunluklari,
            List<double> kalinliklar, double minX, double minY, double maxX, double maxY)>();

        void NoktayiSinirlaraKat(ref (int adet, decimal uzunluk, decimal alan, List<double> segmentUzunluklari, List<double> kalinliklar, double minX, double minY, double maxX, double maxY) veri, double x, double y)
        {
            veri.minX = Math.Min(veri.minX, x); veri.maxX = Math.Max(veri.maxX, x);
            veri.minY = Math.Min(veri.minY, y); veri.maxY = Math.Max(veri.maxY, y);
        }

        foreach (var entity in modelSpace.Entities)
        {
            var katmanAdi = entity.Layer?.Name ?? "0";
            if (!katmanGruplari.ContainsKey(katmanAdi))
                katmanGruplari[katmanAdi] = (0, 0m, 0m, new List<double>(), new List<double>(),
                    double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

            var veri = katmanGruplari[katmanAdi];
            veri.adet++;

            switch (entity)
            {
                case Line line:
                    var segUzunluk = Mesafe(line.StartPoint, line.EndPoint);
                    veri.uzunluk += (decimal)segUzunluk;
                    veri.segmentUzunluklari.Add(segUzunluk);
                    NoktayiSinirlaraKat(ref veri, line.StartPoint.X, line.StartPoint.Y);
                    NoktayiSinirlaraKat(ref veri, line.EndPoint.X, line.EndPoint.Y);
                    break;

                case LwPolyline lw:
                    var lwNoktalar = lw.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList();
                    veri.uzunluk += (decimal)PolylineUzunluk(lwNoktalar, lw.IsClosed, veri.segmentUzunluklari);
                    if (lw.IsClosed) veri.alan += (decimal)PolylineAlan(lwNoktalar);
                    foreach (var (x, y) in lwNoktalar) NoktayiSinirlaraKat(ref veri, x, y);
                    // ConstantWidth genelde çizim kalınlığı (duvar kalınlığı) sinyali verir.
                    if (lw.ConstantWidth > 0) veri.kalinliklar.Add(lw.ConstantWidth);
                    break;

                case Polyline2D pl:
                    var plNoktalar = pl.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList();
                    veri.uzunluk += (decimal)PolylineUzunluk(plNoktalar, pl.IsClosed, veri.segmentUzunluklari);
                    if (pl.IsClosed) veri.alan += (decimal)PolylineAlan(plNoktalar);
                    foreach (var (x, y) in plNoktalar) NoktayiSinirlaraKat(ref veri, x, y);
                    break;

                default:
                    // Desteklenmeyen entity tipi (Circle, Arc, Hatch, Block Insert vb.)
                    break;
            }

            katmanGruplari[katmanAdi] = veri;
        }

        var sonuclar = new List<CizimKatmanSinyali>();
        foreach (var (katmanAdi, veri) in katmanGruplari)
        {
            var genislik = Math.Max(veri.maxX - veri.minX, 0);
            var yukseklik = Math.Max(veri.maxY - veri.minY, 0);
            var buyukKenar = Math.Max(genislik, yukseklik);
            var kucukKenar = Math.Max(Math.Min(genislik, yukseklik), 0.001); // sıfıra bölmeyi önle

            sonuclar.Add(new CizimKatmanSinyali
            {
                LayerAdi = katmanAdi,
                EntitySayisi = veri.adet,
                KapaliMi = veri.alan > 0,
                OrtSegmentUzunlugu = veri.segmentUzunluklari.Count > 0 ? veri.segmentUzunluklari.Average() : 0,
                CizgiKalinligi = veri.kalinliklar.Count > 0 ? veri.kalinliklar.Average() : 0,
                EnBoyOrani = Math.Round(buyukKenar / kucukKenar, 2),
                ToplamUzunluk = Math.Round((double)veri.uzunluk, 3),
                ToplamAlan = Math.Round((double)veri.alan, 3)
            });
        }

        return sonuclar;
    }

    /// <summary>AI önerisi kullanıcı tarafından onaylandığında çağrılır — projeye metraj kalemi ekler.</summary>
    public void CizimOnerisiniOnayla(Proje proje, CizimKatmanSinyali sinyal, Poz poz)
    {
        var miktar = poz.Birim == "m2" ? sinyal.ToplamAlan : sinyal.ToplamUzunluk;
        var yeniId = proje.MetrajKalemleri.Count == 0 ? 1 : proje.MetrajKalemleri.Max(k => k.Id) + 1;
        proje.MetrajKalemleri.Add(new MetrajKalemi
        {
            Id = yeniId,
            Poz = poz,
            OlcumDetayi = $"AI önerisiyle çizimden alındı — katman: {sinyal.LayerAdi}",
            Miktar = (decimal)miktar
        });
    }

    private static double Mesafe(XYZ a, XYZ b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

    /// <summary>Toplam uzunluğu döner; segmentUzunluklariTopla verilirse her segmentin uzunluğunu da oraya ekler (ortalama hesaplamak için).</summary>
    private static double PolylineUzunluk(List<(double x, double y)> noktalar, bool kapali, List<double>? segmentUzunluklariTopla = null)
    {
        double toplam = 0;
        for (int i = 0; i < noktalar.Count - 1; i++)
        {
            var (x1, y1) = noktalar[i];
            var (x2, y2) = noktalar[i + 1];
            var uzunluk = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            toplam += uzunluk;
            segmentUzunluklariTopla?.Add(uzunluk);
        }
        if (kapali && noktalar.Count > 1)
        {
            var (x1, y1) = noktalar[^1];
            var (x2, y2) = noktalar[0];
            var uzunluk = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            toplam += uzunluk;
            segmentUzunluklariTopla?.Add(uzunluk);
        }
        return toplam;
    }

    /// <summary>Shoelace formülü ile kapalı bir poligonun alanını hesaplar.</summary>
    private static double PolylineAlan(List<(double x, double y)> noktalar)
    {
        double toplam = 0;
        int n = noktalar.Count;
        for (int i = 0; i < n; i++)
        {
            var (x1, y1) = noktalar[i];
            var (x2, y2) = noktalar[(i + 1) % n];
            toplam += (x1 * y2) - (x2 * y1);
        }
        return Math.Abs(toplam) / 2.0;
    }
}
