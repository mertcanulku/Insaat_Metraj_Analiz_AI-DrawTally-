using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// IAiClassificationProvider'ın NVIDIA NIM (OpenAI-uyumlu) implementasyonu.
/// https://integrate.api.nvidia.com/v1/chat/completions kullanır.
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

    public async Task<AiKatmanSinifi> SiniflandirAsync(string model, CizimKatmanSinyali katman, List<Poz> pozlar)
    {
        var pozListesi = string.Join("\n", pozlar.Select(p => $"- id:{p.Id} kod:{p.PozKodu} ad:\"{p.Ad}\" birim:{p.Birim}"));

        var sistemPrompt = $$"""
            Sen bir inşaat metraj uzmanısın. Sana bir CAD çizimindeki bir katmanın
            (layer) adı ve geometrik özellikleri verilecek. Görevin bu katmanın
            aşağıdaki poz listesinden hangisine karşılık geldiğini tahmin etmek.
            Katman ismi güvenilmez olabilir (kısaltma, yabancı dil, anlamsız kod) —
            asıl karar verici geometrik özellikler olmalı: kapalı bir poligon ve
            kareye yakın şekil genelde döşeme/temel betonunu, kapalı olmayan ve
            10-40cm kalınlığında bir hat genelde duvarı işaret eder.

            Poz listesi:
            {{pozListesi}}

            SADECE şu JSON formatında cevap ver, başka hiçbir açıklama ekleme:
            {"pozId": <uygun poz id'si veya null>, "guven": <0-100 arası tam sayı>, "gerekce": "<tek cümlelik kısa gerekçe>"}
            """;

        var kullaniciMesaji = $"""
            Katman adı: "{katman.LayerAdi}"
            Entity sayısı: {katman.EntitySayisi}
            Kapalı poligon mu: {(katman.KapaliMi ? "evet" : "hayır")}
            Ortalama segment uzunluğu: {katman.OrtSegmentUzunlugu:0.0} m
            Çizgi kalınlığı: {katman.CizgiKalinligi:0.00} m
            En/boy oranı: {katman.EnBoyOrani:0.0}
            Toplam uzunluk: {katman.ToplamUzunluk:0.0} m
            Toplam alan: {katman.ToplamAlan:0.0} m2
            """;

        var istekGovdesi = new
        {
            model,
            max_tokens = 300,
            messages = new[]
            {
                new { role = "system", content = sistemPrompt },
                new { role = "user", content = kullaniciMesaji }
            }
        };

        using var istek = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        istek.Content = new StringContent(JsonSerializer.Serialize(istekGovdesi), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cevap = await _http.SendAsync(istek, cts.Token);
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
