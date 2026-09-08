using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir çizimden çıkarılan metin kümesini veya görseli, verilen tek bir AI modeliyle
/// yapılandırılmış oda bilgisine çeviren sağlayıcı. AiSiniflandirmaServisi'ndeki
/// ucuz->güçlü model yükseltme mantığı bu arayüzün üstünde çalışır; sağlayıcı yalnızca
/// "hangi modele nasıl soru sorulur ve cevap nasıl ayrıştırılır" kısmından sorumludur.
/// </summary>
public interface IAiClassificationProvider
{
    /// <summary>İlk denemede kullanılacak ucuz/hızlı model adı.</summary>
    string UcuzModel { get; }

    /// <summary>Güven eşiğinin altında kalınırsa yükseltilecek güçlü model adı.</summary>
    string GucluModel { get; }

    /// <summary>API anahtarı yapılandırılmış mı.</summary>
    bool ApiAnahtariVarMi { get; }

    Task<OdaYapilandirmaSonucu> MetinSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni);

    /// <summary>
    /// Sağlayıcı/model görsel girdiyi desteklemiyorsa NotSupportedException fırlatabilir —
    /// AiSiniflandirmaServisi bunu yakalayıp kullanıcıya "sınıflandırılamadı" olarak raporlar.
    /// </summary>
    Task<OdaYapilandirmaSonucu> GorselSiniflandirAsync(string model, string sistemPrompt, string kullaniciMetni, byte[] pngGorsel);
}
