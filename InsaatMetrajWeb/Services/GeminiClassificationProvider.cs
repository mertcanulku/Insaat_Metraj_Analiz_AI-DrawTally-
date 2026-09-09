using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın Google Gemini implementasyonu. responseSchema ile JSON çıktısını
/// şema düzeyinde zorunlu kılar — NIM'de yaşanan "model JSON yerine serbest metin döndürüyor"
/// sorununu ortadan kaldırır (bkz. NvidiaNimProvider.cs üstündeki not).
///
/// KURULUM: appsettings.json'a veya (tercihen) kullanıcı gizli dizinine
/// (dotnet user-secrets) şu anahtarı eklemen gerekiyor:
///   "Gemini": { "ApiKey": "AIza..." }
/// API anahtarını asla appsettings.json'a commit etme — user-secrets veya
/// ortam değişkeni (GEMINI_API_KEY) kullan.
/// </summary>
public class GeminiClassificationProvider : IAiClassificationProvider
{
    public string UcuzModel => "gemini-flash-lite-latest";
    public string GucluModel => "gemini-flash-latest";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public bool ApiAnahtariVarMi => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiClassificationProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }

    // OdaYapilandirmaSonucu ile birebir eşleşen JSON şeması — Gemini bu şemaya uymayan bir
    // yanıt üretemez, bkz. AiSiniflandirmaYaniti.MetindenAyristir'in beklediği alanlar.
    private static readonly object YanitSemasi = new
    {
        type = "OBJECT",
        properties = new
        {
            ilgili = new { type = "BOOLEAN" },
            odaAdi = new { type = "STRING" },
            alanM2 = new { type = "NUMBER", nullable = true },
            kat = new { type = "STRING" },
            pozId = new { type = "INTEGER", nullable = true },
            guven = new { type = "INTEGER" },
            gerekce = new { type = "STRING" }
        },
        required = new[] { "ilgili", "odaAdi", "kat", "guven", "gerekce" }
    };

    public Task<OdaYapilandirmaSonucu> MetinSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni)
    {
        var parcalar = new object[] { new { text = kullaniciMetni } };
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, parcalar);
    }

    public Task<OdaYapilandirmaSonucu> GorselSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni, byte[] pngGorsel)
    {
        var base64Gorsel = Convert.ToBase64String(pngGorsel);
        var parcalar = new object[]
        {
            new { text = kullaniciMetni },
            new { inline_data = new { mime_type = "image/png", data = base64Gorsel } }
        };
        return IstekGonderVeYorumlaAsync(model, sistemPrompt, parcalar);
    }

    private async Task<OdaYapilandirmaSonucu> IstekGonderVeYorumlaAsync(string model, string sistemPrompt, object[] parcalar)
    {
        var istekGovdesi = new
        {
            system_instruction = new { parts = new[] { new { text = sistemPrompt } } },
            contents = new[] { new { role = "user", parts = parcalar } },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = YanitSemasi
            }
        };

        var govdeJson = JsonSerializer.Serialize(istekGovdesi);
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
        HttpRequestMessage IstekOlustur()
        {
            var istek = new HttpRequestMessage(HttpMethod.Post, endpoint);
            istek.Content = new StringContent(govdeJson, Encoding.UTF8, "application/json");
            return istek;
        }

        // Gemini ücretsiz katmanı NIM/Anthropic'ten farklı: ani patlamaya değil, dakika başına SABİT
        // bir istek tavanına (model başına 15 RPM) tabi. AiHttpRetryYardimcisi'nin üstel geri
        // çekilmesi (~15sn) bunun için yetersiz kalıyor — Google "18sn sonra dene" diyor ve bunu
        // Retry-After header'ında değil yalnızca hata gövdesindeki "retryDelay" alanında bildiriyor.
        // Bu yüzden isteği göndermeden ÖNCE model başına kayan pencereyle kotaya göre yavaşlatıyoruz.
        await KotaBekleyiciler.GetOrAdd(model, _ => new KotaBekleyici(maksimumIstek: 14, pencere: TimeSpan.FromSeconds(60)))
            .BekleVeKaydetAsync();

        using var cevap = await AiHttpRetryYardimcisi.GonderYenidenDenemeli(_http, IstekOlustur, TimeSpan.FromSeconds(30));
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }

    // Model başına ayrı kova: Google'ın kotası "GenerateRequestsPerMinutePerProjectPerModel" olduğu
    // için ucuz (flash-lite) ve güçlü (flash) model birbirinden bağımsız 15 RPM hakkına sahip.
    private static readonly ConcurrentDictionary<string, KotaBekleyici> KotaBekleyiciler = new();

    /// <summary>Model başına kayan 60 saniyelik pencerede en fazla <see cref="_maksimumIstek"/> isteğe
    /// izin veren basit bir hız sınırlayıcı. Eşzamanlı çağrılar (bkz. AiEsZamanliIstekLimiti) burada
    /// kilitlenerek sıraya girer; kota dolmuşsa pencerenin açılmasını bekler.</summary>
    private sealed class KotaBekleyici(int maksimumIstek, TimeSpan pencere)
    {
        private readonly Queue<DateTime> _istekZamanlari = new();
        private readonly SemaphoreSlim _kilit = new(1, 1);

        public async Task BekleVeKaydetAsync()
        {
            await _kilit.WaitAsync();
            try
            {
                while (true)
                {
                    var simdi = DateTime.UtcNow;
                    while (_istekZamanlari.Count > 0 && simdi - _istekZamanlari.Peek() >= pencere)
                        _istekZamanlari.Dequeue();

                    if (_istekZamanlari.Count < maksimumIstek)
                    {
                        _istekZamanlari.Enqueue(simdi);
                        return;
                    }

                    var bekleme = pencere - (simdi - _istekZamanlari.Peek());
                    if (bekleme > TimeSpan.Zero)
                        await Task.Delay(bekleme);
                }
            }
            finally
            {
                _kilit.Release();
            }
        }
    }
}
