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
            system = sistemPrompt,
            messages = new[] { new { role = "user", content = kullaniciMesaji } }
        };

        using var istek = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        istek.Headers.Add("x-api-key", _apiKey);
        istek.Headers.Add("anthropic-version", "2023-06-01");
        istek.Content = new StringContent(JsonSerializer.Serialize(istekGovdesi), Encoding.UTF8, "application/json");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cevap = await _http.SendAsync(istek, cts.Token);
        var cevapMetni = await cevap.Content.ReadAsStringAsync();

        if (!cevap.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)cevap.StatusCode} döndü: {cevapMetni}");

        using var doc = JsonDocument.Parse(cevapMetni);
        var metinIcerik = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

        return AiSiniflandirmaYaniti.MetindenAyristir(metinIcerik);
    }
}
