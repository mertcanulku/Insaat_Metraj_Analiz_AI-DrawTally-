using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir CAD katmanının hangi poza karşılık geldiğini bir AI sağlayıcısı (IAiClassificationProvider)
/// üzerinden sınıflandırır.
///
/// Strateji: önce sağlayıcının ucuz/hızlı modeli sorulur. Modelin kendi döndürdüğü
/// güven skoru belirlenen eşiğin (GuvenEsigi) altındaysa, aynı soru daha güçlü bir
/// modele tekrar sorulur. Karar HER ZAMAN bir AI modeli tarafından
/// veriliyor — sabit kural/isim eşleştirmesi burada bypass olarak kullanılmıyor,
/// sadece "hangi modelin sorulacağı" maliyet amacıyla kademelendiriliyor.
///
/// Hangi sağlayıcının (Anthropic/Nvidia) kullanılacağı appsettings.json'daki
/// "AiProvider" ayarına göre DI kaydında belirlenir; bu sınıf sağlayıcıdan bağımsızdır.
/// </summary>
public class AiSiniflandirmaServisi
{
    private const int GuvenEsigi = 70; // bu değerin altında güçlü modele yükseltilir

    private readonly IAiClassificationProvider _saglayici;

    public AiSiniflandirmaServisi(IAiClassificationProvider saglayici)
    {
        _saglayici = saglayici;
    }

    public async Task<AiKatmanSinifi> SiniflandirAsync(CizimKatmanSinyali katman, List<Poz> pozlar)
    {
        if (!_saglayici.ApiAnahtariVarMi)
        {
            return new AiKatmanSinifi
            {
                PozId = null,
                Guven = 0,
                Gerekce = "AI sağlayıcısının API anahtarı tanımlı değil. appsettings.json veya ortam değişkenini ayarla.",
                KullanilanModel = "yapılandırılmadı"
            };
        }

        try
        {
            var ucuzSonuc = await _saglayici.SiniflandirAsync(_saglayici.UcuzModel, katman, pozlar);

            if (ucuzSonuc.Guven >= GuvenEsigi)
            {
                ucuzSonuc.KullanilanModel = _saglayici.UcuzModel;
                return ucuzSonuc;
            }

            // Ucuz modelin güveni düşük -> daha güçlü modele yükselt
            var gucluSonuc = await _saglayici.SiniflandirAsync(_saglayici.GucluModel, katman, pozlar);
            gucluSonuc.KullanilanModel = $"{_saglayici.GucluModel} (ucuz model güveni yetersizdi: %{ucuzSonuc.Guven})";
            return gucluSonuc;
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
}
