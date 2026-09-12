using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir hakediş döneminin fiyat farkını, sözleşme (temel) dönemindeki TÜİK Yİ-ÜFE endeksi ile
/// hakediş dönemindeki endeksi karşılaştırarak otomatik hesaplar. Endeks verisi
/// EndeksYonetimi.razor'dan elle girilir (bkz. Data/EndeksDonemiKaydi.cs) — bu servis saf/statik
/// olup veritabanına dokunmaz, VeriDeposu.FiyatFarkiniOtomatikHesapla tarafından çağrılır.
///
/// Not: İstenen "katsayıyı BrutHakedisTutari() üzerine uygula" yaklaşımı yerine bilinçli olarak
/// BuDonemImalatTutari() (fiyat farkı hariç imalat tutarı) baz alınır — çünkü
/// BrutHakedisTutari() = BuDonemImalatTutari() + FiyatFarkiTutari olduğundan brüt tutarı baz almak
/// dairesel bir hesaba (fiyat farkının kendi üzerinden hesaplanmasına) yol açar.
/// </summary>
public static class FiyatFarkiHesaplamaServisi
{
    /// <summary>
    /// sozlesmeTarihi (temel dönem) ile hakedisTarihi (hakediş dönemi) için endeksler listede
    /// bulunursa oranı ((hakedişEndeks / temelEndeks) - 1) * 100 olarak hesaplar ve bu oranı
    /// imalatTutari'na uygulayarak tutarı üretir. Endeks eksikse veya temel dönem tanımsızsa
    /// Basarili=false ve kullanıcıya gösterilecek bir mesajla döner.
    /// </summary>
    public static FiyatFarkiSonucu Hesapla(DateOnly? sozlesmeTarihi, DateOnly hakedisTarihi, decimal imalatTutari, IEnumerable<EndeksDonemi> endeksler)
    {
        if (sozlesmeTarihi == null)
        {
            return new FiyatFarkiSonucu
            {
                Basarili = false,
                Mesaj = "Projede sözleşme tarihi tanımlı değil — Proje Ayarları'ndan girmelisin."
            };
        }

        var temel = endeksler.FirstOrDefault(e => e.Yil == sozlesmeTarihi.Value.Year && e.Ay == sozlesmeTarihi.Value.Month);
        var guncel = endeksler.FirstOrDefault(e => e.Yil == hakedisTarihi.Year && e.Ay == hakedisTarihi.Month);

        if (temel == null)
        {
            return new FiyatFarkiSonucu
            {
                Basarili = false,
                Mesaj = $"{sozlesmeTarihi.Value.Year}-{sozlesmeTarihi.Value.Month:00} (sözleşme/temel dönem) için endeks verisi girilmemiş — Endeks Yönetimi'nden ekle."
            };
        }

        if (guncel == null)
        {
            return new FiyatFarkiSonucu
            {
                Basarili = false,
                Mesaj = $"{hakedisTarihi.Year}-{hakedisTarihi.Month:00} (hakediş dönemi) için endeks verisi girilmemiş — Endeks Yönetimi'nden ekle."
            };
        }

        if (temel.TufeYiUfeDegeri <= 0)
        {
            return new FiyatFarkiSonucu
            {
                Basarili = false,
                Mesaj = $"{temel.DonemMetni()} dönemi için girilmiş endeks değeri geçersiz (0 veya negatif)."
            };
        }

        var oran = (guncel.TufeYiUfeDegeri / temel.TufeYiUfeDegeri - 1m) * 100m;
        var tutar = imalatTutari * oran / 100m;
        var ozet = $"Temel dönem {temel.DonemMetni()} (endeks {temel.TufeYiUfeDegeri:0.####}) → Hakediş dönemi {guncel.DonemMetni()} (endeks {guncel.TufeYiUfeDegeri:0.####}), oran %{oran:0.##}";

        return new FiyatFarkiSonucu
        {
            Basarili = true,
            Mesaj = "Fiyat farkı otomatik hesaplandı.",
            Ozet = ozet,
            Oran = oran,
            Tutar = tutar,
            TemelDonem = temel,
            HakedisDonemi = guncel
        };
    }
}
