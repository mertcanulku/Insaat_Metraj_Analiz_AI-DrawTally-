namespace InsaatMetrajWeb.Models;

/// <summary>Bir ay için TÜİK Yİ-ÜFE endeks değeri — bkz. Data/EndeksDonemiKaydi.cs.</summary>
public class EndeksDonemi
{
    public int Id { get; set; }
    public int Yil { get; set; }
    public int Ay { get; set; }
    public decimal TufeYiUfeDegeri { get; set; }
    public decimal? BakanlikKatsayisi { get; set; }

    public string DonemMetni() => $"{Yil}-{Ay:00}";
}
