namespace InsaatMetrajWeb.Models;

/// <summary>Bir oda/alan değerinin nereden geldiği — kullanıcıya güven düzeyini göstermek için.</summary>
public enum KaynakTuru
{
    /// <summary>PDF'in seçilebilir metin katmanından okundu (oda adı/alan yazısı).</summary>
    VektorMetin,
    /// <summary>DWG'deki kapalı bir poligondan shoelace formülüyle hesaplandı (deterministik).</summary>
    VektorGeometri,
    /// <summary>Metin katmanı yoktu veya yetersizdi — sayfa/bölge görsel olarak yapay zekaya gönderildi.</summary>
    AIGorsel
}

/// <summary>Bir satırın ne kadar güvenilir olduğuna dair kabaca üç seviyeli özet.</summary>
public enum GuvenSkoru
{
    Dusuk,
    Orta,
    Yuksek
}

/// <summary>
/// PDF veya DWG çiziminden çıkarılan tek bir oda/alan satırı — iki ayrı
/// pipeline (PDF ve DWG) aynı modele yazar, aynı onay tablosunda gösterilir.
/// Kullanıcı onaylamadan proje.MetrajKalemleri'ne eklenmez.
/// </summary>
public class CizimAnalizSonucu
{
    public string OdaAdi { get; set; } = "";
    public decimal AlanM2 { get; set; }
    public string KatAdi { get; set; } = "";
    public KaynakTuru KaynakTuru { get; set; }
    public GuvenSkoru GuvenSkoru { get; set; }
    public bool OnaylandiMi { get; set; }

    /// <summary>Yapay zekanın önerdiği poz (varsa) — kullanıcı onaylayınca bu poz metraj kalemi olarak eklenir.</summary>
    public int? OnerilenPozId { get; set; }
    public string OneriGerekcesi { get; set; } = "";
    public string KullanilanModel { get; set; } = "";
}

/// <summary>AiSiniflandirmaServisi'nin bir metin kümesinden veya görselden ürettiği yapılandırılmış sonuç.</summary>
public class OdaYapilandirmaSonucu
{
    public string OdaAdi { get; set; } = "";
    public decimal? AlanM2 { get; set; }
    public string KatAdi { get; set; } = "";
    public int Guven { get; set; } // 0-100
    public int? OnerilenPozId { get; set; }
    public string Gerekce { get; set; } = "";
    public string KullanilanModel { get; set; } = "";
}
