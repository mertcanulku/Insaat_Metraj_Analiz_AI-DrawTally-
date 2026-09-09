using System.Text.RegularExpressions;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir PDF/DWG çiziminden çıkarılan metin kümesini veya görsel kırpmayı bir AI
/// sağlayıcısı (IAiClassificationProvider) üzerinden yapılandırılmış oda bilgisine
/// ({oda adı, alan, kat}) ve önerilen poza çevirir.
///
/// Strateji: önce sağlayıcının ucuz/hızlı modeli sorulur. Modelin kendi döndürdüğü
/// güven skoru belirlenen eşiğin (GuvenEsigi) altındaysa, aynı soru daha güçlü bir
/// modele tekrar sorulur. Karar HER ZAMAN bir AI modeli tarafından
/// veriliyor — sabit kural/isim eşleştirmesi burada bypass olarak kullanılmıyor,
/// sadece "hangi modelin sorulacağı" maliyet amacıyla kademelendiriliyor.
///
/// Hangi sağlayıcının (Anthropic/Nvidia) kullanılacağı appsettings.json'daki
/// "AiProvider" ayarına göre DI kaydında belirlenir; bu sınıf sağlayıcıdan bağımsızdır.
/// API anahtarı tanımlı değilse bu servis no-op döner (Guven=0, PozId=null) — çağıran
/// taraf çizim analizini kullanıcıya elle işaretlenmek üzere sunar, uygulama anahtarsız
/// da çalışır/derlenir.
/// </summary>
public class AiSiniflandirmaServisi
{
    private const int GuvenEsigi = 70; // bu değerin altında güçlü modele yükseltilir

    private readonly IAiClassificationProvider _saglayici;

    public AiSiniflandirmaServisi(IAiClassificationProvider saglayici)
    {
        _saglayici = saglayici;
    }

    public bool ApiAnahtariTanimliMi => _saglayici.ApiAnahtariVarMi;

    private static readonly OdaYapilandirmaSonucu YapilandirilmadiSonucu = new()
    {
        Guven = 0,
        Gerekce = "AI sağlayıcısının API anahtarı tanımlı değil. appsettings.json veya ortam değişkenini ayarla.",
        KullanilanModel = "yapılandırılmadı"
    };

    /// <summary>Bir metin kümesini (ör. bir PDF sayfasında birbirine yakın "oda adı" + "alan: X m²" satırları) yapılandırılmış oda bilgisine çevirir.
    /// <paramref name="disiplin"/> tespit edilebildiyse (DisiplinTespitServisi), AI'ya hangi disipline
    /// öncelik vermesi gerektiği bağlam olarak verilir — poz listesini daraltmaz, sadece yönlendirir.</summary>
    public Task<OdaYapilandirmaSonucu> MetinKumesindenOdaCikarAsync(string metinKumesi, List<Poz> pozlar, ProjeDisiplini disiplin = ProjeDisiplini.Bilinmiyor)
    {
        var adaylar = DaraltilmisPozlar(pozlar, metinKumesi);
        return HibritSiniflandirAsync(adaylar, model => _saglayici.MetinSiniflandirAsync(
            model, SistemPrompt(adaylar, disiplin), $"Çizimden çıkarılan metin kümesi:\n{metinKumesi}"));
    }

    /// <summary>Metin katmanı yoksa/yetersizse (taranmış PDF, patlatılmış DWG metni) sayfa/bölge görselini doğrudan yapay zeka görüşüne gönderir.</summary>
    public Task<OdaYapilandirmaSonucu> GorseldenOdaCikarAsync(byte[] pngGorsel, List<Poz> pozlar, ProjeDisiplini disiplin = ProjeDisiplini.Bilinmiyor)
    {
        // Görsel modda daraltma için kullanılabilecek bir sorgu metni yok; poz kütüphanesi
        // binlerce kalem içerebileceğinden (bkz. DaraltilmisPozlar), en azından mantıksız
        // derecede büyük bir liste gönderilmesin diye ilk AdayPozLimiti kadarıyla sınırlanır.
        var adaylar = pozlar.Count > AdayPozLimiti ? pozlar.Take(AdayPozLimiti).ToList() : pozlar;
        return HibritSiniflandirAsync(adaylar, model => _saglayici.GorselSiniflandirAsync(
            model, SistemPrompt(adaylar, disiplin), "Çizimin bu bölgesinin görseli aşağıda. Metin katmanı yoktu veya yetersizdi, bu yüzden görsele bakarak yorumla.", pngGorsel));
    }

    private const int AdayPozLimiti = 60;

    private static readonly HashSet<string> DurakKelimeler = new()
    {
        "oda", "alan", "kat", "metre", "kare", "adet", "olan", "icin", "için", "ile", "her", "bir", "ve", "veya"
    };

