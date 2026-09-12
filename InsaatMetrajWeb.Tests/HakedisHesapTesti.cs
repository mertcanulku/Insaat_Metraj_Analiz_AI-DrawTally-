using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Tests;

/// <summary>Hakedis.cs içindeki brüt/kesinti/net hesap mantığını (bkz. Models/Hakedis.cs) doğrular.</summary>
public class HakedisHesapTesti
{
    private static Hakedis OrnekHakedisOlustur(decimal kumulatifYuzde = 100)
    {
        var poz = new Poz { Id = 1, PozKodu = "15.150.1001", Ad = "Beton", Birim = "m3", ResmiFiyat = 1000m };
        var metrajKalemi = new MetrajKalemi { Id = 1, Poz = poz, OlcumDetayi = "Test", Miktar = 10 };

        return new Hakedis
        {
            HakedisNo = 1,
            Tarih = new DateOnly(2026, 9, 1),
            Kalemler = new List<HakedisKalemi>
            {
                new() { MetrajKalemi = metrajKalemi, OncekiKumulatifYuzde = 0, KumulatifYuzde = kumulatifYuzde }
            }
        };
    }

    [Fact]
    public void SgkKesintisi_BrutHakedisTutariUzerindenOraniUygular()
    {
        var hakedis = OrnekHakedisOlustur();
        hakedis.SgkKesintiOrani = 5;

        // BuDonemImalatTutari = 10 m3 * 1000 ₺ = 10.000 ₺, fiyat farkı yok -> BrutHakedisTutari = 10.000 ₺
        Assert.Equal(10_000m, hakedis.BrutHakedisTutari());
        Assert.Equal(500m, hakedis.SgkKesintisi());
    }

    [Fact]
    public void SgkKesintiOraniSifirsa_KesintiSifirOlur()
    {
        var hakedis = OrnekHakedisOlustur();
        hakedis.SgkKesintiOrani = 0;

        Assert.Equal(0m, hakedis.SgkKesintisi());
    }

    [Fact]
    public void NetOdenecekTutar_SgkKesintisiniDusuklerAsindaDikkateAlir()
    {
        var hakedis = OrnekHakedisOlustur();
        hakedis.KdvOrani = 20;
        hakedis.TeminatOrani = 10;
        hakedis.StopajOrani = 1;
        hakedis.SgkKesintiOrani = 5;
        hakedis.AvansOrani = 0;

        // Brüt: 10.000 ₺. KDV: +2.000. Teminat: -1.000. Stopaj: -100. SGK: -500. Avans: 0.
        var beklenenNet = 10_000m + 2_000m - 1_000m - 100m - 500m - 0m;
        Assert.Equal(beklenenNet, hakedis.NetOdenecekTutar());
    }

    [Fact]
    public void NetOdenecekTutar_SgkKesintisiOlmadanOncekiDavranisiKorur()
    {
        // Migration sonrası eski kayıtlarda SgkKesintiOrani varsayılan olarak 0 gelir —
        // bu durumda net tutar SGK eklenmeden önceki değerle aynı olmalı.
        var hakedis = OrnekHakedisOlustur();
        hakedis.KdvOrani = 20;
        hakedis.TeminatOrani = 10;
        hakedis.StopajOrani = 1;
        hakedis.AvansOrani = 2;

        var beklenenNet = hakedis.BrutHakedisTutari() + hakedis.KdvTutari()
            - hakedis.TeminatKesintisi() - hakedis.StopajKesintisi() - hakedis.AvansMahsubu();

        Assert.Equal(beklenenNet, hakedis.NetOdenecekTutar());
        Assert.Equal(0m, hakedis.SgkKesintisi());
    }
}
