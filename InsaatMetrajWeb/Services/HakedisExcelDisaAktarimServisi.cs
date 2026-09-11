using ClosedXML.Excel;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>Bir hakedişi .xlsx dosyasına döker (bkz. Program.cs'teki "/proje/{id}/hakedis/{hakedisId}/excel" uç noktası).</summary>
public static class HakedisExcelDisaAktarimServisi
{
    private const int SutunSayisi = 7;

    public static byte[] HakedisiXlsxOlarakOlustur(Proje proje, Hakedis hakedis, byte[]? firmaLogosu = null)
    {
        using var workbook = new XLWorkbook();
        var sayfa = workbook.Worksheets.Add($"{hakedis.HakedisNo}. Hakediş");

        var markaSatiri = sayfa.Range(1, 1, 1, SutunSayisi).Merge();
        markaSatiri.Value = "DrawTally  ·  by Drongos Global";
        markaSatiri.Style.Font.Bold = true;
        markaSatiri.Style.Font.FontSize = 13;
        markaSatiri.Style.Font.FontColor = XLColor.FromHtml("#5AA9DE");
        markaSatiri.Style.Fill.BackgroundColor = XLColor.FromHtml("#14181A");
        markaSatiri.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        markaSatiri.Style.Alignment.Indent = 1;
        sayfa.Row(1).Height = 24;

        var projeSatiri = sayfa.Range(2, 1, 2, SutunSayisi).Merge();
        projeSatiri.Value = $"{proje.Ad} — {hakedis.HakedisNo}. Hakediş ({hakedis.Tarih:dd.MM.yyyy})";
        projeSatiri.Style.Font.Bold = true;
        projeSatiri.Style.Font.FontSize = 11;
        projeSatiri.Style.Font.FontColor = XLColor.FromHtml("#F5F5F7");
        projeSatiri.Style.Fill.BackgroundColor = XLColor.FromHtml("#1C1C1E");
        projeSatiri.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        projeSatiri.Style.Alignment.Indent = 1;
        sayfa.Row(2).Height = 20;

        if (firmaLogosu is { Length: > 0 })
        {
            using var akis = new MemoryStream(firmaLogosu);
            sayfa.AddPicture(akis).WithSize(90, 30).MoveTo(sayfa.Cell(1, SutunSayisi));
        }

        const int baslikSatiri = 3;
        string[] basliklar = { "Poz Kodu", "Poz Adı", "Önceki %", "Bu Hakediş %", "Bu Dönem %", "Bu Dönem Tutar", "Kümülatif Tutar" };
        for (var i = 0; i < basliklar.Length; i++)
        {
            var hucre = sayfa.Cell(baslikSatiri, i + 1);
            hucre.Value = basliklar[i];
            hucre.Style.Font.Bold = true;
            hucre.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2937");
            hucre.Style.Font.FontColor = XLColor.White;
        }

        var satir = baslikSatiri + 1;
        foreach (var k in hakedis.Kalemler)
        {
            sayfa.Cell(satir, 1).Value = k.MetrajKalemi.Poz.PozKodu;
            sayfa.Cell(satir, 2).Value = k.MetrajKalemi.Poz.Ad;
            sayfa.Cell(satir, 3).Value = k.OncekiKumulatifYuzde;
            sayfa.Cell(satir, 4).Value = k.KumulatifYuzde;
            sayfa.Cell(satir, 5).Value = k.BuDonemYuzde();
            sayfa.Cell(satir, 6).Value = k.BuDonemTutar();
            sayfa.Cell(satir, 7).Value = k.KumulatifTutar();
            satir++;
        }

        var sonSatir = Math.Max(satir - 1, baslikSatiri + 1);
        sayfa.Range(baslikSatiri + 1, 3, sonSatir, 5).Style.NumberFormat.Format = "0.00\"%\"";
        sayfa.Range(baslikSatiri + 1, 6, sonSatir, 7).Style.NumberFormat.Format = "#,##0.00";

        var ozetSatiri = satir + 1;
        void OzetEkle(string etiket, decimal tutar, bool vurgulu = false)
        {
            var etiketHucre = sayfa.Cell(ozetSatiri, 6);
            etiketHucre.Value = etiket;
            etiketHucre.Style.Font.Bold = true;
            var tutarHucre = sayfa.Cell(ozetSatiri, 7);
            tutarHucre.Value = tutar;
            tutarHucre.Style.NumberFormat.Format = "#,##0.00";
            tutarHucre.Style.Font.Bold = true;
            if (vurgulu)
            {
                etiketHucre.Style.Font.FontColor = XLColor.FromHtml("#5AA9DE");
                tutarHucre.Style.Font.FontColor = XLColor.FromHtml("#5AA9DE");
            }
            ozetSatiri++;
        }

        OzetEkle("Bu Dönem İmalat Tutarı", hakedis.BuDonemImalatTutari());
        OzetEkle("Fiyat Farkı", hakedis.FiyatFarkiTutari);
        OzetEkle("Brüt Hakediş Tutarı", hakedis.BrutHakedisTutari());
        OzetEkle($"KDV (%{hakedis.KdvOrani:0.##})", hakedis.KdvTutari());
        OzetEkle($"Teminat Kesintisi (%{hakedis.TeminatOrani:0.##})", -hakedis.TeminatKesintisi());
        OzetEkle($"Stopaj Kesintisi (%{hakedis.StopajOrani:0.##})", -hakedis.StopajKesintisi());
        OzetEkle($"Avans Mahsubu (%{hakedis.AvansOrani:0.##})", -hakedis.AvansMahsubu());
        OzetEkle("NET ÖDENECEK TUTAR", hakedis.NetOdenecekTutar(), vurgulu: true);

        sayfa.Row(baslikSatiri).Height = 20;
        sayfa.SheetView.FreezeRows(baslikSatiri);
        sayfa.Columns().AdjustToContents();

        using var cikisAkis = new MemoryStream();
        workbook.SaveAs(cikisAkis);
        return cikisAkis.ToArray();
    }
}
