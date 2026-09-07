namespace InsaatMetrajWeb.Models;

/// <summary>
/// Bir CAD katmanından (layer) çıkarılan geometrik sinyaller — sabit bir
/// isim eşleştirmesi değil, yapay zekanın "bu ne olabilir?" diye akıl
/// yürütmesi için ham veri. Katman ismi hangi dilde/kısaltmada olursa
/// olsun, geometri (kalınlık, kapalılık, şekil) tutarlı bir ipucu verir.
/// </summary>
public class CizimKatmanSinyali
{
    public string LayerAdi { get; set; } = "";
    public int EntitySayisi { get; set; }
    public bool KapaliMi { get; set; }
    public double OrtSegmentUzunlugu { get; set; }
    public double CizgiKalinligi { get; set; }
    public double EnBoyOrani { get; set; }
    public double ToplamUzunluk { get; set; }
    public double ToplamAlan { get; set; }
}

/// <summary>Yapay zekanın bir katman için verdiği sınıflandırma kararı.</summary>
public class AiKatmanSinifi
{
    public int? PozId { get; set; }
    public int Guven { get; set; }
    public string Gerekce { get; set; } = "";
    public string KullanilanModel { get; set; } = "";
}

/// <summary>
/// Bir katmanın tam analiz sonucu: geometrik sinyaller + AI'nin önerisi.
/// Kullanıcı onaylamadan proje.MetrajKalemleri'ne eklenmez.
/// </summary>
public class CizimAnalizSonucu
{
    public CizimKatmanSinyali Sinyal { get; set; } = new();
    public AiKatmanSinifi Oneri { get; set; } = new();
    public bool Eklendi { get; set; }
}
