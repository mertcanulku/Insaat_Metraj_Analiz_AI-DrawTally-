using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın NVIDIA NIM (OpenAI-uyumlu) implementasyonu.
/// https://integrate.api.nvidia.com/v1/chat/completions kullanır.
///
/// NOT: Varsayılan modeller (Llama 3.1 Instruct) metin-only'dir — görsel sınıflandırma
/// desteklenmiyor, bkz. GorselSiniflandirAsync. Vizyon desteği gerekiyorsa appsettings
/// üzerinden vizyon yeteneği olan bir NIM modeline geçirilebilir.
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
    public string UcuzModel => "meta/llama-3.1-8b-instruct";
    public string GucluModel => "meta/llama-3.1-70b-instruct";

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
        => IstekGonderVeYorumlaAsync(model, sistemPrompt, kullaniciMetni);

    public Task<OdaYapilandirmaSonucu> GorselSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni, byte[] pngGorsel)
        => throw new NotSupportedException(
            $"NVIDIA NIM sağlayıcısının varsayılan modeli ({model}) görsel girdiyi desteklemiyor — vizyon yeteneği olan bir modele geçmeden görselden oda çıkarımı yapılamaz.");

    private async Task<OdaYapilandirmaSonucu> IstekGonderVeYorumlaAsync(string model, string sistemPrompt, string kullaniciMetni)
    {
        var istekGovdesi = new
        {
            model,
            max_tokens = 400,
            messages = new[]
            {
                new { role = "system", content = sistemPrompt },
                new { role = "user", content = kullaniciMetni }
            }
        };

        using var istek = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        istek.Content = new StringContent(JsonSerializer.Serialize(istekGovdesi), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cevap = await _http.SendAsync(istek, cts.Token);
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
