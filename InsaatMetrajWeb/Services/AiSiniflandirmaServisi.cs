using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir PDF/DWG çiziminden çıkarılan metin kümesini veya görsel kırpmayı Claude API
/// ile yapılandırılmış oda bilgisine ({oda adı, alan, kat}) ve önerilen poza çevirir.
///
/// Strateji: önce ucuz/hızlı model (Haiku 4.5) sorulur. Modelin kendi döndürdüğü
/// güven skoru belirlenen eşiğin (GuvenEsigi) altındaysa, aynı soru daha güçlü bir
/// modele (Sonnet 5) tekrar sorulur. Karar HER ZAMAN bir AI modeli tarafından
/// veriliyor — sabit kural/isim eşleştirmesi burada bypass olarak kullanılmıyor,
/// sadece "hangi modelin sorulacağı" maliyet amacıyla kademelendiriliyor.
///
/// KURULUM: appsettings.json'a veya (tercihen) kullanıcı gizli dizinine
/// (dotnet user-secrets) şu anahtarı eklemen gerekiyor:
///   "Anthropic": { "ApiKey": "sk-ant-..." }
/// API anahtarını asla appsettings.json'a commit etme — user-secrets veya
/// ortam değişkeni (ANTHROPIC_API_KEY) kullan. Anahtar tanımlı değilse bu servis
/// no-op döner (Guven=0, PozId=null) — çağıran taraf çizim analizini kullanıcıya
/// elle işaretlenmek üzere sunar, uygulama anahtarsız da çalışır/derlenir.
/// </summary>
public class AiSiniflandirmaServisi
{
    private const string HaikuModel = "claude-haiku-4-5-20251001";
    private const string SonnetModel = "claude-sonnet-5";
    private const int GuvenEsigi = 70; // bu değerin altında Sonnet'e yükseltilir

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public AiSiniflandirmaServisi(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    public bool ApiAnahtariTanimliMi => !string.IsNullOrWhiteSpace(_apiKey);

    private static readonly OdaYapilandirmaSonucu YapilandirilmadiSonucu = new()
    {
        Guven = 0,
        Gerekce = "Anthropic API anahtarı tanımlı değil. appsettings.json'da Anthropic:ApiKey veya ANTHROPIC_API_KEY ortam değişkenini ayarla.",
        KullanilanModel = "yapılandırılmadı"
    };

    /// <summary>Bir metin kümesini (ör. bir PDF sayfasında birbirine yakın "oda adı" + "alan: X m²" satırları) yapılandırılmış oda bilgisine çevirir.</summary>
    public Task<OdaYapilandirmaSonucu> MetinKumesindenOdaCikarAsync(string metinKumesi, List<Poz> pozlar)
        => HibritSiniflandirAsync(pozlar, model => TekModelIleMetinSiniflandirAsync(model, metinKumesi, pozlar));

    /// <summary>Metin katmanı yoksa/yetersizse (taranmış PDF, patlatılmış DWG metni) sayfa/bölge görselini doğrudan yapay zeka görüşüne gönderir.</summary>
    public Task<OdaYapilandirmaSonucu> GorseldenOdaCikarAsync(byte[] pngGorsel, List<Poz> pozlar)
        => HibritSiniflandirAsync(pozlar, model => TekModelIleGorselSiniflandirAsync(model, pngGorsel, pozlar));

    private async Task<OdaYapilandirmaSonucu> HibritSiniflandirAsync(List<Poz> pozlar, Func<string, Task<OdaYapilandirmaSonucu>> tekModelCagir)
    {
        if (!ApiAnahtariTanimliMi)
            return YapilandirilmadiSonucu;

        try
        {
            var haikuSonuc = await tekModelCagir(HaikuModel);

            if (haikuSonuc.Guven >= GuvenEsigi)
            {
                haikuSonuc.KullanilanModel = "Haiku 4.5";
                return haikuSonuc;
            }

            // Haiku'nun güveni düşük -> daha güçlü modele yükselt
            var sonnetSonuc = await tekModelCagir(SonnetModel);
            sonnetSonuc.KullanilanModel = $"Sonnet 5 (Haiku güveni yetersizdi: %{haikuSonuc.Guven})";
            return sonnetSonuc;
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

    private async Task<OdaYapilandirmaSonucu> TekModelIleMetinSiniflandirAsync(string model, string metinKumesi, List<Poz> pozlar)
    {
        var kullaniciMesaji = new object[]
        {
            new { type = "text", text = $"Çizimden çıkarılan metin kümesi:\n{metinKumesi}" }
        };

        return await IstekGonderVeYorumlaAsync(model, SistemPrompt(pozlar), kullaniciMesaji);
    }

    private async Task<OdaYapilandirmaSonucu> TekModelIleGorselSiniflandirAsync(string model, byte[] pngGorsel, List<Poz> pozlar)
    {
        var base64Gorsel = Convert.ToBase64String(pngGorsel);
        var kullaniciMesaji = new object[]
        {
            new { type = "text", text = "Çizimin bu bölgesinin görseli aşağıda. Metin katmanı yoktu veya yetersizdi, bu yüzden görsele bakarak yorumla." },
            new
            {
                type = "image",
                source = new { type = "base64", media_type = "image/png", data = base64Gorsel }
            }
        };

        return await IstekGonderVeYorumlaAsync(model, SistemPrompt(pozlar), kullaniciMesaji);
    }

    private async Task<OdaYapilandirmaSonucu> IstekGonderVeYorumlaAsync(string model, string sistemPrompt, object[] kullaniciIcerik)
    {
        var istekGovdesi = new
        {
            model,
            max_tokens = 400,
            system = sistemPrompt,
            messages = new[] { new { role = "user", content = kullaniciIcerik } }
        };

        using var istek = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        istek.Headers.Add("x-api-key", _apiKey);
        istek.Headers.Add("anthropic-version", "2023-06-01");
        istek.Content = new StringContent(JsonSerializer.Serialize(istekGovdesi), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cevap = await _http.SendAsync(istek, cts.Token);
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

        // Model bazen JSON'un etrafına ekstra metin ekleyebilir — ilk { ile son } arasını al
        var baslangic = metinIcerik.IndexOf('{');
        var bitis = metinIcerik.LastIndexOf('}');
        if (baslangic < 0 || bitis < 0 || bitis <= baslangic)
            return new OdaYapilandirmaSonucu { Guven = 0, Gerekce = "Model geçerli bir JSON döndürmedi: " + metinIcerik };

        var jsonKismi = metinIcerik.Substring(baslangic, bitis - baslangic + 1);
        using var sonucDoc = JsonDocument.Parse(jsonKismi);
        var root = sonucDoc.RootElement;

        string odaAdi = root.TryGetProperty("odaAdi", out var odaAdiEl) ? odaAdiEl.GetString() ?? "" : "";
        decimal? alanM2 = root.TryGetProperty("alanM2", out var alanEl) && alanEl.ValueKind != JsonValueKind.Null
            ? alanEl.GetDecimal() : null;
        string kat = root.TryGetProperty("kat", out var katEl) ? katEl.GetString() ?? "" : "";
        int? pozId = root.TryGetProperty("pozId", out var pozIdEl) && pozIdEl.ValueKind != JsonValueKind.Null
            ? pozIdEl.GetInt32() : null;
        int guven = root.TryGetProperty("guven", out var guvenEl) ? guvenEl.GetInt32() : 0;
        string gerekce = root.TryGetProperty("gerekce", out var gerekceEl) ? gerekceEl.GetString() ?? "" : "";

        return new OdaYapilandirmaSonucu
        {
            OdaAdi = odaAdi,
            AlanM2 = alanM2,
            KatAdi = kat,
            OnerilenPozId = pozId,
            Guven = guven,
            Gerekce = gerekce
        };
    }
}
