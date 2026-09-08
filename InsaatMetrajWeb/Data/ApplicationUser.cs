using Microsoft.AspNetCore.Identity;

namespace InsaatMetrajWeb.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>Bekleyen e-posta doğrulama kodu (6 haneli). Doğrulandığında null'a döner.</summary>
    public string? DogrulamaKodu { get; set; }

    public DateTime? DogrulamaKoduSonGecerlilik { get; set; }

    /// <summary>Kullanım koşullarını kabul ettiği an (UTC). Kayıt sırasında zorunlu olduğu için hep dolu olur.</summary>
    public DateTime? SozlesmeKabulTarihi { get; set; }
}
