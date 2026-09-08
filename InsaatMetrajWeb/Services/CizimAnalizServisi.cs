using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using InsaatMetrajWeb.Models;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// PDF ve DWG mimari çizimlerinden oda adı + alan (m²) çıkarıp, onaylanmak üzere
/// aynı sonuç modeline (<see cref="CizimAnalizSonucu"/>) yazan iki ayrı pipeline.
///
/// PDF: metin katmanı varsa (vektörel) yakın metin parçalarını uzamsal olarak
/// kümeleyip yapay zekaya yapılandırtır; metin katmanı yoksa (taranmış) veya bir
/// küme belirsizse, o bölgenin görselini render edip yapay zeka görüşüne gönderir.
///
/// DWG: ACadSharp ile doğrudan okunur (DXF'e çevirme adımı yok). Kapalı poligonların
/// alanı shoelace formülüyle DETERMİNİSTİK hesaplanır (AI tahmini değil), en yakın
/// TEXT/MTEXT metni oda adı olarak eşleştirilir. Gerçek metin varlığı azsa (metin
/// patlatılmış olabilir sinyali) güven düşürülür.
/// </summary>
public class CizimAnalizServisi
{
    private readonly AiSiniflandirmaServisi _ai;
    private readonly VeriDeposu _veri;

    private const int GorselFallbackGuvenEsigi = 50; // bu değerin altındaki metin-tabanlı sonuçlar görsel ile teyit edilir

    public CizimAnalizServisi(AiSiniflandirmaServisi ai, VeriDeposu veri)
    {
        _ai = ai;
        _veri = veri;
    }

    // ---------------------------------------------------------------- PDF ----

