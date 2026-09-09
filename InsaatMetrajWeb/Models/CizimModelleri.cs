namespace InsaatMetrajWeb.Models;

/// <summary>Bir oda/alan değerinin nereden geldiği — kullanıcıya güven düzeyini göstermek için.</summary>
public enum KaynakTuru
{
    /// <summary>PDF'in seçilebilir metin katmanından okundu (oda adı/alan yazısı).</summary>
    VektorMetin,
    /// <summary>DWG'deki kapalı bir poligondan shoelace formülüyle hesaplandı (deterministik).</summary>
    VektorGeometri,
    /// <summary>Metin katmanı yoktu veya yetersizdi — sayfa/bölge görsel olarak yapay zekaya gönderildi.</summary>
    AIGorsel,
    /// <summary>DWG'deki Insert (blok referansı) nesnelerinin katman+blok bazında sayılmasıyla elde edildi
    /// (deterministik) — kolon/kiriş, priz/anahtar gibi alan değil adet ile ifade edilen elemanlar için.</summary>
    BlokSayimi
}

/// <summary>Bir satırın ne kadar güvenilir olduğuna dair kabaca üç seviyeli özet.</summary>
public enum GuvenSkoru
{
    Dusuk,
    Orta,
    Yuksek
}

/// <summary>Çizimin ait olduğu mühendislik disiplini — DisiplinTespitServisi tarafından
/// pafta/başlık metni, DWG katman adları ve içerik desenlerinden tahmin edilir.</summary>
public enum ProjeDisiplini
{
    Bilinmiyor,
    Statik,
    Mimari,
    Isitma,
    Sihhi,
    Elektrik
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

    /// <summary>Bu satırın üretildiği yüklenen dosyanın adı — aynı projeye art arda birden fazla
    /// çizim/PDF yüklendiğinde sonuçlar birikir (üzerine yazılmaz), bu alan hangi satırın hangi
    /// dosyadan geldiğini ayırt etmek için kullanılır.</summary>
    public string KaynakDosya { get; set; } = "";

    /// <summary>Blok (Insert) referansı sayımından gelen satırlar için adet — kolon/kiriş, priz/anahtar
    /// gibi alan değil adet ile ifade edilen elemanlarda dolu, aksi halde null.</summary>
    public int? Adet { get; set; }

    /// <summary>Çizgi (Line) / açık polyline segmentlerinin katman bazında toplam uzunluğu (metre) —
    /// elektrik hat/kablo gibi uzunluk ile ifade edilen elemanlarda dolu, aksi halde null.</summary>
    public decimal? Uzunluk { get; set; }

    /// <summary>Çizimin ait olduğu disiplin (tespit edilebildiyse) — AI'ya bağlam olarak
    /// verilir ve UI'da Kesin/Belirsiz sekmelerinde etiket olarak gösterilir.</summary>
    public ProjeDisiplini Disiplin { get; set; }

    /// <summary>Yapay zekanın önerdiği poz (varsa) — kullanıcı onaylayınca bu poz metraj kalemi olarak eklenir.</summary>
    public int? OnerilenPozId { get; set; }
    public string OneriGerekcesi { get; set; } = "";
    public string KullanilanModel { get; set; } = "";

    /// <summary>Onaylanıp eklendiğinde oluşan metraj kaleminin veritabanı id'si — sonradan silinebilsin diye.</summary>
    public int? EklenenMetrajKalemiId { get; set; }

    /// <summary>UI-only: AI hiçbir poz öneremediğinde kullanıcının dropdown'dan seçtiği poz — kalıcı veri değil.</summary>
    public int? SeciliPozId { get; set; }
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
