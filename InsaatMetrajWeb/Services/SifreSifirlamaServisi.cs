using InsaatMetrajWeb.Data;
using Microsoft.AspNetCore.Identity;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// "Şifremi unuttum" akışı — EmailDogrulamaServisi ile aynı 6 haneli kod deseni, ama ayrı bir alan
/// üzerinden çalışır (DogrulamaKodu'nu paylaşmak e-posta doğrulamasıyla çakışırdı). Kod doğrulandığında
/// UserManager'ın kendi (kriptografik olarak güvenli) parola sıfırlama token'ı arka planda üretilip
/// hemen kullanılır — kullanıcıya gösterilen sır her zaman 6 haneli kod, ama gerçek şifre değişimi
/// Identity'nin kendi güvenli mekanizmasıyla yapılır.
/// </summary>
public class SifreSifirlamaServisi(
    UserManager<ApplicationUser> userManager,
    IEmailGonderici emailGonderici,
    ILogger<SifreSifirlamaServisi> logger)
{
    private static readonly TimeSpan KodGecerlilikSuresi = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Yeni bir sıfırlama kodu üretip kullanıcıya e-posta ile göndermeyi dener. SMTP yapılandırılmamışsa
    /// kod, ekranda gösterilmek üzere geri döner; başarıyla gönderildiyse boş string döner.
    /// </summary>
    public async Task<string> KodGonder(ApplicationUser kullanici)
    {
        var kod = Random.Shared.Next(100000, 999999).ToString();
        kullanici.SifreSifirlamaKodu = kod;
        kullanici.SifreSifirlamaKoduSonGecerlilik = DateTime.UtcNow.Add(KodGecerlilikSuresi);
        await userManager.UpdateAsync(kullanici);

        var govde = $"Merhaba,\n\nDrawTally hesabının şifresini sıfırlamak için kodun: {kod}\n" +
                     $"Bu kod {KodGecerlilikSuresi.TotalMinutes:0} dakika geçerlidir.\n\n" +
                     "Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz, şifreniz değişmez.";

        var gonderildi = await emailGonderici.GonderAsync(kullanici.Email!, "DrawTally - Şifre Sıfırlama Kodu", govde);

        if (!gonderildi)
        {
            logger.LogWarning("SMTP yapılandırılmadı — şifre sıfırlama kodu gösterim modunda üretildi: {Email} -> {Kod}", kullanici.Email, kod);
            return kod;
        }

        return "";
    }

    public async Task<(bool Basarili, string Mesaj)> KoduDogrulaVeSifreyiSifirla(ApplicationUser kullanici, string girilenKod, string yeniSifre)
    {
        if (kullanici.SifreSifirlamaKodu == null || kullanici.SifreSifirlamaKoduSonGecerlilik == null)
        {
            return (false, "Önce bir sıfırlama kodu istemelisiniz.");
        }

        if (DateTime.UtcNow > kullanici.SifreSifirlamaKoduSonGecerlilik)
        {
            return (false, "Kodun süresi dolmuş. Yeni bir kod isteyin.");
        }

        if (!string.Equals(kullanici.SifreSifirlamaKodu, girilenKod.Trim(), StringComparison.Ordinal))
        {
            return (false, "Girdiğiniz kod hatalı.");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(kullanici);
        var sonuc = await userManager.ResetPasswordAsync(kullanici, token, yeniSifre);
        if (!sonuc.Succeeded)
        {
            return (false, string.Join(" ", sonuc.Errors.Select(e => e.Description)));
        }

        kullanici.SifreSifirlamaKodu = null;
        kullanici.SifreSifirlamaKoduSonGecerlilik = null;
        await userManager.UpdateAsync(kullanici);

        return (true, "Şifren güncellendi.");
    }
}