    public async Task<List<CizimAnalizSonucu>> PdfAnalizEt(Stream pdfStream, string dosyaAdi)
    {
        using var bellekAkisi = new MemoryStream();
        await pdfStream.CopyToAsync(bellekAkisi);
        var pdfBaytlari = bellekAkisi.ToArray();

        var sonuclar = new List<CizimAnalizSonucu>();

        // 1. geçiş: tüm sayfaların kelime/kümelerini topla — AI'ya henüz hiçbir şey gönderilmedi.
        var sayfaVerileri = new List<(int SayfaNo, double Genislik, double Yukseklik, List<Word> Kelimeler,
            List<(string Metin, double Sol, double Alt, double Sag, double Ust)> Kumeler)>();

        using (var pdf = PdfDocument.Open(pdfBaytlari))
        {
            foreach (var sayfa in pdf.GetPages())
            {
                // 2.5 — ardışık tekrarı en başta çök (OCR damga/kaşe artefaktı ihtimaline karşı).
                var hamKelimeler = CizimGurultuFiltresi.ArdisikTekrariColaps(sayfa.GetWords().ToList(), w => w.Text);
                var kumeler = hamKelimeler.Count == 0
                    ? new List<(string, double, double, double, double)>()
                    : MetinKumeleriOlustur(hamKelimeler);
                sayfaVerileri.Add((sayfa.Number, sayfa.Width, sayfa.Height, hamKelimeler, kumeler));
            }
        }

        var tumKumeMetinleri = sayfaVerileri.SelectMany(s => s.Kumeler.Select(k => k.Metin)).ToList();

        // 2.4 (tekrar tabanlı) — sayfaların %80+'inde aynen tekrarlayan bloklar (pafta/antet/legend).
        var paftaTekrarBloklari = CizimGurultuFiltresi.TekrarEdenBloklariBul(tumKumeMetinleri);

        // Bölüm 4 — proje disiplinini tüm sayfalardaki metinlerden tespit et, AI'ya bağlam olarak ver.
        var disiplin = DisiplinTespitServisi.TespitEt(tumKumeMetinleri);

        foreach (var sayfaVerisi in sayfaVerileri)
        {
            if (sayfaVerisi.Kelimeler.Count == 0)
            {
                // Taranmış (resim) sayfa — metin katmanı yok, doğrudan görsele bak.
                var gorsel = SayfaGorseliOlustur(pdfBaytlari, sayfaVerisi.SayfaNo, sayfaVerisi.Genislik, sayfaVerisi.Yukseklik, kirpma: null);
                var oda = await _ai.GorseldenOdaCikarAsync(gorsel, _veri.Pozlar, disiplin);
                sonuclar.Add(SonucaCevir(oda, KaynakTuru.AIGorsel, $"Sayfa {sayfaVerisi.SayfaNo}", disiplin));
                continue;
            }

            foreach (var kume in sayfaVerisi.Kumeler)
            {
                // Ön filtre: kot/pafta/diğer gürültü ile ölçü zinciri ve tekrar eden pafta blokları
                // AI'ya hiç gönderilmez (Bölüm 2).
                if (CizimGurultuFiltresi.GurultuMu(kume.Metin) ||
                    CizimGurultuFiltresi.OlcuZinciriBlokMu(kume.Metin) ||
                    paftaTekrarBloklari.Contains(kume.Metin.Trim()))
                {
                    continue;
                }

                var oda = await _ai.MetinKumesindenOdaCikarAsync(kume.Metin, _veri.Pozlar, disiplin);
                var kaynak = KaynakTuru.VektorMetin;

                if (oda.Guven < GorselFallbackGuvenEsigi)
                {
                    // Metin kümesi belirsiz — o bölgenin kırpılmış görselini yapay zeka görüşüne gönder.
                    var gorsel = SayfaGorseliOlustur(pdfBaytlari, sayfaVerisi.SayfaNo, sayfaVerisi.Genislik, sayfaVerisi.Yukseklik,
                        (kume.Sol, kume.Alt, kume.Sag, kume.Ust));
                    var gorselOda = await _ai.GorseldenOdaCikarAsync(gorsel, _veri.Pozlar, disiplin);
                    if (gorselOda.Guven > oda.Guven)
                    {
                        oda = gorselOda;
                        kaynak = KaynakTuru.AIGorsel;
                    }
                }

                sonuclar.Add(SonucaCevir(oda, kaynak, $"Sayfa {sayfaVerisi.SayfaNo}", disiplin));
            }
        }

        return sonuclar;
    }

    private static CizimAnalizSonucu SonucaCevir(OdaYapilandirmaSonucu oda, KaynakTuru kaynak, string varsayilanKat, ProjeDisiplini disiplin)
    {
        return new CizimAnalizSonucu
        {
            OdaAdi = string.IsNullOrWhiteSpace(oda.OdaAdi) ? "(Tanımlanamadı)" : oda.OdaAdi,
            AlanM2 = oda.AlanM2 ?? 0m,
            KatAdi = string.IsNullOrWhiteSpace(oda.KatAdi) ? varsayilanKat : oda.KatAdi,
            KaynakTuru = kaynak,
            GuvenSkoru = GuveneCevir(oda.Guven),
            OnerilenPozId = oda.OnerilenPozId,
            OneriGerekcesi = oda.Gerekce,
            KullanilanModel = oda.KullanilanModel,
            Disiplin = disiplin
        };
    }

    private static GuvenSkoru GuveneCevir(int guven) =>
        guven >= 80 ? GuvenSkoru.Yuksek : guven >= 50 ? GuvenSkoru.Orta : GuvenSkoru.Dusuk;

