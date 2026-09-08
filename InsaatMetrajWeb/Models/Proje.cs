namespace InsaatMetrajWeb.Models;

public class Proje
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public List<MetrajKalemi> MetrajKalemleri { get; set; } = new();

    public decimal ToplamMaliyet() => MetrajKalemleri.Sum(k => k.ToplamMaliyet());
}

public class MetrajKalemi
{
    public int Id { get; set; }
    public required Poz Poz { get; set; }
    public string OlcumDetayi { get; set; } = "";
    public decimal Miktar { get; set; }

    public decimal ToplamMaliyet() => Miktar * Poz.BirimMaliyet();
}

/// <summary>CSV'den bir satırın import sonucu — başarılı mı, hangi poza eşleşti, hata var mı.</summary>
public class ImportSonucSatiri
{
    public int SatirNo { get; set; }
    public string HamMetin { get; set; } = "";
    public bool Basarili { get; set; }
    public string Mesaj { get; set; } = "";
    public string EslesenPozKodu { get; set; } = "";

    /// <summary>Eklenen metraj kaleminin veritabanı id'si — sonradan silinebilsin diye.</summary>
    public int? EklenenMetrajKalemiId { get; set; }

    /// <summary>
    /// Poz bulunamadığı için başarısız olan ama ölçüm detayı/miktarı ayrıştırılabilmiş satırlar için true —
    /// bu durumda kullanıcıya "poz seç" seçeneği sunulur, satır kalıcı olarak hatalı sayılmaz.
    /// </summary>
    public bool PozEksik { get; set; }
    public string OlcumDetayiTaslak { get; set; } = "";
    public decimal? MiktarTaslak { get; set; }

    /// <summary>UI-only: kullanıcının "poz eksik" satırında dropdown'dan seçtiği poz — kalıcı veri değil.</summary>
    public int? SeciliPozId { get; set; }
}
