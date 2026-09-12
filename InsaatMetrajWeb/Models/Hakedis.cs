namespace InsaatMetrajWeb.Models;

/// <summary>
/// Bir hakediş (ilerleme ödemesi) dönemi — proje içinde HakedisNo ile artan sırada birden fazla
/// olabilir. Her dönem, kalemlerin bir önceki hakedişe göre kümülatif tamamlanma farkı üzerinden
/// o dönem yapılacak imalat tutarını, kesintileri ve net ödenecek tutarı hesaplar.
///
/// Basitleştirme: avans/teminat/stopaj/KDV oranları o dönemin brüt hakediş tutarına doğrudan
/// uygulanır (şantiyede yaygın pratik) — resmi hakedişlerdeki üst limit takibi (ör. toplam avansın
/// sözleşme avansını aşmaması) bu sürümde yok.
/// </summary>
public class Hakedis
{
    public int Id { get; set; }
    public int HakedisNo { get; set; }
    public DateOnly Tarih { get; set; }

    public decimal AvansOrani { get; set; }
    public decimal TeminatOrani { get; set; }
    public decimal StopajOrani { get; set; }
    public decimal KdvOrani { get; set; }
    public decimal SgkKesintiOrani { get; set; }

    public decimal FiyatFarkiOrani { get; set; }
    public decimal FiyatFarkiTutari { get; set; }

    /// <summary>true ise FiyatFarkiOrani/Tutari, FiyatFarkiHesaplamaServisi tarafından TÜİK endeksinden
    /// otomatik önerildi; false ise kullanıcı elle girdi/değiştirdi — bkz. HakedisForm.razor.</summary>
    public bool FiyatFarkiOtomatikMi { get; set; }

    /// <summary>Otomatik hesaplamanın hangi endeks dönemlerini kullandığını özetleyen, kullanıcıya
    /// gösterilen kısa metin (şeffaflık için) — otomatik değilse boş.</summary>
    public string FiyatFarkiHesaplamaOzeti { get; set; } = "";

    public List<HakedisKalemi> Kalemler { get; set; } = new();

    /// <summary>Bu dönemde yapılan imalatın tutarı (kalemlerin bu-dönem tutarları toplamı).</summary>
    public decimal BuDonemImalatTutari() => Kalemler.Sum(k => k.BuDonemTutar());

    /// <summary>Projenin bu hakedişe kadarki kümülatif imalat tutarı.</summary>
    public decimal KumulatifImalatTutari() => Kalemler.Sum(k => k.KumulatifTutar());

    public decimal BrutHakedisTutari() => BuDonemImalatTutari() + FiyatFarkiTutari;

    public decimal TeminatKesintisi() => BrutHakedisTutari() * TeminatOrani / 100m;

    public decimal StopajKesintisi() => BrutHakedisTutari() * StopajOrani / 100m;

    public decimal SgkKesintisi() => BrutHakedisTutari() * SgkKesintiOrani / 100m;

    public decimal AvansMahsubu() => BrutHakedisTutari() * AvansOrani / 100m;

    public decimal KdvTutari() => BrutHakedisTutari() * KdvOrani / 100m;

    public decimal NetOdenecekTutar() =>
        BrutHakedisTutari() + KdvTutari() - TeminatKesintisi() - StopajKesintisi() - SgkKesintisi() - AvansMahsubu();
}

/// <summary>Bir hakedişte tek bir metraj kalemine ait kümülatif/dönem hesabı.</summary>
public class HakedisKalemi
{
    public required MetrajKalemi MetrajKalemi { get; set; }

    /// <summary>Bir önceki hakedişten gelen kümülatif tamamlanma yüzdesi (0-100) — ilk hakedişte 0.</summary>
    public decimal OncekiKumulatifYuzde { get; set; }

    /// <summary>Bu hakedişte girilen, o ana kadarki toplam kümülatif tamamlanma yüzdesi (0-100).</summary>
    public decimal KumulatifYuzde { get; set; }

    public decimal BuDonemYuzde() => KumulatifYuzde - OncekiKumulatifYuzde;

    public decimal KumulatifTutar() => MetrajKalemi.ToplamMaliyet() * KumulatifYuzde / 100m;

    public decimal BuDonemTutar() => MetrajKalemi.ToplamMaliyet() * BuDonemYuzde() / 100m;
}

/// <summary>Bir hakediş kaydetme işleminin sonucu — kümülatif yüzde azalması gibi doğrulama hataları başarısız sayılır.</summary>
public class HakedisKaydetSonucu
{
    public bool Basarili { get; set; }
    public string Mesaj { get; set; } = "";
    public int? HakedisId { get; set; }
}

/// <summary>
/// FiyatFarkiHesaplamaServisi'nin bir hesaplama denemesinin sonucu — temel (sözleşme) dönemi ile
/// hakediş dönemi endeksleri bulunamazsa Basarili=false ve sebebi Mesaj'da döner; bulunursa
/// hesaplanan Oran/Tutar ile birlikte hangi dönemlerin kullanıldığını (Ozet) da taşır.
/// </summary>
public class FiyatFarkiSonucu
{
    public bool Basarili { get; set; }
    public string Mesaj { get; set; } = "";
    public string Ozet { get; set; } = "";
    public decimal Oran { get; set; }
    public decimal Tutar { get; set; }
    public EndeksDonemi? TemelDonem { get; set; }
    public EndeksDonemi? HakedisDonemi { get; set; }
}

/// <summary>Hakediş listesinde gösterilen özet satır.</summary>
public class HakedisOzeti
{
    public int Id { get; set; }
    public int HakedisNo { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal NetOdenecekTutar { get; set; }
}
