using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Data;

/// <summary>Bir projenin veritabanı kaydı. Sahibi (SahipId) dışındaki kullanıcılar bu projeyi göremez.</summary>
public class ProjeKaydi
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string SahipId { get; set; } = "";
    public ApplicationUser? Sahip { get; set; }
    public List<MetrajKalemiKaydi> MetrajKalemleri { get; set; } = new();
}

/// <summary>
/// Bir metraj kalemi kaydı. PozId, VeriDeposu'ndaki (bellek içi) poz kütüphanesine işaret eder —
/// poz kütüphanesi tüm kullanıcılar arasında ortak olduğu için ayrıca veritabanına taşınmadı.
/// </summary>
public class MetrajKalemiKaydi
{
    public int Id { get; set; }
    public int ProjeKaydiId { get; set; }
    public ProjeKaydi? ProjeKaydi { get; set; }
    public int PozId { get; set; }
    public string OlcumDetayi { get; set; } = "";
    public decimal Miktar { get; set; }

    /// <summary>Kalemin ait olduğu mühendislik disiplini — çizim analizinden (DisiplinTespitServisi)
    /// veya CSV/PDF import sırasında kullanıcının seçtiği disiplinden gelir; bilinmiyorsa Bilinmiyor.</summary>
    public ProjeDisiplini Disiplin { get; set; } = ProjeDisiplini.Bilinmiyor;
}
