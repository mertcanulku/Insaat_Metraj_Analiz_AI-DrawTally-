using System.Globalization;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Tutarları CultureInfo.CurrentCulture'a göre biçimlendirir — bu kültür Program.cs'teki
/// UseRequestLocalization tarafından ziyaretçinin tarayıcı Accept-Language'ından set edilir.
/// Yani Türkiye'den bağlanan ₺, Almanya'dan bağlanan € görür; sabit "TL" metni yerine hangi
/// ülkeden kullanılıyorsa o ülkenin para birimi sembolü ve biçimi uygulanır.
/// </summary>
public static class ParaFormatlayici
{
    public static string Formatla(decimal tutar, int ondalikBasamak = 2)
        => tutar.ToString(ondalikBasamak <= 0 ? "C0" : "C2", CultureInfo.CurrentCulture);
}
