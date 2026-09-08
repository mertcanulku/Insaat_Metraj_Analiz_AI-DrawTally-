using InsaatMetrajWeb.Data;
using Microsoft.AspNetCore.Identity;

namespace InsaatMetrajWeb.Services;

public class EmailDogrulamaServisi(
    UserManager<ApplicationUser> userManager,
    IEmailGonderici emailGonderici,
    ILogger<EmailDogrulamaServisi> logger)
{
    private static readonly TimeSpan KodGecerlilikSuresi = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Yeni bir doğrulama kodu üretip kullanıcıya e-posta ile göndermeyi dener.
    /// SMTP yapılandırılmamışsa e-posta gönderilmez; bu durumda kod, ekranda gösterilmek üzere geri döner.
    /// E-posta başarıyla gönderildiyse boş string döner (kodu ekranda göstermeye gerek yok).
    /// </summary>
    public async Task<string> KodGonder(ApplicationUser kullanici)
    {
        var kod = Random.Shared.Next(100000, 999999).ToString();
        kullanici.DogrulamaKodu = kod;
        kullanici.DogrulamaKoduSonGecerlilik = DateTime.UtcNow.Add(KodGecerlilikSuresi);
        await userManager.UpdateAsync(kullanici);

        var govde = $"Merhaba,\n\nDrawTally hesabınızı doğrulamak için kodunuz: {kod}\n" +
                     $"Bu kod {KodGecerlilikSuresi.TotalMinutes:0} dakika geçerlidir.\n\n" +
                     "Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz.";

        var gonderildi = await emailGonderici.GonderAsync(kullanici.Email!, "DrawTally - E-posta Doğrulama Kodu", govde);

        if (!gonderildi)
        {
            logger.LogWarning("SMTP yapılandırılmadı — doğrulama kodu gösterim modunda üretildi: {Email} -> {Kod}", kullanici.Email, kod);
            return kod;
        }

        return "";
    }

    public async Task<(bool Basarili, string Mesaj)> KoduDogrula(ApplicationUser kullanici, string girilenKod)
    {
        if (kullanici.EmailConfirmed)
        {
            return (true, "E-posta zaten doğrulanmış.");
        }

        if (kullanici.DogrulamaKodu == null || kullanici.DogrulamaKoduSonGecerlilik == null)
        {
            return (false, "Önce bir doğrulama kodu istemelisiniz.");
        }

        if (DateTime.UtcNow > kullanici.DogrulamaKoduSonGecerlilik)
        {
            return (false, "Kodun süresi dolmuş. Yeni bir kod isteyin.");
        }

        if (!string.Equals(kullanici.DogrulamaKodu, girilenKod.Trim(), StringComparison.Ordinal))
        {
            return (false, "Girdiğiniz kod hatalı.");
        }

        kullanici.EmailConfirmed = true;
        kullanici.DogrulamaKodu = null;
        kullanici.DogrulamaKoduSonGecerlilik = null;
        await userManager.UpdateAsync(kullanici);

        return (true, "E-posta doğrulandı.");
    }
}
