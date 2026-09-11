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

    /// <summary>Hakediş ilerleme görünümü (Keşif Özeti'nde tamamlanan/kalan) Starter'da kapalı, Pro ve Business'ta açık.</summary>
    public static bool IlerlemeGorunumuIzinli(UyelikPlani plan) => plan != UyelikPlani.Starter;

    /// <summary>
    /// Tam kapsamlı hakediş hesabı (teminat/stopaj/KDV kesintileri, avans mahsubu, fiyat farkı)
    /// Starter'da kapalı — Starter'da hakediş sadece imalat tutarı ve % üzerinden çalışır.
    /// Pro ve Business'ta tüm kesinti/avans/fiyat farkı alanları açık.
    /// </summary>
    public static bool HakedisTamKapsamliIzinli(UyelikPlani plan) => plan != UyelikPlani.Starter;

    /// <summary>Plana göre aylık yeni-hakediş-oluşturma kredisi (bkz. HakedisKrediMaliyeti — düzenleme/export kredi harcamaz, sadece yeni dönem oluşturmak harcar).</summary>
    public static int AylikHakedisKredisi(UyelikPlani plan) => plan switch
    {
        UyelikPlani.Starter => 1,
        UyelikPlani.Pro => 10,
        UyelikPlani.Business => 30,
        _ => 1
    };

    /// <summary>
    /// Bir hakediş oluşturmanın kaç kredi tutacağını projenin alanına (m²) göre hesaplar —
    /// her 100 m² için 1 kredi, en az 1 kredi (alan girilmemiş/küçük projeler de kredi sistemini
    /// tamamen bypass edemesin diye). Örn: 500 m² → 5 kredi, 1200 m² → 12 kredi.
    /// </summary>
    public static int HakedisKrediMaliyeti(decimal alanM2) => Math.Max(1, (int)Math.Ceiling(alanM2 / 100m));

    public static string PlanAdi(UyelikPlani plan) => plan switch
    {
        UyelikPlani.Starter => "Starter",
        UyelikPlani.Pro => "Pro",
        UyelikPlani.Business => "Business",
        _ => plan.ToString()
    };
}
