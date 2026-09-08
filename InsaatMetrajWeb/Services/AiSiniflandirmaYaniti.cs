using System.Text.Json;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>Model çıktısındaki serbest metinden OdaYapilandirmaSonucu JSON'unu ayrıştırır.</summary>
internal static class AiSiniflandirmaYaniti
{
    public static OdaYapilandirmaSonucu MetindenAyristir(string metinIcerik)
    {
        // Model bazen JSON'un etrafına ekstra metin ekleyebilir — ilk { ile son } arasını al
        var baslangic = metinIcerik.IndexOf('{');
        var bitis = metinIcerik.LastIndexOf('}');
        if (baslangic < 0 || bitis < 0 || bitis <= baslangic)
            return new OdaYapilandirmaSonucu { Guven = 0, Gerekce = "Model geçerli bir JSON döndürmedi: " + metinIcerik };

        var jsonKismi = metinIcerik.Substring(baslangic, bitis - baslangic + 1);
        using var sonucDoc = JsonDocument.Parse(jsonKismi);
        var root = sonucDoc.RootElement;

        string odaAdi = root.TryGetProperty("odaAdi", out var odaAdiEl) ? odaAdiEl.GetString() ?? "" : "";
        decimal? alanM2 = root.TryGetProperty("alanM2", out var alanEl) && alanEl.ValueKind != JsonValueKind.Null
            ? alanEl.GetDecimal() : null;
        string kat = root.TryGetProperty("kat", out var katEl) ? katEl.GetString() ?? "" : "";
        int? pozId = root.TryGetProperty("pozId", out var pozIdEl) && pozIdEl.ValueKind != JsonValueKind.Null
            ? pozIdEl.GetInt32() : null;
        int guven = root.TryGetProperty("guven", out var guvenEl) ? guvenEl.GetInt32() : 0;
        string gerekce = root.TryGetProperty("gerekce", out var gerekceEl) ? gerekceEl.GetString() ?? "" : "";

        return new OdaYapilandirmaSonucu
        {
            OdaAdi = odaAdi,
            AlanM2 = alanM2,
            KatAdi = kat,
            OnerilenPozId = pozId,
            Guven = guven,
            Gerekce = gerekce
        };
    }
}
