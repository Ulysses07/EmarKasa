using System.Globalization;
using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>
/// Entry.Text ↔ decimal iki yönlü. Ayrıştırma Türkçe kurallarla <see cref="ParaGiris"/>'te yapılır
/// ("1.500" = 1500, "1.500,50" = 1500,5, "12.5" = 12,5). Geçersiz ya da negatif giriş bağlı değeri
/// DEĞİŞTİRMEZ (Binding.DoNothing); kutu kırmızıya döner (bkz. <see cref="ParaGirisDogrulama"/>).
/// Boş = 0.
/// </summary>
public sealed class ParaGirisConverter : IValueConverter
{
    // İki yönlü bağlamada kaynak değişince MAUI hedefi hemen yeniden yazar. Kullanıcının yazdığı
    // metin aynı tutarı temsil ediyorsa onu geri ver; yoksa "1.5" yazarken kutu "1,5"e dönüşür ve
    // "1.500" hiç yazılamaz.
    [ThreadStatic] private static string? _sonMetin;
    [ThreadStatic] private static decimal _sonTutar;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value is decimal m ? m : 0m;
        if (_sonMetin is { } metin && d == _sonTutar)
        {
            _sonMetin = null;
            return metin;
        }
        return ParaGiris.Bicimle(d);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sonuc = ParaGiris.Ayristir(value as string);
        if (!sonuc.Gecerli)
        {
            _sonMetin = null;
            return Binding.DoNothing;   // kaynak değişmez; eski/geçerli değer korunur
        }
        _sonMetin = value as string ?? string.Empty;
        _sonTutar = sonuc.Tutar;
        return sonuc.Tutar;
    }
}
