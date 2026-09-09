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
                // Öncesinde döndürülmüş metinler düzeltilir/elenir — bkz. KelimeleriDuzelt.
                var hamKelimeler = CizimGurultuFiltresi.ArdisikTekrariColaps(KelimeleriDuzelt(sayfa.GetWords().ToList()), w => w.Text);
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
                if (oda.Ilgili)
                    sonuclar.Add(SonucaCevir(oda, KaynakTuru.AIGorsel, $"Sayfa {sayfaVerisi.SayfaNo}", disiplin, dosyaAdi));
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

                // Görsel teyide sadece AI ilgili bir oda/eleman bulduğunu ama düşük güvenle
                // bulduğunu söylediğinde başvurulur — zaten irrelevant kararı için görsele
                // bakmanın bir faydası yok.
                if (oda.Ilgili && oda.Guven < GorselFallbackGuvenEsigi)
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

                // AI bu metin kümesinin bir oda/yapı elemanı olmadığına karar verdiyse (genel proje
                // notu, malzeme şartnamesi, revizyon bilgisi, yön oku vb.) satır hiç eklenmez.
                if (oda.Ilgili)
                    sonuclar.Add(SonucaCevir(oda, kaynak, $"Sayfa {sayfaVerisi.SayfaNo}", disiplin, dosyaAdi));
            }
        }

        return sonuclar;
    }

    private static CizimAnalizSonucu SonucaCevir(OdaYapilandirmaSonucu oda, KaynakTuru kaynak, string varsayilanKat, ProjeDisiplini disiplin, string dosyaAdi)
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
            Disiplin = disiplin,
            KaynakDosya = dosyaAdi
        };
    }

    private static GuvenSkoru GuveneCevir(int guven) =>
        guven >= 80 ? GuvenSkoru.Yuksek : guven >= 50 ? GuvenSkoru.Orta : GuvenSkoru.Dusuk;

    // Tek başına 1-3 haneli bir sayı (nokta/virgül/birim yok) — mimari çizimlerde ölçü zinciri veya
    // kot rakamı olarak neredeyse her yerde bulunur, tek başına asla bir oda etiketinin parçası
    // değildir. "Alan : 49.65 m²" gibi ondalıklı/birim taşıyan sayılar bu deseni EŞLEŞTİRMEZ.
    private static readonly Regex TekBasinaKisaSayiDeseni = new(@"^\d{1,3}$", RegexOptions.Compiled);

    /// <summary>
    /// PDF'ten çıkarılan ham kelime listesini kümelemeye göndermeden önce düzeltir:
    /// (1) Dikey yazılmış (90°/270° döndürülmüş) kelimeler tamamen elenir — gerçek bir mimari
    ///     çizimde bu, neredeyse her zaman ölçü zinciri/detay çiziminin tek haneli rakamlarıdır
    ///     (bu dosyada sayfadaki kelimelerin yarısından fazlası bu türdendi) ve bir oda etiketiyle
    ///     karışırsa X/Y yakınlık sezgisi bile onu ayıklayamaz.
    /// (2) 180° döndürülmüş (baş aşağı) kelimelerde PdfPig bazen karakterleri ters sırayla
    ///     döndürüyor (ör. "YANGIN KAPISI" etiketinin bir parçası "GNAY"/"ISIPAK" olarak geliyor) —
    ///     harfleri ters çevirerek okunabilir hale getirilir (doğrulandı: "ISIPAK"→"KAPISI",
    ///     "AMALPAK"→"KAPLAMA", "022/001"→"100/220").
    /// (3) Yatay ama tek başına kısa bir sayı olan kelimeler (ölçü/kot rakamı) elenir — bunlar
    ///     genelde oda etiketine çok yakın konumlandığından X/Y kümeleme sezgisiyle ayıklanamıyordu.
    /// </summary>
    internal static List<Word> KelimeleriDuzelt(List<Word> kelimeler)
    {
        var sonuc = new List<Word>(kelimeler.Count);
        foreach (var kelime in kelimeler)
        {
            if (kelime.TextOrientation is TextOrientation.Rotate90 or TextOrientation.Rotate270)
                continue;

            if (kelime.TextOrientation == TextOrientation.Rotate180)
            {
                sonuc.Add(new Word(kelime.Letters.Reverse().ToList()));
                continue;
            }

            if (TekBasinaKisaSayiDeseni.IsMatch(kelime.Text.Trim()))
                continue;

            sonuc.Add(kelime);
        }
        return sonuc;
    }

    // Aynı fiziksel satırdaki kelimeler arasındaki yatay boşluk, satır yüksekliğinin bu katından
    // fazla olamaz — aksi halde sayfanın tamamen başka bir yerindeki (aynı Y yüksekliğine denk
    // gelen) alakasız bir kelime aynı "satır"a eklenir (bkz. YatayYakinlikliSatirGrubu notu).
    private const double SatirYatayBoslukKatsayisi = 5.0;

    // İki ardışık satırın aynı kümeye (etikete) ait sayılması için, dikey boşluğun küçük olması
    // yetmez — yatay aralıkları da örtüşmeli/yakın olmalı (bir oda adı ile altındaki "Alan: X m²"
    // satırı genelde aynı X aralığında/soldan hizalıdır). Bu olmadan mimari bir çizimde sayfanın
    // her yerine dağılmış metinler (oda etiketleri, notlar, ölçü zincirleri) sırf dikey boşluk
    // küçük diye tek bir dev kümede birleşiyordu.
    private const double KumeYatayOrtusmeToleransKatsayisi = 1.5;

    // Tipik bir oda etiketi bloğu (ODA ADI / Alan: X m² / DÖŞ:.. / DUVAR:.. / TAVAN:..) en fazla
    // ~5 satırdır. Bazı odaların (ör. SIĞINAK) hemen yanına/altına fiziksel olarak yazılmış uzun
    // NOT: metinleri, X/Y sezgisiyle mükemmel ayrılamayabiliyor — bunun yerine bir kümenin büyümesine
    // burada sert bir tavan konur: tavana ulaşan bir küme artık yeni satır kabul etmez, taşan satırlar
    // ayrı bir kümeye düşer (ve büyük olasılıkla ayrı bir kümede gürültü filtresine takılır/AI
    // tarafından "ilgisiz" işaretlenir) — böylece en azından odanın kendi etiketi kirlenmeden kalır.
    private const int KumeMaksimumSatir = 6;

    /// <summary>
    /// Kelimeleri önce satırlara (aynı yükseklikte VE yatayda birbirine yakın), sonra hem dikey
    /// boşluğu küçük HEM de yatayda örtüşen/yakın komşu satırları tek bir kümeye (ör. "Yatak Odası"
    /// + "alan: 14.2 m²") birleştirir. Kesin bir tablo/etiket yapısı varsayılmaz — bu bir sezgisel
    /// gruplama. Yatay kısıt olmadan (eski sürüm) mimari çizimlerde aynı Y-bandına denk gelen ama
    /// sayfanın tamamen farklı yerlerindeki metinler (ör. sol üstteki bir oda etiketiyle sağ alttaki
    /// bir ölçü zinciri) aynı "satır"a, ardından zincirleme olarak neredeyse tüm sayfa tek bir devasa
    /// kümeye birleşebiliyordu — bu da hem gerçek oda etiketlerinin kayboluşuna (hepsi tek bir
    /// anlamsız metin yığınına gömülüyor) hem de AI'ya anlamsız/dev bir metin gönderilmesine yol açtı.
    /// </summary>
    internal readonly record struct FizikselSatir(string Metin, double Ust, double Alt, double Sol, double Sag, double Yukseklik, bool TersYonlu);

    /// <summary>
    /// Kelimeleri (aynı yükseklikte VE yatayda birbirine yakın olanları) fiziksel satırlara gruplar.
    /// Hem oda-etiketi kümeleme (<see cref="MetinKumeleriOlustur"/>) hem de düz keşif/metraj PDF
    /// satır taraması (<see cref="VeriDeposu.ProjeyePdfImportEt"/>) bu metodu kullanır — ikisi de
    /// PdfPig'in ham <c>Page.Text</c> özelliğini KULLANMAZ, çünkü o metni sayfadaki içerik akışı
    /// sırasına göre birleştirir; mimari bir çizim gibi lineer olmayan bir sayfada bu, sayfanın
    /// tamamen farklı yerlerindeki metinlerin yan yana yapışmasına yol açar.
    /// </summary>
    internal static List<FizikselSatir> FizikselSatirlaraGrupla(List<Word> kelimeler)
    {
        var satirlar = new List<List<Word>>();
        foreach (var kelime in kelimeler.OrderByDescending(k => k.BoundingBox.Top))
        {
            var merkezY = (kelime.BoundingBox.Top + kelime.BoundingBox.Bottom) / 2;
            var satir = satirlar.FirstOrDefault(s =>
            {
                var ortMerkez = s.Average(k => (k.BoundingBox.Top + k.BoundingBox.Bottom) / 2);
                var ortYukseklik = Math.Max(s.Average(k => k.BoundingBox.Height), 1);
                if (Math.Abs(merkezY - ortMerkez) >= ortYukseklik * 0.6) return false;

                var satirSolu = s.Min(k => k.BoundingBox.Left);
                var satirSagi = s.Max(k => k.BoundingBox.Right);
                var yatayBosluk = kelime.BoundingBox.Left > satirSagi ? kelime.BoundingBox.Left - satirSagi
                    : kelime.BoundingBox.Right < satirSolu ? satirSolu - kelime.BoundingBox.Right
                    : 0; // kelime zaten satırın X aralığıyla örtüşüyor
                return yatayBosluk < ortYukseklik * SatirYatayBoslukKatsayisi;
            });
            if (satir != null) satir.Add(kelime);
            else satirlar.Add(new List<Word> { kelime });
        }

        return satirlar
            .Select(s =>
            {
                // 180° döndürülmüş (baş aşağı) bir satırda okuma yönü de ters olduğundan, kelimeler
                // Left'e göre ARTAN sırada dizilirse kelime SIRASI da ters çıkar (ör. "SAYAÇ ODASI"
                // yerine "ODASI SAYAÇ") — KelimeleriDuzelt zaten kelimelerin kendi harflerini
                // düzeltti, burada da (satırın tamamı Rotate180 ise) sıralama yönü tersine çevrilir.
                var tersYonlu = s.All(k => k.TextOrientation == TextOrientation.Rotate180);
                return new FizikselSatir(
                    Metin: string.Join(" ", (tersYonlu ? s.OrderByDescending(k => k.BoundingBox.Left) : s.OrderBy(k => k.BoundingBox.Left)).Select(k => k.Text)),
                    Ust: s.Max(k => k.BoundingBox.Top),
                    Alt: s.Min(k => k.BoundingBox.Bottom),
                    Sol: s.Min(k => k.BoundingBox.Left),
                    Sag: s.Max(k => k.BoundingBox.Right),
                    Yukseklik: Math.Max(s.Average(k => k.BoundingBox.Height), 1),
                    TersYonlu: tersYonlu);
            })
            .OrderByDescending(s => s.Ust)
            .ToList();
    }

    /// <summary>Düz keşif/metraj PDF'lerinde (bkz. VeriDeposu.ProjeyePdfImportEt) satır satır tarama
    /// için kullanılan metin çıkarımı — döndürülmüş metinler düzeltilir, satırlar X/Y konumuna göre
    /// doğru sırayla oluşturulur (ham Page.Text'in aksine).</summary>
    internal static List<string> DuzMetinSatirlariniCikar(Page sayfa)
    {
        var kelimeler = KelimeleriDuzelt(sayfa.GetWords().ToList());
        return kelimeler.Count == 0
            ? new List<string>()
            : FizikselSatirlaraGrupla(kelimeler).Select(s => s.Metin).ToList();
    }

    private static List<(string Metin, double Sol, double Alt, double Sag, double Ust)> MetinKumeleriOlustur(List<Word> kelimeler)
    {
        var satirBilgisi = FizikselSatirlaraGrupla(kelimeler);

        // NOT: sadece "en son oluşturulan kümeye" bakmak (eski sürüm) yan yana duran etiketlerde
        // (ör. solda "MUTFAK", sağda "BANYO" aynı Y bandında) hatalı bölünmeye yol açar: satırlar
        // global Y sırasına göre işlendiği için iki etiketin "alan" alt satırları küresel sırada
        // birbirine karışır (MUTFAK'ın alanı, BANYO satırından hemen sonra gelebilir) — bu durumda
        // "son küme" artık MUTFAK'ın değil BANYO'nun kümesi olur ve MUTFAK'ın alan satırı hiçbir
        // kümeye uyum sağlayamayıp kendi başına ayrı (yanlış) bir kümeye düşer. Bunun yerine her
        // yeni satır için AÇIK OLAN TÜM kümeler arasından (son satırı dikey+yatay olarak uyan)
        // en iyi eşleşen aranır.
        var kumeIndeksleri = new List<List<int>>();
        for (int i = 0; i < satirBilgisi.Count; i++)
        {
            var yeniSatir = satirBilgisi[i];
            List<int>? enIyiKume = null;
            double enKucukDikeyBosluk = double.MaxValue;

            foreach (var aday in kumeIndeksleri)
            {
                if (aday.Count >= KumeMaksimumSatir) continue;

                var oncekiSatir = satirBilgisi[aday[^1]];
                var dikeyBosluk = oncekiSatir.Alt - yeniSatir.Ust;
                if (dikeyBosluk < 0 || dikeyBosluk >= oncekiSatir.Yukseklik * 1.8) continue;

                var yatayToleransi = oncekiSatir.Yukseklik * KumeYatayOrtusmeToleransKatsayisi;
                var yatayOrtusuyorMu = yeniSatir.Sol < oncekiSatir.Sag + yatayToleransi &&
                                        yeniSatir.Sag > oncekiSatir.Sol - yatayToleransi;
                if (!yatayOrtusuyorMu) continue;

                if (dikeyBosluk < enKucukDikeyBosluk)
                {
                    enKucukDikeyBosluk = dikeyBosluk;
                    enIyiKume = aday;
                }
            }

            if (enIyiKume != null) enIyiKume.Add(i);
            else kumeIndeksleri.Add(new List<int> { i });
        }

        return kumeIndeksleri
            .Select(k => k.Select(i => satirBilgisi[i]).ToList())
            .Select(satirlarSecili => (
                // Kümenin tamamı 180° döndürülmüş satırlardan oluşuyorsa (baş aşağı yazılmış bir
                // etiket bloğu — ör. "SAYAÇ ODASI"), dikey okuma yönü de terstir: sayfa koordinatında
                // Y'si en büyük satır aslında etiketin mantıksal EN ALT satırıdır. Satırlar bu
                // yönteme kadar hep Y-azalan (sayfanın üstünden altına) sırayla eklendiğinden, böyle
                // bir kümede satır sırası ters çevrilir.
                Metin: string.Join("\n", (satirlarSecili.All(s => s.TersYonlu)
                        ? Enumerable.Reverse(satirlarSecili)
                        : satirlarSecili)
                    .Select(s => s.Metin)),
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
    /// NOT: ACadSharp kütüphanesi henüz alpha aşamasında. Bu sürüm üç ayrı geometrik
    /// yolu aynı pasoda işler (bir DWG'de hepsi bir arada bulunabilir):
    /// (1) kapalı LwPolyline/Polyline2D → alan (shoelace) + en yakın TEXT/MTEXT eşleştirmesi
    ///     (Mimari/Isıtma/Sıhhi çizimlerde oda/alan sınırları için tipik),
    /// (2) Insert (blok referansı) + Attrib → (BlockName, Katman) bazında adet sayımı
    ///     (Statik'te kolon/kiriş, Elektrik'te priz/anahtar/pano gibi sembolik elemanlar için tipik),
    /// (3) Line + açık polyline → katman bazında toplam uzunluk (sadece Elektrik disiplininde,
    ///     kablo/hat metrajı için).
    /// Hatch entity'lerinin sınır yolları (çizgi/yay/spline karışımı olabilir) ve kolon/donatı
    /// cetveli metin tabloları bu sürümde işlenmiyor — ileride eklenebilir.
    /// </summary>
    public List<CizimAnalizSonucu> DwgAnalizEt(Stream dosyaStream, string dosyaAdi)
    {
        CadDocument dokuman = dosyaAdi.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
            ? DxfReader.Read(dosyaStream)
            : DwgReader.Read(dosyaStream);

        var modelSpace = dokuman.BlockRecords["*Model_Space"];

        var metinler = new List<(string Deger, double X, double Y, string Katman)>();
        var poligonlar = new List<(List<(double X, double Y)> Noktalar, string Katman)>();
        // Statik/Elektrik disiplinlerde kolon/kiriş, priz/anahtar/pano gibi elemanlar kapalı poligon değil,
        // blok referansı (Insert) olarak modellenir — adet bazlı sayım için bkz. Bölüm (blok sayımı).
        var bloklar = new List<(string BlockName, double X, double Y, string Katman, List<(string Etiket, string Deger)> Oznitelikler)>();
        // Elektrik hat/kablo gibi elemanlar Line veya açık (kapalı olmayan) polyline olarak çizilir —
        // katman bazında toplam uzunluk hesabı için bkz. Bölüm (kablo/hat uzunluğu).
        var cizgiler = new List<(double Uzunluk, string Katman)>();

        foreach (var entity in modelSpace.Entities)
        {
            // ACadSharp henüz alpha aşamasında — bazı entity'lerde (özellikle XREF'e bağlı ya da
            // bozuk/eksik referanslı Insert'lerde) iç property getter'ları null referans fırlatabilir.
            // Tek bir bozuk entity yüzünden dosyanın tamamının analizi çökmesin diye, her entity
            // ayrı ayrı korunur; sorunlu olan atlanır, geri kalanı işlenmeye devam eder.
            try
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

                    case LwPolyline lw when !lw.IsClosed && lw.Vertices.Count >= 2:
                        cizgiler.Add((AcikPolilenkUzunluk(lw.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList()), katmanAdi));
                        break;

                    case Polyline2D pl when pl.IsClosed && pl.Vertices.Count >= 3:
                        poligonlar.Add((pl.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList(), katmanAdi));
                        break;

                    case Polyline2D pl when !pl.IsClosed && pl.Vertices.Count >= 2:
                        cizgiler.Add((AcikPolilenkUzunluk(pl.Vertices.Select(v => ((double)v.Location.X, (double)v.Location.Y)).ToList()), katmanAdi));
                        break;

                    case Line line:
                        cizgiler.Add((Mesafe(((double)line.StartPoint.X, (double)line.StartPoint.Y), ((double)line.EndPoint.X, (double)line.EndPoint.Y)), katmanAdi));
                        break;

                    // "*" ile başlayan blok adları AutoCAD'in anonim (kullanıcı tarafından adlandırılmamış)
                    // blokları — ölçülendirme (dimension), tarama (hatch) veya ilişkisel dizi (array)
                    // için otomatik üretilir, gerçek bir sembol/eleman değildir; sayıma dahil edilmez.
                    case Insert insert when !(insert.Block?.Name ?? "").StartsWith('*'):
                        var oznitelikler = (insert.Attributes ?? Enumerable.Empty<AttributeEntity>())
                            .Where(a => !string.IsNullOrWhiteSpace(a.Value) && !CizimGurultuFiltresi.GurultuMu(a.Value))
                            .Select(a => (Etiket: a.Tag ?? "", Deger: a.Value.Trim()))
                            .ToList();
                        bloklar.Add((insert.Block?.Name ?? "(isimsiz blok)", (double)insert.InsertPoint.X, (double)insert.InsertPoint.Y, katmanAdi, oznitelikler));
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DWG entity atlandı ({entity.GetType().Name}, handle {entity.Handle}): {ex.Message}");
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
        // Statik/Elektrik çizimlerde katman sinyali çoğunlukla sadece blok (Insert) ve çizgi (Line)
        // katmanlarında bulunur (poligon/metin hiç olmayabilir), o yüzden onlar da dahil edilir.
        var katmanAdlari = poligonlar.Select(p => p.Katman)
            .Concat(metinler.Select(m => m.Katman))
            .Concat(bloklar.Select(b => b.Katman))
            .Concat(cizgiler.Select(c => c.Katman))
            .Distinct().ToList();
        var disiplin = DisiplinTespitServisi.TespitEt(metinler.Select(m => m.Deger), katmanAdlari);

        var sonuclar = new List<CizimAnalizSonucu>();
        // Statik/Elektrik çizimlerde kapalı bir poligon "oda" değildir — kolon kesiti, pano kutusu,
        // tarama sınırı gibi şeyler de kapalı poligon olarak çizilir. Bu disiplinlerde alan (m²)
        // sonucu üretmek anlamsız/gürültü demektir; adet (blok sayımı) ve uzunluk (kablo/hat) zaten
        // ayrı yollardan üretiliyor. Alan yolu sadece oda/alan kavramının anlamlı olduğu Mimari/
        // Isıtma/Sıhhi'de (ve disiplin tespit edilemediyse, güvenli varsayılan olarak) çalışır.
        bool alanYoluGecerli = disiplin != ProjeDisiplini.Statik && disiplin != ProjeDisiplini.Elektrik;
        if (alanYoluGecerli)
        {
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
                    Disiplin = disiplin,
                    KaynakDosya = dosyaAdi
                });
            }
        }

        // Blok (Insert) sayımı — Statik'te kolon/kiriş, Elektrik'te priz/anahtar/pano gibi elemanlar
        // kapalı poligon değil blok referansı olarak modellenir; (BlockName, Katman) bazında gruplanıp
        // adet olarak deterministik sayılır. Mimari/Isıtma/Sıhhi çizimlerde de bir blok bulunursa
        // (ör. kapı/pencere sembolleri) aynı şekilde sayılır — bu, poligon yoluna ek, onun yerine değil.
        // Aynı (BlockName, Katman) çifti birden fazla öznitelik varyantı taşıyabilir (ör. aynı "KAPI"
        // bloğu aynı katmanda hem 90x210 hem 80x210 kapılar için kullanılmış olabilir) — sadece bunlarla
        // gruplarsak varyantlardan biri rastgele seçilip etikete yazılır, diğerinin kimliği tamamen
        // kaybolur (sadece toplam adet hayatta kalır). Grup anahtarına öznitelik imzasını (sıralı
        // "Etiket=Deger" çiftleri) da katarak her varyant kendi satırında ayrı sayılır.
        var blokGruplari = bloklar.GroupBy(b => (
            b.BlockName,
            b.Katman,
            OznitelikImzasi: string.Join("|", b.Oznitelikler.Select(o => $"{o.Etiket}={o.Deger}").OrderBy(s => s, StringComparer.Ordinal))));
        foreach (var grup in blokGruplari)
        {
            var adet = grup.Count();
            var ornekOznitelik = grup.SelectMany(b => b.Oznitelikler).FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Deger));
            var odaAdi = string.IsNullOrWhiteSpace(ornekOznitelik.Deger)
                ? grup.Key.BlockName
                : $"{grup.Key.BlockName}: {ornekOznitelik.Deger}";

            sonuclar.Add(new CizimAnalizSonucu
            {
                OdaAdi = odaAdi,
                Adet = adet,
                KatAdi = "",
                KaynakTuru = KaynakTuru.BlokSayimi,
                GuvenSkoru = GuvenSkoru.Yuksek,
                OneriGerekcesi = $"Katman \"{grup.Key.Katman}\" üzerindeki blok (Insert) referansları deterministik olarak sayıldı.",
                KullanilanModel = "geometrik (blok sayımı)",
                Disiplin = disiplin,
                KaynakDosya = dosyaAdi
            });
        }

        // Kablo/hat uzunluğu — sadece Elektrik disiplininde anlamlı bir metrajdır (aksi halde çizim
        // içindeki her sıradan Line/açık polyline gereksiz satırlar üretir).
        if (disiplin == ProjeDisiplini.Elektrik)
        {
            var kabloGruplari = cizgiler
                .GroupBy(c => c.Katman)
                .Select(g => (Katman: g.Key, ToplamUzunluk: g.Sum(c => c.Uzunluk)))
                .Where(g => g.ToplamUzunluk > 0.01);

            foreach (var grup in kabloGruplari)
            {
                sonuclar.Add(new CizimAnalizSonucu
                {
                    OdaAdi = $"Kablo/Hat: {grup.Katman}",
                    Uzunluk = Math.Round((decimal)grup.ToplamUzunluk, 2),
                    KatAdi = "",
                    KaynakTuru = KaynakTuru.VektorGeometri,
                    GuvenSkoru = GuvenSkoru.Yuksek,
                    OneriGerekcesi = $"Katman \"{grup.Katman}\" üzerindeki Line/açık polyline segmentlerinin toplam uzunluğu deterministik hesaplandı.",
                    KullanilanModel = "geometrik (çizgi uzunluğu toplamı)",
                    Disiplin = disiplin,
                    KaynakDosya = dosyaAdi
                });
            }
        }

        return sonuclar;
    }

    // Tek bir DWG onlarca satır (kolon/priz adedi, kablo hattı, oda) üretebilir; her satır için AI
    // isteği 2-20+ saniye sürebiliyor. Sırayla (tek tek) çalıştırılırsa toplam süre kolayca 5-15+
    // dakikaya çıkıyor ve UI'da tek bir sabit mesaj olduğundan kullanıcıya "takılı kaldı" gibi
    // görünüyor. Sınırlı paralellikle (aynı anda en fazla bu kadar istek) hem toplam süre kısalır
    // hem de her istek bittiğinde ilerlemeRaporu ile arayüz güncellenebilir.
    private const int AiEsZamanliIstekLimiti = 4;

    /// <summary>Katman adı + geometrik sinyalleri (kapalılık, alan) AI hibrit sınıflandırmaya vererek her odaya bir poz önerisi ekler,
    /// ve AI'nın gerçek bir yapı elemanı olmadığına (çizim süsü — pafta çerçevesi, kuzey oku, ölçek çubuğu, revizyon bulutu,
    /// lejant sembolü vb.) karar verdiği satırları sonuçtan çıkarır. Geometrik tespit (kapalı poligon veya adlandırılmış blok
    /// olması) tek başına gerçek bir elemanın kanıtı değildir — bu metod son sözü yine AI'ya bırakır.
    /// <paramref name="ilerlemeRaporu"/> verilirse her satır tamamlandığında (Tamamlanan, Toplam) raporlanır — UI'da ilerleme göstermek için.
    /// Geriye, ilgisiz olarak işaretlenenler çıkarılmış yeni bir liste döner — <paramref name="odalar"/> mutasyona uğramaz.</summary>
    public async Task<List<CizimAnalizSonucu>> DwgPozOnerileriniEkleAsync(List<CizimAnalizSonucu> odalar, string dosyaAdi, IProgress<(int Tamamlanan, int Toplam)>? ilerlemeRaporu = null)
    {
        int tamamlanan = 0;
        using var esZamanlilikSiniri = new SemaphoreSlim(AiEsZamanliIstekLimiti);

        async Task<bool> TekSatirIsle(CizimAnalizSonucu oda)
        {
            await esZamanlilikSiniri.WaitAsync();
            try
            {
                // Sonuç satırı hangi ölçüm türünden geldiğine göre (alan / adet / uzunluk) farklı bir
                // bağlam metni kurulur — AI'ya her zaman AlanM2 varmış gibi yanlış bağlam verilmesin.
                var olcumSatiri = oda.Adet.HasValue
                    ? $"Adet: {oda.Adet} (blok/sembol referansı sayımıyla deterministik hesaplandı)"
                    : oda.Uzunluk.HasValue
                        ? $"Uzunluk: {oda.Uzunluk} m (çizgi/hat segmentleri toplamıyla deterministik hesaplandı)"
                        : $"Alan: {oda.AlanM2} m2\nGeometri: kapalı poligon (shoelace ile deterministik hesaplandı)";

                // Not: bu satır geometrik olarak tespit edildi (kapalı poligon ya da adlandırılmış blok
                // referansı) ama bu, gerçek bir yapı elemanı olduğunun kanıtı değil — pafta çerçevesi,
                // kuzey oku, ölçek çubuğu, revizyon bulutu, lejant sembolü gibi çizim süsleri de aynı
                // şekilde kapalı poligon veya adlandırılmış blok olarak modellenebilir. Ada/etiket
                // metnine bakarak bunun gerçek bir eleman mı yoksa çizim süsü mü olduğuna karar ver.
                var baglam = $"Kaynak: DWG çizimi ({dosyaAdi})\nBu satır geometrik olarak (kapalı poligon veya blok referansı sayımıyla) tespit edildi — bu, gerçek bir yapı elemanı olduğu anlamına gelmez, pafta çerçevesi/kuzey oku/ölçek çubuğu/revizyon bulutu/lejant sembolü gibi çizim süsleri de aynı şekilde geometrik olarak tespit edilir.\nOda/eleman adı: {oda.OdaAdi}\nKat: {(string.IsNullOrWhiteSpace(oda.KatAdi) ? "belirtilmemiş" : oda.KatAdi)}\n{olcumSatiri}";
                var oneri = await _ai.MetinKumesindenOdaCikarAsync(baglam, _veri.Pozlar, oda.Disiplin);
                if (!oneri.Ilgili)
                    return false;

                oda.OnerilenPozId = oneri.OnerilenPozId;
                oda.OneriGerekcesi = string.IsNullOrWhiteSpace(oneri.Gerekce) ? oda.OneriGerekcesi : oneri.Gerekce;
                oda.KullanilanModel = string.IsNullOrWhiteSpace(oneri.KullanilanModel) ? oda.KullanilanModel : oneri.KullanilanModel;
                return true;
            }
            finally
            {
                esZamanlilikSiniri.Release();
                ilerlemeRaporu?.Report((Interlocked.Increment(ref tamamlanan), odalar.Count));
            }
        }

        var sonuclar = await Task.WhenAll(odalar.Select(async oda => (Oda: oda, Ilgili: await TekSatirIsle(oda))));
        return sonuclar.Where(x => x.Ilgili).Select(x => x.Oda).ToList();
    }

    /// <summary>
    /// MTEXT formatlama kodlarını temizler. Eski regex (\\[A-Za-z](\d+(\.\d+)?)?;?) sadece sayısal
    /// parametreli kodları (\H1.5;, \A1;) temizliyordu — \fFontAdı|b0|i0|c0|p34; gibi metin
    /// parametreli kodlarda (yazı tipi/font değişimi) sadece "\f" kısmını tüketip font adının
    /// kendisini ("Arial", "Calibri" vb.) ham metin olarak bırakıyordu; bu da oda adlarının
    /// içine yazı tipi adlarının sızmasına yol açıyordu. Ayrıca \P (paragraf/satır sonu) da bu
    /// regex tarafından yutulduğu için ondan sonraki .Replace("\\P","\n") hiçbir zaman eşleşmiyor,
    /// satırlar birbirine yapışıyordu. Bu sürüm: (1) \P ve \~ önce (regex'ten önce) çeviriliyor,
    /// (2) parametreli kodlar noktalı virgüle kadar (metin/sayı fark etmeksizin) tek seferde
    /// temizleniyor, (3) parametresiz aç/kapa kodları (\L, \l, \O, \o, \K, \k vb.) da temizleniyor.
    /// </summary>
    private static string MTextTemizle(string mtext)
    {
        var temiz = mtext.Replace("\\P", "\n").Replace("\\~", " ");
        temiz = Regex.Replace(temiz, @"\\[A-Za-z](?:[^;\\{}]*;)?", "");
        temiz = Regex.Replace(temiz, @"[{}]", "");
        return temiz.Trim();
    }

    private static double Mesafe((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

    /// <summary>Açık (kapalı olmayan) bir polyline'ın ardışık köşe noktaları arasındaki mesafelerin toplamı — başa dönüş dahil değil.</summary>
    private static double AcikPolilenkUzunluk(List<(double X, double Y)> noktalar)
    {
        double toplam = 0;
        for (int i = 0; i < noktalar.Count - 1; i++)
            toplam += Mesafe(noktalar[i], noktalar[i + 1]);
        return toplam;
    }

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
