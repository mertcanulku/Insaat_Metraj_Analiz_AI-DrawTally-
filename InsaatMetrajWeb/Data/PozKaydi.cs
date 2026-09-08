namespace InsaatMetrajWeb.Data;

/// <summary>
/// ÇŞB rayiç/poz kütüphanesinin veritabanı kayıtları. Bu tablolar tüm kullanıcılar
/// arasında ortaktır (kullanıcıya özel değildir) — bkz. PozKutuphanesi, VeriDeposu.
/// Yıllık yayınlanan yeni rayiç/poz listeleri GecerlilikYili ile ayırt edilip
/// yeniden import edilebilir; eski yılların kaydı silinmez.
/// </summary>
public class RayicKaydi
{
    public int Id { get; set; }
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "";
    public string Kategori { get; set; } = "";
    public decimal Fiyat { get; set; }
    public int GecerlilikYili { get; set; }
}

public class PozKaydi
{
    public int Id { get; set; }
    public string PozKodu { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "";
    public string Kaynak { get; set; } = "ÇŞB";
    public int GecerlilikYili { get; set; }

    /// <summary>
    /// Kaynak PDF'te doğrudan basılı olan resmi birim fiyat. Maliyet hesaplarında HER ZAMAN
    /// bu alan kullanılır — AnalizSatirlari'ndan yeniden toplanmaz, çünkü PDF ayrıştırma
    /// bazı satırlarda (ör. malzeme+işçilik kırılımının bir kısmı) hatalı olabilir; resmi
    /// toplam fiyat PDF'ten doğrudan alındığı için bundan etkilenmez.
    /// </summary>
    public decimal ResmiFiyat { get; set; }

    /// <summary>
    /// AnalizSatirlari toplamının PDF'teki "Malzeme + İşçilik Tutarı" ve nihai fiyatla
    /// aritmetik olarak tutarlı olup olmadığı — import sırasında doğrulanır. false ise
    /// satır kırılımı sadece bilgi amaçlıdır, ResmiFiyat yine de güvenilirdir.
    /// </summary>
    public bool AnalizGuvenilir { get; set; }

    public List<PozAnalizSatiriKaydi> AnalizSatirlari { get; set; } = new();
}

public class PozAnalizSatiriKaydi
{
    public int Id { get; set; }
    public int PozKaydiId { get; set; }
    public PozKaydi? PozKaydi { get; set; }

    /// <summary>Kaynak PDF'teki ham rayiç/poz kodu — RayicKaydiId eşleşmemiş olsa bile korunur.</summary>
    public string RayicKodu { get; set; } = "";
    public int? RayicKaydiId { get; set; }
    public RayicKaydi? RayicKaydi { get; set; }

    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "";
    public decimal Miktar { get; set; }
}

/// <summary>Kullanıcıların kısaltma/eski kod ile arama yapabilmesi için eşleme kaydı.</summary>
public class AliasKaydi
{
    public int Id { get; set; }
    public int PozKaydiId { get; set; }
    public PozKaydi? PozKaydi { get; set; }
    public string AliasMetin { get; set; } = "";
}
