using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın NVIDIA NIM (OpenAI-uyumlu) implementasyonu.
/// https://integrate.api.nvidia.com/v1/chat/completions kullanır.
///
/// Varsayılan model (meta/llama-3.2-11b-vision-instruct) vizyon destekli olduğundan
/// hem metin hem görsel sınıflandırma OpenAI'nin image_url (base64 data URI) formatıyla
/// aynı endpoint üzerinden yapılır.
///
/// KURULUM: appsettings.json'a veya (tercihen) kullanıcı gizli dizinine
/// (dotnet user-secrets) şu anahtarı eklemen gerekiyor:
///   "Nvidia": { "ApiKey": "nvapi-..." }
/// API anahtarını asla appsettings.json'a commit etme — user-secrets veya
/// ortam değişkeni (NVIDIA_API_KEY) kullan.
/// </summary>
public class NvidiaNimProvider : IAiClassificationProvider
{
    // NIM kataloğundaki model adları — ihtiyaca göre appsettings üzerinden
    // değiştirilebilir hale getirilebilir; şimdilik makul varsayılanlar.
    //
    // NOT (2026-09-09, latency optimizasyonu sırasında test edildi): UcuzModel'i daha hafif/hızlı
    // text-only bir modele ayırmak denendi ama bu hesapta güvenilir çalışan başka bir seçenek
    // bulunamadı — denenenler:
    //   - meta/llama-3.1-8b-instruct, meta/llama-3.3-70b-instruct: NVIDIA tarafından emekliye
    //     ayrılmış (410 Gone).
    //   - ibm/granite-*, microsoft/phi-3.5-moe-instruct, databricks/dbrx-instruct,
    //     nv-mistralai/mistral-nemo-12b-instruct, google/gemma-3-*, qwen2.5-7b-instruct: bu hesapta
    //     entitlement yok (404 "Function not found for account").
    //   - nvidia/nemotron-3.5-lightning-30b-a3b, openai/gpt-oss-20b: entitlement var (200) ama
    //     ağır reasoning modelleri — "SADECE JSON döndür" talimatını yok sayıp uzun bir gizli
    //     "thinking" çıktısı üretiyorlar, max_tokens=2000 + 60s timeout ile bile hiçbir test
    //     isteğinde geçerli JSON'a ulaşamadılar (ya timeout ya da JSON'suz düşünce metni).
    // Sonuç: UcuzModel==GucluModel bilinçli bir seçim — AiSiniflandirmaServisi.HibritSiniflandirAsync
    // bu durumu tespit edip düşük güvende gereksiz ikinci (aynı model, aynı girdi) çağrıyı atlıyor,
    // asıl gecikme kazancı ise CizimAnalizServisi.PdfAnalizEt'in artık kümeleri sınırlı paralellikle
    // işlemesinden geliyor (bkz. AiEsZamanliIstekLimiti).
    public string UcuzModel => "meta/llama-3.2-11b-vision-instruct";
    public string GucluModel => "meta/llama-3.2-11b-vision-instruct";

    private const string Endpoint = "https://integrate.api.nvidia.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public bool ApiAnahtariVarMi => !string.IsNullOrWhiteSpace(_apiKey);

    public NvidiaNimProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Nvidia:ApiKey"] ?? Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
    }

    public Task<OdaYapilandirmaSonucu> MetinSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni)
    {
        object kullaniciIcerik = kullaniciMetni;
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, kullaniciIcerik);
    }

    public Task<OdaYapilandirmaSonucu> GorselSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni, byte[] pngGorsel)
    {
        var base64Gorsel = Convert.ToBase64String(pngGorsel);
        object kullaniciIcerik = new object[]
        {
            new { type = "text", text = kullaniciMetni },
            new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Gorsel}" } }
        };
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, kullaniciIcerik);
    }

    private async Task<OdaYapilandirmaSonucu> IstekGonderVeYorumlaAsync(string model, string sistemPrompt, object kullaniciIcerik)
    {
        var istekGovdesi = new
        {
            model,
            max_tokens = 400,
            messages = new object[]
            {
                new { role = "system", content = sistemPrompt },
                new { role = "user", content = kullaniciIcerik }
            }
        };

        var govdeJson = JsonSerializer.Serialize(istekGovdesi);
        HttpRequestMessage IstekOlustur()
        {
            var istek = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            istek.Content = new StringContent(govdeJson, Encoding.UTF8, "application/json");
            return istek;
        }

        // 429 (Too Many Requests) / 503 gibi geçici hatalar, sınırlı paralellikle de olsa aynı anda
        // birçok satırın işlendiği DWG akışında sağlayıcı limiti aşılınca sıkça görülüyordu — bkz.
        // AiHttpRetryYardimcisi. Burada otomatik yeniden denenir, kalıcı bir hata değilse kullanıcıya
        // ham "API 429 döndü" hatası olarak sızmaz.
        using var cevap = await AiHttpRetryYardimcisi.GonderYenidenDenemeli(_http, IstekOlustur, TimeSpan.FromSeconds(30));
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
