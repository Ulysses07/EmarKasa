using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>
/// Kart ızgarası (Kartlar ekranı, tasarım 2026-09-30 §2): <see cref="ItemsSource"/>'taki her <see cref="KartTakipSatiri"/> için
/// bir <see cref="KartKutusu"/>, sonunda "Yeni kart ekle" kutusu (<see cref="YeniGorunur"/>) ve en sonda <see cref="Ayrinti"/>.
/// Açık kutu açık kartın kimliğinden (<see cref="AcikKartId"/>) ya da yeni kart formundan (<see cref="YeniAcik"/>) gelir;
/// ayrıntı yalnız bir kutu açıkken görünür ve o kutunun satırının hemen altına tam genişlikte yerleşir. Satırdaki kutu sayısı
/// genişliğe göre değişir. Ölçüm ve yerleştirmenin hesabı KartIzgarasiHesabi'ndadır (Kasa.App.Core, sınanır); bu sınıf yalnız
/// çocukları ölçüp o hesabın dikdörtgenlerine yerleştirir.
/// </summary>
public class KartIzgarasi : Layout
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(KartIzgarasi),
        propertyChanged: (b, e, y) => ((KartIzgarasi)b).KaynakDegisti((IEnumerable?)e, (IEnumerable?)y));
    public static readonly BindableProperty SecCommandProperty = BindableProperty.Create(nameof(SecCommand), typeof(ICommand), typeof(KartIzgarasi));
    public static readonly BindableProperty YeniCommandProperty = BindableProperty.Create(nameof(YeniCommand), typeof(ICommand), typeof(KartIzgarasi));
    public static readonly BindableProperty YeniGorunurProperty = Durum(nameof(YeniGorunur), typeof(bool), false);
    public static readonly BindableProperty AcikKartIdProperty = Durum(nameof(AcikKartId), typeof(int?), null);
    public static readonly BindableProperty YeniAcikProperty = Durum(nameof(YeniAcik), typeof(bool), false);
    public static readonly BindableProperty AralikProperty = Durum(nameof(Aralik), typeof(double), 16d);
    public static readonly BindableProperty AyrintiProperty = BindableProperty.Create(nameof(Ayrinti), typeof(View), typeof(KartIzgarasi),
        propertyChanged: (b, e, y) => ((KartIzgarasi)b).AyrintiDegisti((View?)e, (View?)y));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>Kutuya dokunulunca kutunun satırıyla çalışır.</summary>
    public ICommand? SecCommand { get => (ICommand?)GetValue(SecCommandProperty); set => SetValue(SecCommandProperty, value); }
    /// <summary>"Yeni kart ekle" kutusuna dokunulunca çalışır.</summary>
    public ICommand? YeniCommand { get => (ICommand?)GetValue(YeniCommandProperty); set => SetValue(YeniCommandProperty, value); }
    public bool YeniGorunur { get => (bool)GetValue(YeniGorunurProperty); set => SetValue(YeniGorunurProperty, value); }
    public int? AcikKartId { get => (int?)GetValue(AcikKartIdProperty); set => SetValue(AcikKartIdProperty, value); }
    public bool YeniAcik { get => (bool)GetValue(YeniAcikProperty); set => SetValue(YeniAcikProperty, value); }
    /// <summary>Kutular ve satırlar arası boşluk (varsayılan 16).</summary>
    public double Aralik { get => (double)GetValue(AralikProperty); set => SetValue(AralikProperty, value); }
    public View? Ayrinti { get => (View?)GetValue(AyrintiProperty); set => SetValue(AyrintiProperty, value); }

    private readonly List<KartKutusu> _kutular = [];
    private readonly YeniKartKutusu _yeni = new();

    public KartIzgarasi()
    {
        _yeni.SetBinding(YeniKartKutusu.CommandProperty, new Binding(nameof(YeniCommand), source: this));
        Add(_yeni);
        Guncelle();
    }

    public IReadOnlyList<KartKutusu> Kutular => _kutular;
    public YeniKartKutusu YeniKutusu => _yeni;
    /// <summary>Açık kutunun sırası (kart kutuları, ardından "Yeni kart ekle"); açık kutu yoksa -1.</summary>
    public int AcikIndeks { get; private set; } = -1;

    private static BindableProperty Durum(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(KartIzgarasi), varsayilan, propertyChanged: (b, _, _) => ((KartIzgarasi)b).Guncelle());

    private void KaynakDegisti(IEnumerable? eski, IEnumerable? yeni)
    {
        if (eski is INotifyCollectionChanged e)
            e.CollectionChanged -= KaynakDegisti;
        if (yeni is INotifyCollectionChanged y)
            y.CollectionChanged += KaynakDegisti;
        KutulariKur();
    }

    private void KaynakDegisti(object? sender, NotifyCollectionChangedEventArgs e) => KutulariKur();

    /// <summary>Kutular kaynaktan yeniden kurulur (satır kayıtları değişmez: güncellenen kart yeni satırdır).</summary>
    private void KutulariKur()
    {
        foreach (var kutu in _kutular)
            Remove(kutu);
        _kutular.Clear();
        foreach (var satir in ItemsSource?.OfType<KartTakipSatiri>() ?? [])
        {
            var kutu = new KartKutusu { BindingContext = satir, CommandParameter = satir };
            kutu.SetBinding(KartKutusu.CommandProperty, new Binding(nameof(SecCommand), source: this));
            Insert(_kutular.Count, kutu);
            _kutular.Add(kutu);
        }
        Guncelle();
    }

    private void AyrintiDegisti(View? eski, View? yeni)
    {
        if (eski is not null)
            Remove(eski);
        if (yeni is not null)
            Add(yeni);
        Guncelle();
    }

    private void Guncelle()
    {
        _yeni.IsVisible = YeniGorunur;
        var kimlikler = _kutular.Select(k => k.Satir!.Veri.Id).ToList();
        AcikIndeks = KartIzgarasiHesabi.AcikIndeks(kimlikler, AcikKartId, YeniAcik && YeniGorunur);
        for (var i = 0; i < _kutular.Count; i++)
            _kutular[i].Secili = i == AcikIndeks;
        _yeni.Secili = AcikIndeks == _kutular.Count;
        if (Ayrinti is { } ayrinti)
            ayrinti.IsVisible = AcikIndeks >= 0;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager() => new Yerlestirici(this);

    /// <summary>
    /// Çocukları ölçüp KartIzgarasiHesabi'nın dikdörtgenlerine yerleştirir. Görünmeyen "Yeni kart ekle" kutusu yer tutmaz; açık
    /// kutunun sırası görünen kutular arasındadır (yeni kart formu yalnız kutu görünürken açık sayılır).
    /// Ölçüm sözleşmesi: Hesapla'ya giden kutu yükseklikleri, yerleşimin kutu genişliğiyle ölçülmüş olmalıdır (kutunun
    /// yüksekliği genişliğine bağlıdır: metin sarımı). Ölçümde kutular hesabın o genişlik için verdiği kutu genişliğiyle, ayrıntı
    /// tam genişlikle ölçülür. Sonsuz genişlik kısıtında son yerleşim genişliği (yoksa tek kutu genişliği) kullanılır.
    /// Yerleştirme genişliği ölçümdekinden başka bir kutu genişliği veriyorsa çocuklar yerleştirmeden önce o genişlikle yeniden
    /// ölçülür; toplam yükseklik değişmişse ızgaranın ölçüsü geçersiz kılınır (aynı genişlikte yinelenmez, döngü kurulmaz).
    /// </summary>
    private sealed class Yerlestirici(KartIzgarasi izgara) : LayoutManager(izgara)
    {
        private double _kutuGenisligi = double.NaN, _ayrintiGenisligi = double.NaN, _sonGenislik = double.NaN, _olculenYukseklik;

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var ic = double.IsFinite(widthConstraint) ? widthConstraint - izgara.Padding.HorizontalThickness : _sonGenislik;
            var (_, genislik, kutuGenisligi) = KartIzgarasiHesabi.Olcu(ic, izgara.Aralik);
            CocuklariOlc(kutuGenisligi, genislik);
            var yerlesim = Hesapla(ic);
            _olculenYukseklik = yerlesim.Yukseklik + izgara.Padding.VerticalThickness;
            return new Size(yerlesim.Genislik + izgara.Padding.HorizontalThickness, _olculenYukseklik);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var ic = bounds.Width - izgara.Padding.HorizontalThickness;
            var (_, genislik, kutuGenisligi) = KartIzgarasiHesabi.Olcu(ic, izgara.Aralik);
            var yeniden = kutuGenisligi != _kutuGenisligi || genislik != _ayrintiGenisligi;
            if (yeniden)
                CocuklariOlc(kutuGenisligi, genislik);
            var yerlesim = Hesapla(ic);
            var x = bounds.X + izgara.Padding.Left;
            var y = bounds.Y + izgara.Padding.Top;
            var kutular = GorunenKutular();
            for (var i = 0; i < kutular.Count; i++)
                kutular[i].Arrange(Dortgen(yerlesim.Kutular[i], x, y));
            if (AcikAyrinti() is { } ayrinti && yerlesim.Ayrinti is { } alan)
                ayrinti.Arrange(Dortgen(alan, x, y));
            var yukseklik = yerlesim.Yukseklik + izgara.Padding.VerticalThickness;
            if (yeniden && yukseklik != _olculenYukseklik && ic != _sonGenislik)
                izgara.InvalidateMeasure();
            _sonGenislik = ic;
            return new Size(bounds.Width, yukseklik);
        }

        private List<IView> GorunenKutular()
            => izgara.Where(v => !ReferenceEquals(v, izgara.Ayrinti) && v.Visibility != Visibility.Collapsed).ToList();

        private IView? AcikAyrinti()
            => izgara.AcikIndeks >= 0 && izgara.Ayrinti is IView ayrinti && ayrinti.Visibility != Visibility.Collapsed ? ayrinti : null;

        /// <summary>Görünen kutuları kutu genişliğiyle, açık ayrıntıyı tam genişlikle ölçer ve bu genişlikleri hatırlar.</summary>
        private void CocuklariOlc(double kutuGenisligi, double genislik)
        {
            foreach (var kutu in GorunenKutular())
                kutu.Measure(kutuGenisligi, double.PositiveInfinity);
            AcikAyrinti()?.Measure(genislik, double.PositiveInfinity);
            _kutuGenisligi = kutuGenisligi;
            _ayrintiGenisligi = genislik;
        }

        /// <summary>Son ölçülmüş yüksekliklerle (DesiredSize) yerleşim.</summary>
        private KartIzgarasiYerlesimi Hesapla(double ic)
            => KartIzgarasiHesabi.Hesapla(ic, izgara.Aralik, GorunenKutular().Select(k => k.DesiredSize.Height).ToList(), izgara.AcikIndeks,
                AcikAyrinti()?.DesiredSize.Height ?? 0);

        private static Rect Dortgen(Dikdortgen d, double x, double y) => new(x + d.X, y + d.Y, d.Genislik, d.Yukseklik);
    }
}
