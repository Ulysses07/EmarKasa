using Kasa.ApiClient;
using Kasa.App.Converters;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Dispatching;

namespace Kasa.App.Core.Tests;

/// <summary>Picker ItemDisplayBinding'i derlenmiş bağlamayla (Aşama 4): XAML'de bağlamaya yazılan x:DataType (örn.
/// <c>{Binding Ad, x:DataType=api:AlisKanalDto}</c>) için MAUI XAML kaynak üreteci TypedBinding&lt;Öğe, Değer&gt; üretir; Picker
/// onu her öğeye ayrı ayrı uygulayıp metni okur. Burada üretilen koddaki biçimle kurulan bağlamanın gerçek MAUI Picker'ında
/// öğe metinlerini eskisi (yansımalı Binding) gibi verdiği sınanır.</summary>
public class DerlenmisOgeGosterimiTests
{
    private sealed class AnlikDispatcher : IDispatcher, IDispatcherProvider
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
        public IDispatcher? GetForCurrentThread() => this;
    }
    static DerlenmisOgeGosterimiTests() => DispatcherProvider.SetCurrent(new AnlikDispatcher());

    [Fact]
    public void Ozellik_yolu_derlenmis_ve_yansimali_baglamayla_ayni_metni_verir()
    {
        var kanallar = new List<AlisKanalDto> { new(1, "Mezat", true), new(2, "Perakende", false) };
        var derlenmis = new Picker
        {
            ItemsSource = kanallar,
            ItemDisplayBinding = new TypedBinding<AlisKanalDto, string>(s => (s.Ad, true), null, [new(static s => s, "Ad")]),
        };
        var yansimali = new Picker { ItemsSource = kanallar, ItemDisplayBinding = new Binding(nameof(AlisKanalDto.Ad)) };
        Assert.Equal(["Mezat", "Perakende"], derlenmis.Items);
        Assert.Equal(yansimali.Items, derlenmis.Items);
    }

    [Fact]
    public void Nokta_yolu_donusturucuyle_derlenmis_ve_yansimali_baglamayla_ayni_metni_verir()
    {
        var donemler = new List<DonemDto> { new(new(2026, 9, 1), new(2026, 9, 7), 2026, 9), new(new(2026, 9, 8), new(2026, 9, 14), 2026, 9) };
        var derlenmis = new Picker
        {
            ItemsSource = donemler,
            ItemDisplayBinding = new TypedBinding<DonemDto, DonemDto>(s => (s, true), null, []) { Converter = new DonemBicimConverter(), ConverterParameter = "yil" },
        };
        var yansimali = new Picker { ItemsSource = donemler, ItemDisplayBinding = new Binding(".", converter: new DonemBicimConverter(), converterParameter: "yil") };
        Assert.Equal(donemler.Select(d => Bicim.Donem(d, yilli: true)), derlenmis.Items);
        Assert.Equal(yansimali.Items, derlenmis.Items);
    }
}
