using Kasa.ApiClient;
using Kasa.App.Core;
using Kasa.Core.Kodlar;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

public sealed partial class EkstreAktarmaPage
{
    private readonly ContentView _kuralFormu = new();
    private void KurallariKur()
    {
        var picker = new Picker { Title = "Düzenlenecek kural", ItemDisplayBinding = new Binding(nameof(EkstreKuralDto.Ad)) };
        picker.SetBinding(Picker.ItemsSourceProperty, nameof(Vm.Kurallar));
        Govde.Add(Editor(Kart("Kişisel sınıflandırma kuralları", Bagli(nameof(Vm.KuralDurumu)),
            Metin("Açıklamadaki sözcükler işlem türü ve kanal dağılımı önerir. Öneriler satır seçmez ve kayıt oluşturmaz. Farklı sonuç öneren kurallar çelişki olarak gösterilir."),
            Tikla("Kuralları ve önerileri yenile", Vm.KurallariYenileAsync),
            Tikla("Uygun önerileri uygula", () => { Vm.OnerileriUygula(); return Task.CompletedTask; }),
            Goster(Tikla("+ Kural ekle", () => { KuralFormunuKur(null); return Task.CompletedTask; }), nameof(Vm.KurallarDestekleniyor)),
            picker,
            Tikla("Seçili kuralı düzenle / kapat", () => { if (picker.SelectedItem is EkstreKuralDto rule) KuralFormunuKur(rule); return Task.CompletedTask; }),
            Tikla("Seçili kuralı sil", async () =>
            {
                if (picker.SelectedItem is not EkstreKuralDto rule || !Vm.EditorMu || Vm.Mesgul) return;
                var epoch = Vm.OturumNesli;
                if (await DisplayAlertAsync("Kuralı sil", $"{rule.Ad}\nBu kural silinecek. Önceden kaydedilen hareketler korunur.", "Sil", "Vazgeç"))
                    await Vm.KuralSilAsync(rule, epoch);
            }), _kuralFormu)));
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Vm.OturumNesli) or nameof(Vm.EditorMu))
                _kuralFormu.Content = null;
        };
    }
    private void KuralHatirla(EkstreSatirEditor row)
    {
        if (!Vm.EditorMu || Vm.Mesgul || !Vm.KurallarDestekleniyor || Vm.Belge is not { } doc || !Vm.Satirlar.Contains(row)) return;
        try { KuralFormunuKur(null, row.HatirlanacakKural(doc)); }
        catch (DogrulamaHatasi error) { Vm.Hata = error.Message; }
    }
    private void KuralFormunuKur(EkstreKuralDto? rule, EkstreKuralYaz? remembered = null)
    {
        if (!Vm.EditorMu || Vm.Mesgul || !Vm.KurallarDestekleniyor) return;
        var epoch = Vm.OturumNesli;
        var name = new Entry { Text = rule?.Ad ?? remembered?.Ad ?? "", MaxLength = 100 };
        var phrase = new Entry { Text = rule?.AciklamaIcerir ?? remembered?.AciklamaIcerir ?? "", MaxLength = 200 };
        var source = new Picker { ItemsSource = new[] { "Banka", "Kart" }, SelectedItem = rule?.Kaynak ?? remembered?.Kaynak ?? "Banka" };
        var bankItems = new[] { new EkstreBankaDto("", "Tüm bankalar") }.Concat(Vm.Bankalar.Select(b => new EkstreBankaDto(b.Kod, b.Ad))).ToArray();
        var bank = new Picker { ItemsSource = bankItems, ItemDisplayBinding = new Binding(nameof(EkstreBankaDto.Ad)) };
        bank.SelectedItem = bankItems.FirstOrDefault(b => b.Kod == (rule?.Banka ?? remembered?.Banka ?? ""));
        var direction = new Picker { ItemsSource = new[] { "Her yön", "Giriş", "Çıkış" } };
        direction.SelectedIndex = (rule?.Yon ?? remembered?.Yon) switch { "Giris" => 1, "Cikis" => 2, _ => 0 };
        var kind = new Picker { ItemDisplayBinding = new Binding(nameof(EkstreSecenek.Ad)) };
        var allocation = new Picker { ItemDisplayBinding = new Binding(nameof(EkstreSecenek.Ad)) };
        var enabled = new CheckBox { IsChecked = rule?.Aktif ?? remembered?.Aktif ?? true };
        SemanticProperties.SetDescription(enabled, "Kural etkin");
        var choices = new Dictionary<int, CheckBox>();
        var channelList = new VerticalStackLayout { Spacing = 4 };
        var selectedIds = rule?.KanalIds ?? remembered?.KanalIds ?? [];
        foreach (var channel in Vm.Kanallar.Where(c => c.Aktif))
        {
            var check = new CheckBox { IsChecked = selectedIds.Contains(channel.Id) };
            SemanticProperties.SetDescription(check, channel.Ad);
            choices.Add(channel.Id, check);
            channelList.Add(new HorizontalStackLayout { Children = { check, Metin(channel.Ad) } });
        }
        void UpdateAllocation()
        {
            var previous = (allocation.SelectedItem as EkstreSecenek)?.Kod;
            var general = source.SelectedItem as string == "Banka";
            EkstreSecenek[] options = (kind.SelectedItem as EkstreSecenek)?.Kod == EkstreIslemTurleri.Atla ? [new(DagilimBicimleri.Genel, "Dağılım yok (atla)")] : general ? [new(DagilimBicimleri.Genel, "Yalnız genel kasa"), new(DagilimBicimleri.Esit, "Seçilen kanallara eşit")] : [new(DagilimBicimleri.Esit, "Seçilen kanallara eşit")];
            allocation.ItemsSource = options;
            allocation.SelectedItem = options.FirstOrDefault(o => o.Kod == previous) ?? options[0];
            channelList.IsVisible = (allocation.SelectedItem as EkstreSecenek)?.Kod == DagilimBicimleri.Esit;
        }
        void UpdateSource()
        {
            var previous = (kind.SelectedItem as EkstreSecenek)?.Kod ?? rule?.IslemTuru ?? remembered?.IslemTuru;
            EkstreSecenek[] options = source.SelectedItem as string == EkstreKaynaklari.Kart ? [new(EkstreIslemTurleri.KartHarcama, "Kart harcaması"), new(EkstreIslemTurleri.Atla, "Satırı seçmeden bırak")] : [new(EkstreIslemTurleri.Gelir, "Banka girişi"), new(EkstreIslemTurleri.Gider, "Banka çıkışı"), new(EkstreIslemTurleri.Atla, "Satırı seçmeden bırak")];
            kind.ItemsSource = options;
            kind.SelectedItem = options.FirstOrDefault(o => o.Kod == previous) ?? options[0];
            UpdateAllocation();
        }
        source.SelectedIndexChanged += (_, _) => UpdateSource();
        kind.SelectedIndexChanged += (_, _) => UpdateAllocation();
        allocation.SelectedIndexChanged += (_, _) => channelList.IsVisible = (allocation.SelectedItem as EkstreSecenek)?.Kod == DagilimBicimleri.Esit;
        UpdateSource();
        var desiredAllocation = rule?.DagilimTuru ?? remembered?.DagilimTuru;
        allocation.SelectedItem = allocation.ItemsSource.Cast<EkstreSecenek>().FirstOrDefault(o => o.Kod == desiredAllocation) ?? allocation.SelectedItem;
        var error = new Label { TextColor = Colors.Red };
        var form = Kart(rule is null ? "Yeni kişisel kural" : $"Kuralı düzenle: {rule.Ad}",
            Alan("Kural adı", name), Alan("Açıklamada geçen sözcük / ifade", phrase),
            Metin("En az üç harfli bir sözcük kullanın. Türkçe harf ve noktalama farkları sadeleştirilir; sözcük sınırı korunur. Örneğin MIGROS, MIGROSAN ile eşleşmez."),
            Alan("Belge türü", source), Alan("Banka", bank), Alan("Hareket yönü", direction), Alan("Önerilen işlem", kind),
            Alan("Dağılım", allocation), channelList,
            Metin("Kuralda sabit tutar saklanmaz. Atla önerisi satırı seçmeden bırakır."),
            new HorizontalStackLayout { Children = { enabled, Metin("Kural etkin (kapatmak için işareti kaldırın)") } }, error);
        var key = new TekrarAnahtari();
        var actions = new HorizontalStackLayout { Spacing = 8 };
        actions.Add(Tikla("Kuralı kaydet", async () =>
        {
            if (_kuralFormu.Content != form || Vm.OturumNesli != epoch || !Vm.EditorMu) return;
            error.Text = "";
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(phrase.Text)) { error.Text = "Kural adını ve koşulunu girin."; return; }
            var mode = (allocation.SelectedItem as EkstreSecenek)?.Kod ?? "";
            var write = new EkstreKuralYaz(Guid.Empty, rule?.Surum ?? 0, name.Text.Trim(), source.SelectedItem as string ?? "", (bank.SelectedItem as EkstreBankaDto)?.Kod is { Length: > 0 } code ? code : null,
                phrase.Text.Trim(), direction.SelectedIndex switch { 1 => "Giris", 2 => "Cikis", _ => null }, (kind.SelectedItem as EkstreSecenek)?.Kod ?? "", mode,
                mode == DagilimBicimleri.Esit ? choices.Where(c => c.Value.IsChecked).Select(c => c.Key).ToArray() : [], enabled.IsChecked);
            write = write with { IstekId = key.Al(write) };
            await Vm.KuralKaydetAsync(rule?.Id, write, epoch);
            if (_kuralFormu.Content == form && Vm.OturumNesli == epoch && string.IsNullOrEmpty(Vm.Hata)) _kuralFormu.Content = null;
        }));
        actions.Add(Tikla("Vazgeç", () => { if (_kuralFormu.Content == form) _kuralFormu.Content = null; return Task.CompletedTask; }));
        ((VerticalStackLayout)form.Content!).Add(actions);
        _kuralFormu.Content = form;
        Gorunur.Yap(form, KaydirmaHesabi.FormKaydirmasi, () => name.Focus());
    }
}
