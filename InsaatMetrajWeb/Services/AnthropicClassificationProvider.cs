using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın Anthropic (Claude) implementasyonu.
///
/// KURULUM: appsettings.json'a veya (tercihen) kullanıcı gizli dizinine
/// (dotnet user-secrets) şu anahtarı eklemen gerekiyor:
///   "Anthropic": { "ApiKey": "sk-ant-..." }
/// API anahtarını asla appsettings.json'a commit etme — user-secrets veya
/// ortam değişkeni (ANTHROPIC_API_KEY) kullan.
/// </summary>
public class AnthropicClassificationProvider : IAiClassificationProvider
{
    public string UcuzModel => "claude-haiku-4-5-20251001";
    public string GucluModel => "claude-sonnet-5";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public bool ApiAnahtariVarMi => !string.IsNullOrWhiteSpace(_apiKey);

    public AnthropicClassificationProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    public Task<OdaYapilandirmaSonucu> MetinSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni)
    {
        var kullaniciIcerik = new object[]
        {
            new { type = "text", text = kullaniciMetni }
        };
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, kullaniciIcerik);
    }

    public Task<OdaYapilandirmaSonucu> GorselSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni, byte[] pngGorsel)
    {
        var base64Gorsel = Convert.ToBase64String(pngGorsel);
        var kullaniciIcerik = new object[]
        {
            new { type = "text", text = kullaniciMetni },
            new
            {
                type = "image",
                source = new { type = "base64", media_type = "image/png", data = base64Gorsel }
            }
        };
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, kullaniciIcerik);
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

        var govdeJson = JsonSerializer.Serialize(istekGovdesi);
        HttpRequestMessage IstekOlustur()
        {
            var istek = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            istek.Headers.Add("x-api-key", _apiKey);
            istek.Headers.Add("anthropic-version", "2023-06-01");
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
        var metinIcerik = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
