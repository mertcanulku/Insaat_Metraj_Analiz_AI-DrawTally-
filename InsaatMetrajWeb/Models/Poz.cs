namespace InsaatMetrajWeb.Models;

public class Rayic
{
    public int Id { get; set; }
    public string PozKodu { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "";
    public string Kategori { get; set; } = "";
    public decimal Fiyat { get; set; }
    public DateOnly GecerlilikTarihi { get; set; }
}

public class Poz
{
    public int Id { get; set; }
    public string PozKodu { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Birim { get; set; } = "";
    public string Kaynak { get; set; } = "ÇŞB";
    public List<PozAnalizSatiri> AnalizSatirlari { get; set; } = new();

    /// <summary>
    /// ÇŞB kaynağında doğrudan basılı olan resmi birim fiyat. Ayarlıysa BirimMaliyet()
    /// bunu döner — AnalizSatirlari'ndan yeniden toplamaz, çünkü PDF ayrıştırmasında bazı
    /// satırlar (ör. eşleşmeyen rayiç kodu) eksik kalmış olabilir; resmi toplam bundan etkilenmez.
    /// </summary>
    public decimal? ResmiFiyat { get; set; }

    /// <summary>AnalizSatirlari kırılımının resmi toplamla aritmetik olarak tutarlı olduğu import sırasında doğrulandı mı.</summary>
    public bool AnalizGuvenilir { get; set; } = true;

    public decimal BirimMaliyet() =>
        ResmiFiyat ?? AnalizSatirlari.Sum(s => s.Rayic.Fiyat * s.Miktar);
}

public class PozAnalizSatiri
{
    public required Rayic Rayic { get; set; }
    public decimal Miktar { get; set; }
}

/// <summary>Kullanıcıların kısaltma/eski kod ile arama yapabilmesi için eşleme kaydı.</summary>
public class Alias
{
    public int Id { get; set; }
    public int PozId { get; set; }
    public string AliasMetin { get; set; } = "";
}