    /// <summary>
    /// Poz kütüphanesi binlerce kalem içerdiğinden (bkz. PozKutuphanesi), her sınıflandırma
    /// isteğinde TÜM listeyi yapay zekaya göndermek hem maliyetli hem de aday sayısı arttıkça
    /// modelin doğru pozu seçme isabetini düşürür. Bunun yerine sorgu metnindeki anlamlı
    /// kelimelerle poz adı arasında basit bir kelime-örtüşme skoru hesaplanıp en iyi eşleşen
    /// AdayPozLimiti kadarı adaylık listesine alınır — nihai kararı yine AI verir, bu sadece
    /// adayları daraltan bir ön filtre (kural tabanlı bir bypass değildir).
    /// </summary>
    private static List<Poz> DaraltilmisPozlar(List<Poz> pozlar, string sorguMetni)
    {
        if (pozlar.Count <= AdayPozLimiti) return pozlar;

        var kelimeler = Regex.Matches(sorguMetni.ToLowerInvariant(), @"[a-zçğıöşü]{3,}")
            .Select(m => m.Value)
            .Where(k => !DurakKelimeler.Contains(k))
            .Distinct()
            .ToList();

        if (kelimeler.Count == 0) return pozlar.Take(AdayPozLimiti).ToList();

        var eslesenler = pozlar
            .Select(p => (Poz: p, Puan: kelimeler.Count(k => p.Ad.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Puan > 0)
            .OrderByDescending(x => x.Puan)
            .Take(AdayPozLimiti)
            .Select(x => x.Poz)
            .ToList();

        return eslesenler.Count > 0 ? eslesenler : pozlar.Take(AdayPozLimiti).ToList();
    }

    private async Task<OdaYapilandirmaSonucu> HibritSiniflandirAsync(List<Poz> pozlar, Func<string, Task<OdaYapilandirmaSonucu>> tekModelCagir)
    {
        if (!ApiAnahtariTanimliMi)
            return YapilandirilmadiSonucu;

        try
        {
            var ucuzSonuc = await tekModelCagir(_saglayici.UcuzModel);
            ucuzSonuc.KullanilanModel = _saglayici.UcuzModel;

            // UcuzModel ve GucluModel aynı sağlayıcıda aynı modele işaret ediyorsa (ör. şu an
            // Nvidia'da güvenilir/hızlı çalışan tek text-only model meta/llama-3.2-11b-vision-instruct
            // olduğu için ikisi de ona ayarlı — bkz. NvidiaNimProvider), aynı modele aynı girdiyle
            // ikinci bir istek atmak sadece gecikmeyi ikiye katlar, anlamlı farklı bir cevap gelmez.
            // Bu durumda yükseltmeyi tamamen atla.
            if (ucuzSonuc.Guven >= GuvenEsigi || _saglayici.GucluModel == _saglayici.UcuzModel)
                return ucuzSonuc;

            // Ucuz modelin güveni düşük ve güçlü model gerçekten farklı -> yükselt
            var gucluSonuc = await tekModelCagir(_saglayici.GucluModel);
            gucluSonuc.KullanilanModel = $"{_saglayici.GucluModel} (ucuz model güveni yetersizdi: %{ucuzSonuc.Guven})";
            return gucluSonuc;
        }
        catch (Exception ex)
        {
            // Ağ hatası, zaman aşımı, beklenmeyen API yanıtı vb. — sessizce
            // yanlış bir şey eklemek yerine "sınıflandırılamadı" olarak raporla,
            // kullanıcı elle karar versin.
            return new OdaYapilandirmaSonucu
            {
                Guven = 0,
                Gerekce = $"AI sınıflandırma sırasında hata oluştu: {ex.Message}",
                KullanilanModel = "hata"
            };
        }
    }

    private static string SistemPrompt(List<Poz> pozlar, ProjeDisiplini disiplin)
    {
        var pozListesi = string.Join("\n", pozlar.Select(p => $"- id:{p.Id} kod:{p.PozKodu} ad:\"{p.Ad}\" birim:{p.Birim}"));

        var disiplinBaglami = disiplin == ProjeDisiplini.Bilinmiyor
            ? ""
            : $"\nBu proje bir {DisiplinTespitServisi.GosterimAdi(disiplin)} projesidir — poz seçerken öncelikle bu disipline uygun kalemleri değerlendir.\n";

        return $$"""
            Sen bir inşaat metraj uzmanısın. Sana bir mimari çizimden (PDF veya DWG)
            alınmış bir oda etiketi/metin kümesi ya da o bölgenin görseli verilecek.

            ÖNCE karar vermen gereken şey: bu girdi gerçekten bir oda/mekan ya da somut
            bir yapı elemanı (kolon, kiriş, priz, anahtar, pano, kapı, pencere, kablo/hat vb.)
            mı, yoksa metraja konu olmayan başka bir şey mi (genel proje notu, malzeme
            şartnamesi/teknik açıklama, revizyon/onay bilgisi, yön oku veya ölçek/kuzey
            oku etiketi, pafta/antet/lejant metni, ölçü/kot zinciri, tarih, imza/kaşe alanı
            vb.)? İkinci durumda bunu tahmin etmeye ÇALIŞMA — "ilgili" alanını false yap
            ve odaAdi/alanM2/pozId için uydurma bir değer üretme (boş/null bırakabilirsin).
            Sadece ilk durumda (gerçek bir oda/eleman olduğuna karar verdiysen) oda adını,
            alanını (m²) ve varsa kat/seviye adını çıkar, ayrıca aşağıdaki poz listesinden
            bu odaya/elemana en uygun kalemi (ör. döşeme kaplaması) öner.
            {{disiplinBaglami}}
            Poz listesi:
            {{pozListesi}}

            SADECE şu JSON formatında cevap ver, başka hiçbir açıklama ekleme:
            {"ilgili": <true veya false>, "odaAdi": "<oda adı>", "alanM2": <sayı veya null>, "kat": "<kat adı veya \"\">", "pozId": <uygun poz id'si veya null>, "guven": <0-100 arası tam sayı>, "gerekce": "<tek cümlelik kısa gerekçe>"}
            """;
    }
}
