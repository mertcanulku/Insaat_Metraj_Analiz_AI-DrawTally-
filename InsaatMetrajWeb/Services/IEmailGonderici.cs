namespace InsaatMetrajWeb.Services;

public interface IEmailGonderici
{
    /// <summary>E-postayı göndermeyi dener. SMTP yapılandırılmamışsa (appsettings'te Smtp:Host boşsa)
    /// hiçbir şey yapmadan false döner — çağıran taraf bu durumda kodu ekranda gösterir.</summary>
    Task<bool> GonderAsync(string aliciEmail, string konu, string govde);
}
