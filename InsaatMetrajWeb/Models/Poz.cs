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

    public decimal BirimMaliyet() =>
        AnalizSatirlari.Sum(s => s.Rayic.Fiyat * s.Miktar);
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
