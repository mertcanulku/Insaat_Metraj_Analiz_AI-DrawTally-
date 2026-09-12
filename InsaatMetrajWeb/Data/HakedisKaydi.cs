namespace InsaatMetrajWeb.Data;

/// <summary>
/// Bir hakediş (ilerleme ödemesi) dönemi kaydı. Bir projede HakedisNo ile artan sırada birden
/// fazla hakediş olabilir — her biri bir öncekinin kümülatif tamamlanma yüzdesi üzerine ekler
/// (bkz. HakedisKalemiKaydi.KumulatifYuzde ve VeriDeposu.HakedisBul'daki önceki-yüzde hesaplaması).
/// </summary>
public class HakedisKaydi
{
    public int Id { get; set; }
    public int ProjeKaydiId { get; set; }
    public ProjeKaydi? ProjeKaydi { get; set; }
    public int HakedisNo { get; set; }
    public DateOnly Tarih { get; set; }

    public decimal AvansOrani { get; set; }
    public decimal TeminatOrani { get; set; }
    public decimal StopajOrani { get; set; }
    public decimal KdvOrani { get; set; }
    public decimal SgkKesintiOrani { get; set; }

    /// <summary>Fiyat farkı için yardımcı oran — TÜİK endeksi otomasyonu yok, sadece FiyatFarkiTutari'nı önermek için kullanılır.</summary>
    public decimal FiyatFarkiOrani { get; set; }

    /// <summary>Hakediş hesabında asıl kullanılan fiyat farkı tutarı — oran ile önerilir ama elle değiştirilebilir.</summary>
    public decimal FiyatFarkiTutari { get; set; }

    public List<HakedisKalemiKaydi> Kalemler { get; set; } = new();
}

/// <summary>Bir hakedişte tek bir metraj kalemi için o ana kadarki kümülatif tamamlanma yüzdesi.</summary>
public class HakedisKalemiKaydi
{
    public int Id { get; set; }
    public int HakedisKaydiId { get; set; }
    public HakedisKaydi? HakedisKaydi { get; set; }
    public int MetrajKalemiKaydiId { get; set; }

    /// <summary>0-100 arası, bu hakedişe kadar toplam tamamlanma yüzdesi.</summary>
    public decimal KumulatifYuzde { get; set; }
}