    /// <summary>
    /// Kelimeleri önce satırlara (aynı yükseklikte, yakın Y merkezli), sonra dikey
    /// boşluğu küçük olan komşu satırları tek bir kümeye (ör. "Yatak Odası" + "alan: 14.2 m²")
    /// birleştirir. Kesin bir tablo/etiket yapısı varsayılmaz — bu bir sezgisel gruplama.
    /// </summary>
    private static List<(string Metin, double Sol, double Alt, double Sag, double Ust)> MetinKumeleriOlustur(List<Word> kelimeler)
    {
        var satirlar = new List<List<Word>>();
        foreach (var kelime in kelimeler.OrderByDescending(k => k.BoundingBox.Top))
        {
            var merkezY = (kelime.BoundingBox.Top + kelime.BoundingBox.Bottom) / 2;
            var satir = satirlar.FirstOrDefault(s =>
            {
                var ortMerkez = s.Average(k => (k.BoundingBox.Top + k.BoundingBox.Bottom) / 2);
                var ortYukseklik = Math.Max(s.Average(k => k.BoundingBox.Height), 1);
                return Math.Abs(merkezY - ortMerkez) < ortYukseklik * 0.6;
            });
            if (satir != null) satir.Add(kelime);
            else satirlar.Add(new List<Word> { kelime });
        }

        var satirBilgisi = satirlar
            .Select(s => new
            {
                Metin = string.Join(" ", s.OrderBy(k => k.BoundingBox.Left).Select(k => k.Text)),
                Ust = s.Max(k => k.BoundingBox.Top),
                Alt = s.Min(k => k.BoundingBox.Bottom),
                Sol = s.Min(k => k.BoundingBox.Left),
                Sag = s.Max(k => k.BoundingBox.Right),
                Yukseklik = Math.Max(s.Average(k => k.BoundingBox.Height), 1)
            })
            .OrderByDescending(s => s.Ust)
            .ToList();

        var kumeIndeksleri = new List<List<int>>();
        for (int i = 0; i < satirBilgisi.Count; i++)
        {
            var mevcutKume = kumeIndeksleri.Count > 0 ? kumeIndeksleri[^1] : null;
            if (mevcutKume != null)
            {
                var oncekiSatir = satirBilgisi[mevcutKume[^1]];
                var dikeyBosluk = oncekiSatir.Alt - satirBilgisi[i].Ust;
                if (dikeyBosluk < oncekiSatir.Yukseklik * 1.8)
                {
                    mevcutKume.Add(i);
                    continue;
                }
            }
            kumeIndeksleri.Add(new List<int> { i });
        }

        return kumeIndeksleri
            .Select(k => k.Select(i => satirBilgisi[i]).ToList())
            .Select(satirlarSecili => (
                Metin: string.Join("\n", satirlarSecili.Select(s => s.Metin)),
                Sol: satirlarSecili.Min(s => s.Sol),
                Alt: satirlarSecili.Min(s => s.Alt),
                Sag: satirlarSecili.Max(s => s.Sag),
                Ust: satirlarSecili.Max(s => s.Ust)
            ))
            .Where(k => k.Metin.Trim().Length >= 3)
            .ToList();
    }

    /// <summary>Sayfayı (veya <paramref name="kirpma"/> verilmişse sadece o bölgeyi) PNG olarak render eder.</summary>
    private static byte[] SayfaGorseliOlustur(byte[] pdfBaytlari, int sayfaNo, double sayfaGenislik, double sayfaYukseklik,
        (double Sol, double Alt, double Sag, double Ust)? kirpma)
    {
        const int Dpi = 150;
        using var tamSayfa = Conversion.ToImage(pdfBaytlari, sayfaNo - 1, options: new RenderOptions(Dpi: Dpi));

        if (kirpma == null)
            return tamSayfa.Encode(SKEncodedImageFormat.Png, 90).ToArray();

        var olcek = tamSayfa.Width / sayfaGenislik;
        const int Kenarbosluk = 25;
        int x0 = Math.Clamp((int)(kirpma.Value.Sol * olcek) - Kenarbosluk, 0, tamSayfa.Width - 1);
        int x1 = Math.Clamp((int)(kirpma.Value.Sag * olcek) + Kenarbosluk, x0 + 1, tamSayfa.Width);
        int y0 = Math.Clamp((int)((sayfaYukseklik - kirpma.Value.Ust) * olcek) - Kenarbosluk, 0, tamSayfa.Height - 1);
        int y1 = Math.Clamp((int)((sayfaYukseklik - kirpma.Value.Alt) * olcek) + Kenarbosluk, y0 + 1, tamSayfa.Height);

        using var kirpilmis = new SKBitmap(x1 - x0, y1 - y0);
        tamSayfa.ExtractSubset(kirpilmis, new SKRectI(x0, y0, x1, y1));
        return kirpilmis.Encode(SKEncodedImageFormat.Png, 90).ToArray();
    }

