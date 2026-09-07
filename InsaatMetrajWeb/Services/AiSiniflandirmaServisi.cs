using System.Text;
using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir CAD katmanının hangi poza karşılık geldiğini Claude API ile sınıflandırır.
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
/// ortam değişkeni (ANTHROPIC_API_KEY) kullan.
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

    public async Task<AiKatmanSinifi> SiniflandirAsync(CizimKatmanSinyali katman, List<Poz> pozlar)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return new AiKatmanSinifi
            {
                PozId = null,
                Guven = 0,
                Gerekce = "Anthropic API anahtarı tanımlı değil. appsettings.json'da Anthropic:ApiKey veya ANTHROPIC_API_KEY ortam değişkenini ayarla.",
                KullanilanModel = "yapılandırılmadı"
            };
        }

        try
        {
            var haikuSonuc = await TekModelIleSiniflandirAsync(HaikuModel, katman, pozlar);

            if (haikuSonuc.Guven >= GuvenEsigi)
            {
                haikuSonuc.KullanilanModel = "Haiku 4.5";
                return haikuSonuc;
            }

            // Haiku'nun güveni düşük -> daha güçlü modele yükselt
            var sonnetSonuc = await TekModelIleSiniflandirAsync(SonnetModel, katman, pozlar);
            sonnetSonuc.KullanilanModel = $"Sonnet 5 (Haiku güveni yetersizdi: %{haikuSonuc.Guven})";
            return sonnetSonuc;
        }
        catch (Exception ex)
        {
            // Ağ hatası, zaman aşımı, beklenmeyen API yanıtı vb. — sessizce
            // yanlış bir şey eklemek yerine "sınıflandırılamadı" olarak raporla,
            // kullanıcı elle karar versin.
            return new AiKatmanSinifi
            {
                PozId = null,
                Guven = 0,
                Gerekce = $"AI sınıflandırma sırasında hata oluştu: {ex.Message}",
                KullanilanModel = "hata"
            };
        }
    }

    private async Task<AiKatmanSinifi> TekModelIleSiniflandirAsync(string model, CizimKatmanSinyali katman, List<Poz> pozlar)
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

        // Model bazen JSON'un etrafına ekstra metin ekleyebilir — ilk { ile son } arasını al
        var baslangic = metinIcerik.IndexOf('{');
        var bitis = metinIcerik.LastIndexOf('}');
        if (baslangic < 0 || bitis < 0 || bitis <= baslangic)
            return new AiKatmanSinifi { PozId = null, Guven = 0, Gerekce = "Model geçerli bir JSON döndürmedi: " + metinIcerik };

        var jsonKismi = metinIcerik.Substring(baslangic, bitis - baslangic + 1);
        using var sonucDoc = JsonDocument.Parse(jsonKismi);
        var root = sonucDoc.RootElement;

        int? pozId = root.TryGetProperty("pozId", out var pozIdEl) && pozIdEl.ValueKind != JsonValueKind.Null
            ? pozIdEl.GetInt32() : null;
        int guven = root.TryGetProperty("guven", out var guvenEl) ? guvenEl.GetInt32() : 0;
        string gerekce = root.TryGetProperty("gerekce", out var gerekceEl) ? gerekceEl.GetString() ?? "" : "";

        return new AiKatmanSinifi { PozId = pozId, Guven = guven, Gerekce = gerekce };
    }
}
