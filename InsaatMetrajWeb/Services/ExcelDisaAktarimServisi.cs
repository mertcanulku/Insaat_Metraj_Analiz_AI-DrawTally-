using ClosedXML.Excel;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>Bir projenin keşif/metraj tablosunu .xlsx dosyasına döker (bkz. Program.cs'teki "/proje/{id}/excel" uç noktası).</summary>
public static class ExcelDisaAktarimServisi
{
    private const int SutunSayisi = 8;

    /// <param name="logoBasligiEkle">
    /// true ise dosyanın en üstüne markayı gösteren hafif bir başlık bandı eklenir (gerçek bir logo
    /// resmi değil — SVG marka varlıklarını xlsx'e gömmek ek bir rasterleştirme kütüphanesi gerektirir,
    /// bu yüzden aynı marka renkleriyle stilize edilmiş bir metin bandı kullanılıyor). Kullanıcı bunu
    /// dışa aktarmadan önce bir onay kutusuyla açıp kapatabiliyor (bkz. ProjeDetay.razor / Home.razor).
    /// </param>
    public static byte[] ProjeyiXlsxOlarakOlustur(Proje proje, bool logoBasligiEkle = true)
    {
        using var workbook = new XLWorkbook();
        var sayfa = workbook.Worksheets.Add("Keşif Özeti");

        var baslikSatiri = 1;
        if (logoBasligiEkle)
        {
            baslikSatiri = MarkaBasligiEkle(sayfa, proje.Ad);
        }

        string[] basliklar = { "Disiplin", "Poz Kodu", "Poz Adı", "Ölçüm Detayı", "Birim", "Miktar", "Birim Fiyat", "Tutar" };
        for (var i = 0; i < basliklar.Length; i++)
        {
            var hucre = sayfa.Cell(baslikSatiri, i + 1);
            hucre.Value = basliklar[i];
            hucre.Style.Font.Bold = true;
            hucre.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2937");
            hucre.Style.Font.FontColor = XLColor.White;
        }

        var satir = baslikSatiri + 1;
        foreach (var k in proje.MetrajKalemleri)
        {
            sayfa.Cell(satir, 1).Value = DisiplinTespitServisi.GosterimAdi(k.Disiplin);
            sayfa.Cell(satir, 2).Value = k.Poz.PozKodu;
            sayfa.Cell(satir, 3).Value = k.Poz.Ad;
            sayfa.Cell(satir, 4).Value = k.OlcumDetayi;
            sayfa.Cell(satir, 5).Value = k.Poz.Birim;
            sayfa.Cell(satir, 6).Value = k.Miktar;
            sayfa.Cell(satir, 7).Value = k.Poz.BirimMaliyet();
            sayfa.Cell(satir, 8).Value = k.ToplamMaliyet();
            satir++;
        }

        var sonSatir = Math.Max(satir - 1, baslikSatiri + 1);
        sayfa.Range(baslikSatiri + 1, 6, sonSatir, 6).Style.NumberFormat.Format = "#,##0.000";
        sayfa.Range(baslikSatiri + 1, 7, sonSatir, 8).Style.NumberFormat.Format = "#,##0.00";

        sayfa.Cell(satir + 1, 7).Value = "GENEL TOPLAM";
        sayfa.Cell(satir + 1, 7).Style.Font.Bold = true;
        var toplamHucre = sayfa.Cell(satir + 1, 8);
        toplamHucre.Value = proje.ToplamMaliyet();
        toplamHucre.Style.Font.Bold = true;
        toplamHucre.Style.NumberFormat.Format = "#,##0.00";

        sayfa.Row(baslikSatiri).Height = 20;
        sayfa.SheetView.FreezeRows(baslikSatiri);
        sayfa.Columns().AdjustToContents();

        using var akis = new MemoryStream();
        workbook.SaveAs(akis);
        return akis.ToArray();
    }

    /// <summary>İki satırlık marka bandı (koyu zeminde "DrawTally" + proje adı) ekler, tablo başlığının hangi satırdan başlaması gerektiğini döner.</summary>
    private static int MarkaBasligiEkle(IXLWorksheet sayfa, string projeAdi)
    {
        var markaSatiri = sayfa.Range(1, 1, 1, SutunSayisi).Merge();
        markaSatiri.Value = "DrawTally  ·  by Drongos Global";
        markaSatiri.Style.Font.Bold = true;
        markaSatiri.Style.Font.FontSize = 13;
        markaSatiri.Style.Font.FontColor = XLColor.FromHtml("#F97316");
        markaSatiri.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B1220");
        markaSatiri.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        markaSatiri.Style.Alignment.Indent = 1;
        sayfa.Row(1).Height = 24;

        var projeSatiri = sayfa.Range(2, 1, 2, SutunSayisi).Merge();
        projeSatiri.Value = $"{projeAdi} — Keşif Özeti";
        projeSatiri.Style.Font.Bold = true;
        projeSatiri.Style.Font.FontSize = 11;
        projeSatiri.Style.Font.FontColor = XLColor.FromHtml("#F1F5F9");
        projeSatiri.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2440");
        projeSatiri.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        projeSatiri.Style.Alignment.Indent = 1;
        sayfa.Row(2).Height = 20;

        return 3;
    }
}