    // ---------------------------------------------------------------- DWG ----

    /// <summary>
    /// NOT: ACadSharp kütüphanesi henüz alpha aşamasında; bu ilk sürüm kapalı
    /// LwPolyline/Polyline2D için alan hesabı ve TextEntity/MText için metin
    /// eşleştirmesi yapar. Hatch entity'lerinin sınır yolları (çizgi/yay/spline
    /// karışımı olabilir) bu sürümde işlenmiyor — ileride eklenebilir.
    /// </summary>
    public List<CizimAnalizSonucu> DwgAnalizEt(Stream dosyaStream, string dosyaAdi)
    {
        CadDocument dokuman = dosyaAdi.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
            ? DxfReader.Read(dosyaStream)
            : DwgReader.Read(dosyaStream);

        var modelSpace = dokuman.BlockRecords["*Model_Space"];

        var metinler = new List<(string Deger, double X, double Y, string Katman)>();
        var poligonlar = new List<(List<(double X, double Y)> Noktalar, string Katman)>();

        foreach (var entity in modelSpace.Entities)
        {
            var katmanAdi = entity.Layer?.Name ?? "0";
            switch (entity)
            {
                case TextEntity metin when !string.IsNullOrWhiteSpace(metin.Value):
                    metinler.Add((metin.Value.Trim(), metin.InsertPoint.X, metin.InsertPoint.Y, katmanAdi));
                    break;

                case MText metin when !string.IsNullOrWhiteSpace(metin.Value):
                    metinler.Add((MTextTemizle(metin.Value), metin.InsertPoint.X, metin.InsertPoint.Y, katmanAdi));
                    break;

                case LwPolyline lw when lw.IsClosed && lw.Vertices.Count >= 3:
                    poligonlar.Add((lw.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList(), katmanAdi));
                    break;

                case Polyline2D pl when pl.IsClosed && pl.Vertices.Count >= 3:
                    poligonlar.Add((pl.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList(), katmanAdi));
                    break;
            }
        }

        // 2.5 — ardışık tekrarı en başta çök, sonra kot/pafta/diğer gürültü metinlerini ayıkla
        // (oda-adı eşleştirmesine karışmasınlar diye) — bkz. Bölüm 2.
        metinler = CizimGurultuFiltresi
            .ArdisikTekrariColaps(metinler, m => m.Deger)
            .Where(m => !CizimGurultuFiltresi.GurultuMu(m.Deger))
            .ToList();

        // Gerçek TEXT/MTEXT sayısı, poligon sayısına göre çok düşükse metin muhtemelen
        // çizgilere/polyline'lara patlatılmış (exploded) demektir — bu durumda oda adı
        // eşleştirmesi güvenilmez, güven seviyesi düşürülür.
        bool metinPatlatilmisOlabilir = poligonlar.Count > 0 && metinler.Count < poligonlar.Count * 0.3;

        // Bölüm 4 — disiplin tespiti: DWG'de en güçlü sinyal katman adları (4.1.2), ek olarak metinler.
        var katmanAdlari = poligonlar.Select(p => p.Katman).Concat(metinler.Select(m => m.Katman)).Distinct().ToList();
        var disiplin = DisiplinTespitServisi.TespitEt(metinler.Select(m => m.Deger), katmanAdlari);

        var sonuclar = new List<CizimAnalizSonucu>();
        foreach (var (noktalar, katman) in poligonlar)
        {
            var alan = ShoelaceAlan(noktalar);
            if (alan < 0.01) continue; // gürültü / dejenere poligon

            var merkez = AgirlikMerkezi(noktalar);
            var enYakin = metinler
                .Select(m => (Metin: m, Uzaklik: Mesafe(merkez, (m.X, m.Y))))
                .OrderBy(x => x.Uzaklik)
                .FirstOrDefault();

            var araMesafesi = Math.Sqrt(alan) * 2; // alanla orantılı arama yarıçapı
            string odaAdi;
            GuvenSkoru guven;
            if (enYakin.Metin.Deger != null && enYakin.Uzaklik <= araMesafesi)
            {
                odaAdi = enYakin.Metin.Deger;
                guven = metinPatlatilmisOlabilir ? GuvenSkoru.Orta : GuvenSkoru.Yuksek;
            }
            else
            {
                odaAdi = $"(İsimsiz alan — katman: {katman})";
                guven = GuvenSkoru.Dusuk;
            }

            sonuclar.Add(new CizimAnalizSonucu
            {
                OdaAdi = odaAdi,
                AlanM2 = Math.Round((decimal)alan, 2),
                KatAdi = "",
                KaynakTuru = KaynakTuru.VektorGeometri,
                GuvenSkoru = guven,
                OneriGerekcesi = metinPatlatilmisOlabilir
                    ? "Alan shoelace formülüyle deterministik hesaplandı. Çizimde gerçek TEXT/MTEXT azlığı, metnin poligonlara patlatılmış (exploded) olabileceğini gösteriyor — oda adı en yakın metne göre tahmin edildi, elle doğrulayın."
                    : "Alan shoelace formülüyle deterministik hesaplandı, oda adı en yakın metin etiketiyle eşleştirildi.",
                KullanilanModel = "geometrik (shoelace + en-yakın-metin)",
                Disiplin = disiplin
            });
        }

        return sonuclar;
    }

