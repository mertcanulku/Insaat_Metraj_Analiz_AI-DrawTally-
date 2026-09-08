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

    /// <summary>Bir metin kümesini (ör. bir PDF sayfasında birbirine yakın "oda adı" + "alan: X m²" satırları) yapılandırılmış oda bilgisine çevirir.</summary>
    public Task<OdaYapilandirmaSonucu> MetinKumesindenOdaCikarAsync(string metinKumesi, List<Poz> pozlar)
        => HibritSiniflandirAsync(pozlar, model => _saglayici.MetinSiniflandirAsync(
            model, SistemPrompt(pozlar), $"Çizimden çıkarılan metin kümesi:\n{metinKumesi}"));

    /// <summary>Metin katmanı yoksa/yetersizse (taranmış PDF, patlatılmış DWG metni) sayfa/bölge görselini doğrudan yapay zeka görüşüne gönderir.</summary>
    public Task<OdaYapilandirmaSonucu> GorseldenOdaCikarAsync(byte[] pngGorsel, List<Poz> pozlar)
        => HibritSiniflandirAsync(pozlar, model => _saglayici.GorselSiniflandirAsync(
            model, SistemPrompt(pozlar), "Çizimin bu bölgesinin görseli aşağıda. Metin katmanı yoktu veya yetersizdi, bu yüzden görsele bakarak yorumla.", pngGorsel));

    private async Task<OdaYapilandirmaSonucu> HibritSiniflandirAsync(List<Poz> pozlar, Func<string, Task<OdaYapilandirmaSonucu>> tekModelCagir)
    {
        if (!ApiAnahtariTanimliMi)
            return YapilandirilmadiSonucu;

        try
        {
            var ucuzSonuc = await tekModelCagir(_saglayici.UcuzModel);

            if (ucuzSonuc.Guven >= GuvenEsigi)
            {
                ucuzSonuc.KullanilanModel = _saglayici.UcuzModel;
                return ucuzSonuc;
            }

            // Ucuz modelin güveni düşük -> daha güçlü modele yükselt
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

    private static string SistemPrompt(List<Poz> pozlar)
    {
        var pozListesi = string.Join("\n", pozlar.Select(p => $"- id:{p.Id} kod:{p.PozKodu} ad:\"{p.Ad}\" birim:{p.Birim}"));

        return $$"""
            Sen bir inşaat metraj uzmanısın. Sana bir mimari çizimden (PDF veya DWG)
            alınmış bir oda etiketi/metin kümesi ya da o bölgenin görseli verilecek.
            Görevin: oda adını, alanını (m²) ve varsa kat/seviye adını çıkarmak, ayrıca
            aşağıdaki poz listesinden bu odaya en uygun kalemi (ör. döşeme kaplaması)
            önermek.

            Poz listesi:
            {{pozListesi}}

            SADECE şu JSON formatında cevap ver, başka hiçbir açıklama ekleme:
            {"odaAdi": "<oda adı>", "alanM2": <sayı veya null>, "kat": "<kat adı veya \"\">", "pozId": <uygun poz id'si veya null>, "guven": <0-100 arası tam sayı>, "gerekce": "<tek cümlelik kısa gerekçe>"}
            """;
    }
}
