using Kasa.App.Controls;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Toplu çek girişi formu (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md): ortak bilgiler, seri, Excel'den yapıştırma ve
/// satırlar. Satırın hatası kendi altındadır; genel hata formun en üstündedir.</summary>
public sealed partial class CekTakipPage
{
    private static Button IkincilDugme(string metin, string komut)
    {
        var d = Dugme(metin, komut);
        d.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        return d;
    }

    private static Label AltBaslik(string metin) => new() { Text = metin, FontAttributes = FontAttributes.Bold };

    private static View TopluForm(CekTakipViewModel vm, View hataKutusu)
    {
        const string h = nameof(vm.TopluHatalari);
        var kisi = Alan("Kişi", Girdi(nameof(vm.TopluKisi)), h, nameof(vm.TopluKisi));
        kisi.SetBinding(FormAlani.BaslikProperty, nameof(vm.TopluKisiBasligi));
        var yapistir = new Microsoft.Maui.Controls.Editor { HeightRequest = 120, Placeholder = "12345\tMehmet Ticaret\t10000,00\t31.01.2027" };
        yapistir.SetBinding(Microsoft.Maui.Controls.Editor.TextProperty, nameof(vm.Yapistirilan));
        var ayni = Goster(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { BagliHata(nameof(vm.TopluAyniUyarisi)), Dugme("Yine de kaydet", nameof(vm.TopluYineDeKaydetCommand)) },
        }, nameof(vm.TopluAyniVar));
        return Kart("Toplu çek girişi", hataKutusu,
            Metin("Ortak bilgiler bütün satırlara yazılır. Hatalı satır varsa hiçbiri kaydedilmez."),
            Alan("Yön", Secim(nameof(vm.YonSecenekleri), nameof(vm.TopluYon))), Alan("Tür", Secim(nameof(vm.TurSecenekleri), nameof(vm.TopluTur))),
            Alan("Banka (senette boş olabilir)", Girdi(nameof(vm.TopluBanka)), h, nameof(vm.TopluBanka)),
            Goster(Alan("Ödeneceği kasa", Secim(nameof(vm.CekKasaSecenekleri), nameof(vm.TopluKasa), "."), h, nameof(vm.TopluKasa)), nameof(vm.TopluVerilen)),
            kisi, Alan("Not", Girdi(nameof(vm.TopluNot))),
            AltBaslik("Seri ekle"), Metin("Numara birer artar, vade seçilen ay aralığıyla ilerler."), SeriAlanlari(vm, h),
            IkincilDugme("Seriyi ekle", nameof(vm.SeriEkleCommand)),
            AltBaslik("Excel'den yapıştır"), Metin("Sütunlar: Numara, Kişi, Tutar, Vade. Kişi sütunu yoksa yukarıdaki kişi yazılır."),
            Alan("Yapıştırılacak satırlar", yapistir, h, nameof(vm.Yapistirilan)), IkincilDugme("Yapıştırılanı ekle", nameof(vm.YapistirilaniEkleCommand)),
            AltBaslik("Satırlar"), TopluSatirListesi(vm), IkincilDugme("Boş satır ekle", nameof(vm.TopluSatirEkleCommand)),
            BagliBuyuk(nameof(vm.TopluToplam)), ayni,
            new UyumluSatir { Spacing = 12, Children = { Dugme("Hepsini kaydet", nameof(vm.TopluKaydetCommand)), IkincilDugme("Vazgeç", nameof(vm.TopluKapatCommand)) } });
    }

    private static UyumluIzgara SeriAlanlari(CekTakipViewModel vm, string h)
    {
        var izgara = new UyumluIzgara
        {
            ColumnSpacing = 10,
            RowSpacing = 8,
            ColumnDefinitions = { new(new GridLength(80)), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(new GridLength(120)) },
        };
        izgara.Add(Alan("Adet", Girdi(nameof(vm.SeriAdet), sayi: true), h, nameof(vm.SeriAdet)));
        izgara.Add(Alan("İlk numara", Girdi(nameof(vm.SeriIlkNo)), h, nameof(vm.SeriIlkNo)), 1);
        izgara.Add(Alan("Her birinin tutarı", Girdi(nameof(vm.SeriTutar), para: true), h, nameof(vm.SeriTutar)), 2);
        izgara.Add(Alan("İlk vade", Tarih(nameof(vm.SeriIlkVade))), 3);
        izgara.Add(Alan("Kaç ayda bir", Girdi(nameof(vm.SeriAralik), sayi: true), h, nameof(vm.SeriAralik)), 4);
        return izgara;
    }

    private static VerticalStackLayout TopluSatirListesi(CekTakipViewModel vm)
    {
        var liste = new VerticalStackLayout { Spacing = 10 };
        liste.SetBinding(BindableLayout.ItemsSourceProperty, nameof(vm.TopluSatirlar));
        BindableLayout.SetEmptyView(liste, Metin("Henüz satır yok."));
        BindableLayout.SetItemTemplate(liste, new DataTemplate(() =>
        {
            var izgara = new UyumluIzgara
            {
                ColumnSpacing = 10,
                RowSpacing = 8,
                ColumnDefinitions =
                {
                    new(new GridLength(64)), new(GridLength.Star), new(new GridLength(1.6, GridUnitType.Star)), new(GridLength.Star),
                    new(new GridLength(150)), new(GridLength.Auto),
                },
            };
            var sira = Bagli(nameof(CekTopluSatiri.Baslik));
            sira.VerticalOptions = LayoutOptions.End;
            izgara.Add(sira);
            izgara.Add(Alan("Numara", Girdi(nameof(CekTopluSatiri.No))), 1);
            izgara.Add(Alan("Kişi", Girdi(nameof(CekTopluSatiri.Kisi))), 2);
            izgara.Add(Alan("Tutar", Girdi(nameof(CekTopluSatiri.Tutar), para: true)), 3);
            izgara.Add(Alan("Vade", Tarih(nameof(CekTopluSatiri.Vade))), 4);
            var sil = new Button { Text = "Kaldır", VerticalOptions = LayoutOptions.End, HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
            sil.SetBinding(SemanticProperties.DescriptionProperty, nameof(CekTopluSatiri.Baslik), stringFormat: "{0} kaldır");
            sil.Clicked += (_, _) =>
            {
                if (sil.BindingContext is CekTopluSatiri s)
                    vm.TopluSatirSilCommand.Execute(s);
            };
            izgara.Add(sil, 5);
            var hata = BagliHata(nameof(CekTopluSatiri.Hata));
            hata.SetBinding(IsVisibleProperty, nameof(CekTopluSatiri.Hata), converter: new Converters.DoluIseConverter());
            var ayirici = new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] };
            return new VerticalStackLayout { Spacing = 6, Children = { izgara, hata, ayirici } };
        }));
        return liste;
    }
}
