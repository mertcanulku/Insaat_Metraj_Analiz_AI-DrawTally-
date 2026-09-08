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
