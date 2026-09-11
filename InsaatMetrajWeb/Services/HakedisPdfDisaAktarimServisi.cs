using InsaatMetrajWeb.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InsaatMetrajWeb.Services;

/// <summary>Bir hakedişi .pdf olarak döker (bkz. Program.cs'teki "/proje/{id}/hakedis/{hakedisId}/pdf" uç noktası).</summary>
public static class HakedisPdfDisaAktarimServisi
{
    private static readonly string KoyuZemin = "#14181A";
    private static readonly string Vurgu = "#5AA9DE";
    private static readonly string TabloBasligi = "#1F2937";

    public static byte[] HakedisiPdfOlarakOlustur(Proje proje, Hakedis hakedis, byte[]? firmaLogosu = null)
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
                        if (firmaLogosu is { Length: > 0 })
                        {
                            satir.ConstantItem(70).Height(28).Image(firmaLogosu).FitArea();
                        }
                        satir.RelativeItem().AlignRight().Text("by Drongos Global").FontSize(9).FontColor("#97A3BF");
                    });
                    baslik.Item().PaddingTop(10).Text(proje.Ad).FontSize(15).Bold();
                    baslik.Item().Text($"{hakedis.HakedisNo}. Hakediş — {hakedis.Tarih:dd.MM.yyyy}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                sayfa.Content().PaddingTop(12).Column(icerik =>
                {
                    icerik.Item().Table(tablo =>
                    {
                        tablo.ColumnsDefinition(sutunlar =>
                        {
                            sutunlar.RelativeColumn(1.2f);
                            sutunlar.RelativeColumn(3.2f);
                            sutunlar.RelativeColumn(1f);
                            sutunlar.RelativeColumn(1f);
                            sutunlar.RelativeColumn(1f);
                            sutunlar.RelativeColumn(1.3f);
                            sutunlar.RelativeColumn(1.3f);
                        });

                        tablo.Header(header =>
                        {
                            string[] basliklar = { "Poz Kodu", "Poz Adı", "Önceki %", "Bu Hak. %", "Bu Dönem %", "Bu Dönem Tutar", "Kümülatif Tutar" };
                            foreach (var b in basliklar)
                            {
                                header.Cell().Background(TabloBasligi).Padding(5)
                                    .Text(b).FontColor(Colors.White).Bold().FontSize(7.5f);
                            }
                        });

                        var satirNo = 0;
                        foreach (var k in hakedis.Kalemler)
                        {
                            var zemin = satirNo % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.MetrajKalemi.Poz.PozKodu);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(k.MetrajKalemi.Poz.Ad);
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"%{k.OncekiKumulatifYuzde:0.##}");
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"%{k.KumulatifYuzde:0.##}");
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"%{k.BuDonemYuzde():0.##}");
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(k.BuDonemTutar().ToString("#,##0.00"));
                            tablo.Cell().Background(zemin).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(k.KumulatifTutar().ToString("#,##0.00"));
                            satirNo++;
                        }
                    });

                    icerik.Item().PaddingTop(14).AlignRight().Column(ozet =>
                    {
                        void OzetSatiri(string etiket, decimal tutar, bool vurgulu = false)
                        {
                            ozet.Item().Row(satir =>
                            {
                                var etiketMetin = satir.AutoItem().Text(etiket + ": ").FontSize(vurgulu ? 11 : 9.5f);
                                var tutarMetin = satir.AutoItem().Text(tutar.ToString("#,##0.00") + " ₺").FontSize(vurgulu ? 11 : 9.5f)
                                    .FontColor(vurgulu ? Vurgu : Colors.Black);
                                if (vurgulu)
                                {
                                    etiketMetin.Bold();
                                    tutarMetin.Bold();
                                }
                            });
                        }

                        OzetSatiri("Bu Dönem İmalat Tutarı", hakedis.BuDonemImalatTutari());
                        OzetSatiri("Fiyat Farkı", hakedis.FiyatFarkiTutari);
                        OzetSatiri("Brüt Hakediş Tutarı", hakedis.BrutHakedisTutari());
                        OzetSatiri($"KDV (%{hakedis.KdvOrani:0.##})", hakedis.KdvTutari());
                        OzetSatiri($"Teminat Kesintisi (%{hakedis.TeminatOrani:0.##})", -hakedis.TeminatKesintisi());
                        OzetSatiri($"Stopaj Kesintisi (%{hakedis.StopajOrani:0.##})", -hakedis.StopajKesintisi());
                        OzetSatiri($"Avans Mahsubu (%{hakedis.AvansOrani:0.##})", -hakedis.AvansMahsubu());
                        ozet.Item().PaddingTop(4).Text("");
                        OzetSatiri("NET ÖDENECEK TUTAR", hakedis.NetOdenecekTutar(), vurgulu: true);
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
