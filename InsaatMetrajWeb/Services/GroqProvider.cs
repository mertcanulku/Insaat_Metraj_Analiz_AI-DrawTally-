using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın Groq (OpenAI-uyumlu) implementasyonu.
/// https://api.groq.com/openai/v1/chat/completions kullanır.
///
/// KURULUM: appsettings.json'a veya (tercihen) kullanıcı gizli dizinine
/// (dotnet user-secrets) şu anahtarı eklemen gerekiyor:
///   "Groq": { "ApiKey": "gsk_..." }
/// API anahtarını asla appsettings.json'a commit etme — user-secrets veya
/// ortam değişkeni (GROQ_API_KEY) kullan.
/// </summary>
public class GroqProvider : IAiClassificationProvider
{
    // Görsel (PNG) sınıflandırma da aynı sağlayıcı üzerinden yapıldığından her iki model de
    // vizyon destekli olmalı — Nvidia sağlayıcısındaki gerekçeyle aynı (bkz. NvidiaNimProvider).
    //
    // NOT (2026-09-10, canlı test edildi): meta-llama/llama-4-* modelleri bu hesapta mevcut
    // değil (404 model_not_found — Groq katalogdan kaldırmış olabilir). openai/gpt-oss-* modelleri
    // metin isteklerini "reasoning" alanına yazıp asıl "content" alanını boş bırakıyor (Nvidia'daki
    // reasoning-modeli sorunuyla aynı belirti) — kullanılamaz. qwen/qwen3.8-27b metin isteklerinde
    // temiz JSON döndürüyor ve doğrulandı; görsel (image_url) isteklerinde ise test sırasında
    // sürekli "currently over capacity" hatası verdi (Groq tarafında geçici/model bazlı kapasite
    // kısıtı olabilir — bkz. AiHttpRetryYardimcisi otomatik yeniden dener).
    public string UcuzModel => "qwen/qwen3.8-27b";
    public string GucluModel => "qwen/qwen3.8-27b";

    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public bool ApiAnahtariVarMi => !string.IsNullOrWhiteSpace(_apiKey);

    public GroqProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Groq:ApiKey"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
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

        using var cevap = await AiHttpRetryYardimcisi.GonderYenidenDenemeli(_http, IstekOlustur, TimeSpan.FromSeconds(30));
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
