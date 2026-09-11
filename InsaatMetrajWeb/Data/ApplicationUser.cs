using System.ComponentModel.DataAnnotations.Schema;
using InsaatMetrajWeb.Models;
using Microsoft.AspNetCore.Identity;

namespace InsaatMetrajWeb.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>Bekleyen e-posta doğrulama kodu (6 haneli). Doğrulandığında null'a döner.</summary>
    public string? DogrulamaKodu { get; set; }

    public DateTime? DogrulamaKoduSonGecerlilik { get; set; }

    /// <summary>Kullanım koşullarını kabul ettiği an (UTC). Kayıt sırasında zorunlu olduğu için hep dolu olur.</summary>
    public DateTime? SozlesmeKabulTarihi { get; set; }

    /// <summary>Kullanıcının satın aldığı/seçtiği plan. Kayıt sırasında Starter olarak başlar.</summary>
    public UyelikPlani Plan { get; set; } = UyelikPlani.Starter;

    /// <summary>
    /// Kayıt sırasında verilen 7 günlük ücretsiz Pro deneme süresinin bitiş anı (UTC). Deneme
    /// bittiğinde veya kullanıcı manuel bir plan seçtiğinde null'a döner — bkz. <see cref="EtkinPlan"/>.
    /// </summary>
    public DateTime? DenemeBitisTarihi { get; set; }

    /// <summary>
    /// Kullanıcının şu anda gerçekte sahip olduğu plan — deneme süresi hâlâ aktifse Plan alanı
    /// ne olursa olsun Pro döner, aksi halde Plan'ın kendisi döner. Veritabanına ayrıca sütun
    /// olarak yazılmaz, bkz. [NotMapped].
    /// </summary>
    [NotMapped]
    public UyelikPlani EtkinPlan =>
        DenemeBitisTarihi.HasValue && DenemeBitisTarihi.Value > DateTime.UtcNow
            ? UyelikPlani.Pro
            : Plan;

    // --- Şirket / fatura bilgileri (kayıt sırasında isteğe bağlı olarak doldurulur) ---

    /// <summary>Kayıt sırasında "Şirket adına kayıt oluyorum" işaretlendiyse true.</summary>
    public bool KurumsalHesap { get; set; }

    public string? FirmaAdi { get; set; }
    public string? FirmaTelefonu { get; set; }
    public string? YetkiliAdSoyad { get; set; }
    public string? YetkiliEmail { get; set; }

    /// <summary>Kayıt sırasında "Fatura bilgilerimi ekle" işaretlendiyse true.</summary>
    public bool FaturaBilgisiIstiyor { get; set; }

    public string? VergiDairesi { get; set; }

    /// <summary>Kurumsal hesapta Vergi Kimlik No (10 hane), bireysel hesapta TC Kimlik No (11 hane).</summary>
    public string? VergiKimlikNo { get; set; }
    public string? FaturaAdresi { get; set; }

    /// <summary>
    /// İsteğe bağlı firma logosu (PNG/JPEG). Yüklenirse Excel ve PDF keşif özeti dışa
    /// aktarımlarında DrawTally markasının yanında gösterilir — bkz. Profil.razor (yükleme UI'ı),
    /// Program.cs'teki "/profil/logo" uç noktası (önizleme) ve ExcelDisaAktarimServisi/PdfDisaAktarimServisi.
    /// </summary>
    public byte[]? FirmaLogoVerisi { get; set; }
    public string? FirmaLogoIcerikTuru { get; set; }

    /// <summary>"Şifremi unuttum" akışında üretilen 6 haneli kod (bkz. SifreSifirlamaServisi). E-posta
    /// doğrulama koduyla (<see cref="DogrulamaKodu"/>) karışmaması için ayrı bir alan.</summary>
    public string? SifreSifirlamaKodu { get; set; }
    public DateTime? SifreSifirlamaKoduSonGecerlilik { get; set; }

    /// <summary>
    /// Bu ay için kalan hakediş oluşturma kredisi — her yeni hakediş (dönem) oluşturma, projenin
    /// alanına göre hesaplanan bir miktar kredi harcar (bkz. UyelikServisi.HakedisKrediMaliyeti).
    /// Düzenleme/görüntüleme/export kredi harcamaz. Ay değiştiğinde VeriDeposu tarafından tembel
    /// (lazy) olarak plan limitine sıfırlanır — bkz. VeriDeposu.KrediyiGerekirseYenile.
    /// </summary>
    public int KalanHakedisKredisi { get; set; }

    /// <summary>Kredinin en son hangi dönem (yyyyMM, ör. 202609) için yenilendiği — ay değiştiğinde yeniden doldurulur.</summary>
    public int HakedisKredisiDonemi { get; set; }
}
