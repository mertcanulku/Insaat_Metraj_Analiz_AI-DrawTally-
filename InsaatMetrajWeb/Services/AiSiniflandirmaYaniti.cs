using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>Model çıktısındaki serbest metinden AiKatmanSinifi JSON'unu ayrıştırır.</summary>
internal static class AiSiniflandirmaYaniti
{
    public static AiKatmanSinifi MetindenAyristir(string metinIcerik)
    {
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
