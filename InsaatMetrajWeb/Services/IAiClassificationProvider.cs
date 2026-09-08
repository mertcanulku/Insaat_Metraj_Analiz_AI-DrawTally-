using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir CAD katmanını verilen tek bir AI modeliyle sınıflandıran sağlayıcı.
/// AiSiniflandirmaServisi'ndeki ucuz->güçlü model yükseltme mantığı bu arayüzün
/// üstünde çalışır; sağlayıcı yalnızca "hangi modele nasıl soru sorulur ve
/// cevap nasıl ayrıştırılır" kısmından sorumludur.
/// </summary>
public interface IAiClassificationProvider
{
    /// <summary>İlk denemede kullanılacak ucuz/hızlı model adı.</summary>
    string UcuzModel { get; }

    /// <summary>Güven eşiğinin altında kalınırsa yükseltilecek güçlü model adı.</summary>
    string GucluModel { get; }

    /// <summary>API anahtarı yapılandırılmış mı.</summary>
    bool ApiAnahtariVarMi { get; }

    Task<AiKatmanSinifi> SiniflandirAsync(string model, CizimKatmanSinyali katman, List<Poz> pozlar);
}
