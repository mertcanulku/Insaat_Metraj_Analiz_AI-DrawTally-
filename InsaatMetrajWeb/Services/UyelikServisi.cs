using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Üç üyelik planının (Starter/Pro/Business) sınır ve özellik kurallarını tek yerde tutar —
/// hem sayfaların hem de ileride eklenecek başka kontrollerin aynı kaynağı kullanması için.
/// Şu an ödeme entegrasyonu yok; plan değişimi Uyelik sayfasından manuel yapılır.
/// </summary>
public static class UyelikServisi
{
    /// <summary>Plana göre en fazla kaç proje oluşturulabilir.</summary>
    public static int ProjeSiniri(UyelikPlani plan) => plan switch
    {
        UyelikPlani.Starter => 3,
        UyelikPlani.Pro => 15,
        UyelikPlani.Business => int.MaxValue,
        _ => 3
    };

    /// <summary>Excel/PDF keşif özeti dışa aktarımı Starter'da kapalı, Pro ve Business'ta açık.</summary>
    public static bool DisaAktarimIzinli(UyelikPlani plan) => plan != UyelikPlani.Starter;

    public static string PlanAdi(UyelikPlani plan) => plan switch
    {
        UyelikPlani.Starter => "Starter",
        UyelikPlani.Pro => "Pro",
        UyelikPlani.Business => "Business",
        _ => plan.ToString()
    };
}
