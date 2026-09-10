using InsaatMetrajWeb.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bir projenin keşif/metraj tablosunu .pdf olarak döker (bkz. Program.cs'teki "/proje/{id}/pdf"
/// uç noktası). QuestPDF Community lisansı — yıllık geliri belirli bir eşiğin altındaki
/// kullanım için ücretsizdir (bkz. Program.cs'teki QuestPDF.Settings.License ataması).
/// </summary>
public static class PdfDisaAktarimServisi
{
    private static readonly string KoyuZemin = "#0B1220";
    private static readonly string Vurgu = "#F97316";
    private static readonly string TabloBasligi = "#1F2937";

    public static byte[] ProjeyiPdfOlarakOlustur(Proje proje)
    {
        var belge = Document.Create(container =>
        {
            container.Page(sayfa =>
            {
                sayfa.Size(PageSizes.A4);
                sayfa.Margin(28);
                sayfa.DefaultTextStyle(x => x.FontSize(9));

                sayfa.Header().Column(baslik =>
                {
                    baslik.Item().Background(KoyuZemin).Padding(10).Row(satir =>
                    {
                        satir.RelativeItem().Text("DrawTally").FontSize(16).Bold().FontColor(Vurgu);
                        satir.RelativeItem().AlignRight().Text("by Drongos Global").FontSize(9).FontColor("#97A3BF");
                    });
                    baslik.Item().PaddingTop(10).Text(proje.Ad).FontSize(15).Bold();
                    baslik.Item().Text($"Keşif Özeti — {DateTime.Now:dd.MM.yyyy}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                sayfa.Content().PaddingTop(12).Column(icerik =>
                {
                    icerik.Item().Table(tablo =>
                    {
                        tablo.ColumnsDefinition(sutunlar =>
                        {
                            sutunlar.RelativeColumn(1.3f);
                            sutunlar.RelativeColumn(1.1f);
                            sutunlar.RelativeColumn(3f);
                            sutunlar.RelativeColumn(1.6f);
                            sutunlar.RelativeColumn(0.7f);
                            sutunlar.RelativeColumn(0.9f);
                            sutunlar.RelativeColumn(1.1f);
                            sutunlar.RelativeColumn(1.1f);
                        });

                        tablo.Header(header =>
                        {
                            string[] basliklar = { "Disiplin", "Poz Kodu", "Poz Adı", "Ölçüm Detayı", "Birim", "Miktar", "Birim Fiyat", "Tutar" };
                            foreach (var b in basliklar)
                            {
                                header.Cell().Background(TabloBasligi).Padding(5)
                                    .Text(b).FontColor(Colors.White).Bold().FontSize(7.5f);
                            }
                        });

                        var satirNo = 0;
                        foreach (var k in proje.MetrajKalemleri)
                        {
                            var zemin = satirNo % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(DisiplinTespitServisi.GosterimAdi(k.Disiplin));
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.Poz.PozKodu);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.Poz.Ad);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.OlcumDetayi);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.Poz.Birim);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(k.Miktar.ToString("#,##0.000"));
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(k.Poz.BirimMaliyet().ToString("#,##0.00"));
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(k.ToplamMaliyet().ToString("#,##0.00"));
                            satirNo++;
                        }
                    });

                    icerik.Item().PaddingTop(10).AlignRight().Row(satir =>
                    {
                        satir.AutoItem().Text("GENEL TOPLAM: ").Bold().FontSize(11);
                        satir.AutoItem().Text(proje.ToplamMaliyet().ToString("#,##0.00") + " ₺").Bold().FontSize(11).FontColor(Vurgu);
                    });
                });

                sayfa.Footer().AlignCenter().Text(metin =>
                {
                    metin.Span("DrawTally ile oluşturuldu — Sayfa ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    metin.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                    metin.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    metin.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return belge.GeneratePdf();
    }
}
