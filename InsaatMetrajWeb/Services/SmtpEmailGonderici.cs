using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// appsettings.json'daki Smtp bölümünü kullanarak e-posta gönderir (örn. Gmail SMTP + Uygulama Şifresi).
/// Smtp:Host boş bırakılırsa hiç denemez — bu durumda çağıran taraf (EmailDogrulamaServisi)
/// doğrulama kodunu ekranda gösterir, böylece SMTP kurulmadan da uygulama uçtan uca çalışır.
/// </summary>
public class SmtpEmailGonderici(IConfiguration config, ILogger<SmtpEmailGonderici> logger) : IEmailGonderici
{
    public async Task<bool> GonderAsync(string aliciEmail, string konu, string govde)
    {
        var host = config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        try
        {
            var mesaj = new MimeMessage();
            var gonderenAdres = config["Smtp:From"] ?? config["Smtp:User"] ?? "";
            mesaj.From.Add(MailboxAddress.Parse(gonderenAdres));
            mesaj.To.Add(MailboxAddress.Parse(aliciEmail));
            mesaj.Subject = konu;
            mesaj.Body = new TextPart("plain") { Text = govde };

            var port = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config["Smtp:User"], config["Smtp:Password"]);
            await client.SendAsync(mesaj);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "E-posta gönderilemedi: {Alici}", aliciEmail);
            return false;
        }
    }
}
