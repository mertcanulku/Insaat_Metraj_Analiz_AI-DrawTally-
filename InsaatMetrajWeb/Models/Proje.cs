namespace InsaatMetrajWeb.Models;

public class Proje
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public List<MetrajKalemi> MetrajKalemleri { get; set; } = new();

    /// <summary>Hakediş hesaplarında kullanılan sözleşme bedeli ve varsayılan kesinti/avans oranları —
    /// bkz. Data/ProjeKaydi.cs.</summary>
    public decimal SozlesmeBedeli { get; set; }

    /// <summary>Fiyat farkı hesabında "temel dönem" olarak kullanılır — bkz. FiyatFarkiHesaplamaServisi.
    /// Boşsa fiyat farkı otomatik hesaplanamaz (kullanıcı elle oran/tutar girmeye devam edebilir).</summary>
    public DateOnly? SozlesmeTarihi { get; set; }

    public decimal VarsayilanAvansOrani { get; set; }
    public decimal VarsayilanTeminatOrani { get; set; }
    public decimal VarsayilanStopajOrani { get; set; }
    public decimal VarsayilanKdvOrani { get; set; } = 20;
    public decimal VarsayilanSgkKesintiOrani { get; set; }
    public decimal AlanM2 { get; set; }

    public decimal ToplamMaliyet() => MetrajKalemleri.Sum(k => k.ToplamMaliyet());
}

public class MetrajKalemi
{
    public int Id { get; set; }
    public required Poz Poz { get; set; }
    public string OlcumDetayi { get; set; } = "";
    public decimal Miktar { get; set; }
    public ProjeDisiplini Disiplin { get; set; }

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