    /// <summary>Katman adı + geometrik sinyalleri (kapalılık, alan) AI hibrit sınıflandırmaya vererek her odaya bir poz önerisi ekler.</summary>
    public async Task DwgPozOnerileriniEkleAsync(List<CizimAnalizSonucu> odalar, string dosyaAdi)
    {
        foreach (var oda in odalar)
        {
            var baglam = $"Kaynak: DWG çizimi ({dosyaAdi})\nOda/alan adı: {oda.OdaAdi}\nKat: {(string.IsNullOrWhiteSpace(oda.KatAdi) ? "belirtilmemiş" : oda.KatAdi)}\nAlan: {oda.AlanM2} m2\nGeometri: kapalı poligon (shoelace ile deterministik hesaplandı)";
            var oneri = await _ai.MetinKumesindenOdaCikarAsync(baglam, _veri.Pozlar, oda.Disiplin);
            oda.OnerilenPozId = oneri.OnerilenPozId;
            oda.OneriGerekcesi = string.IsNullOrWhiteSpace(oneri.Gerekce) ? oda.OneriGerekcesi : oneri.Gerekce;
            oda.KullanilanModel = string.IsNullOrWhiteSpace(oneri.KullanilanModel) ? oda.KullanilanModel : oneri.KullanilanModel;
        }
    }

    private static string MTextTemizle(string mtext) =>
        Regex.Replace(mtext, @"\\[A-Za-z](\d+(\.\d+)?)?;?|[{}]", "").Replace("\\P", "\n").Trim();

    private static double Mesafe((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

    private static (double X, double Y) AgirlikMerkezi(List<(double X, double Y)> noktalar) =>
        (noktalar.Average(n => n.X), noktalar.Average(n => n.Y));

    /// <summary>Shoelace formülü ile kapalı bir poligonun alanını hesaplar.</summary>
    private static double ShoelaceAlan(List<(double X, double Y)> noktalar)
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
