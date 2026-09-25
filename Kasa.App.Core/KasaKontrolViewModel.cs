using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KasaKontrolViewModel(IKasaKontrolApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _anahtar = new();
    private KasaKontrolOnizle? _girdi;
    private KasaKontrolOnizlemeDto? _onizleme;
    public ObservableCollection<KasaKontrolSatiri> Gecmis { get; } = new();
    [ObservableProperty] private decimal _gercekBakiye;
    [ObservableProperty] private string _not = "";
    [ObservableProperty] private string? _karsilastirma;
    [ObservableProperty] private string? _esikUyarilari;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        VeriHazir = false;
        var gecmis = await api.KasaKontrolleriAsync(); var esikler = await api.KasaEsikleriAsync();
        if (!Gecerli(n)) return;
        TakipMetni.Doldur(Gecmis, gecmis.Select(x => new KasaKontrolSatiri(x)));
        EsikUyarilari = string.Join("\n", esikler.Where(x => x.Etkin && x.EsikAltinda).Select(x => $"{x.Kanal}: bakiye {Bicim.Tl(x.Bakiye)} ₺ — alt limit {Bicim.Tl(x.Tutar)} ₺"));
        if (string.IsNullOrEmpty(EsikUyarilari)) EsikUyarilari = "Açık uyarılarda alt limitin altında kanal yok.";
        _girdi = null; _onizleme = null; Karsilastirma = null; Tamamlandi();
    });
    private KasaKontrolOnizle Girdi() => new(GercekBakiye, Not.Trim());
    [RelayCommand] private Task OnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        _girdi = null; _onizleme = null; Karsilastirma = null;
        var g = Girdi(); var s = await api.KasaKontrolOnizleAsync(g);
        if (!Gecerli(n) || g != Girdi()) return;
        _girdi = g; _onizleme = s;
        Karsilastirma = $"Sistemdeki genel kasa: {Bicim.Tl(s.SistemBakiye)} ₺\nGerçek bakiye: {Bicim.Tl(s.GercekBakiye)} ₺\nFark (gerçek − sistem): {Bicim.Tl(s.Fark)} ₺\nYalnız karşılaştırma kaydı tutulur; kasa değişmez.";
    });
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        if (_onizleme is null || _girdi != Girdi()) { Hata = "Önce güncel bakiye karşılaştırmasını alın."; return; }
        var g = new KasaKontrolYaz(Guid.Empty, _girdi!.GercekBakiye, _onizleme.KontrolOzeti, _girdi.Not);
        g = g with { IstekId = _anahtar.Al(g) };
        try
        {
            var sonuc = await api.KasaKontrolKaydetAsync(g); if (!Gecerli(n)) return;
            Gecmis.Insert(0, new(sonuc)); _anahtar.Temizle(); _girdi = null; _onizleme = null; Karsilastirma = null;
            Mesaj = "Karşılaştırma kaydedildi. Genel kasa ve kanal bakiyeleri değiştirilmedi."; Tamamlandi();
        }
        catch (KasaApiException e) when ((int)e.DurumKodu == 409) { if (Gecerli(n)) { _girdi = null; _onizleme = null; Karsilastirma = null; } throw; }
    });
    protected override void OturumTemizle() { Gecmis.Clear(); GercekBakiye = 0; Not = ""; Karsilastirma = EsikUyarilari = null; _girdi = null; _onizleme = null; _anahtar.Temizle(); }
}
public record KasaKontrolSatiri(KasaKontrolDto Veri)
{
    public string Baslik => $"{Veri.Kaydedildi.LocalDateTime:dd.MM.yyyy HH:mm} · fark {Bicim.Tl(Veri.Fark)} ₺";
    public string Ozet => $"Sistem {Bicim.Tl(Veri.SistemBakiye)} ₺ · gerçek {Bicim.Tl(Veri.GercekBakiye)} ₺\n{Veri.Not}";
}

public partial class KasaEsikViewModel(IKasaKontrolApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public ObservableCollection<KasaEsikSatiri> Kanallar { get; } = new();
    [ObservableProperty] private KasaEsikSatiri? _secili;
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private bool _etkin;
    partial void OnSeciliChanged(KasaEsikSatiri? value) { Tutar = value?.Veri.Tutar ?? 0; Etkin = value?.Veri.Etkin ?? false; }
    public Task YukleAsync() => YurutAsync(async n => { VeriHazir = false; var s = await api.KasaEsikleriAsync(); if (!Gecerli(n)) return; TakipMetni.Doldur(Kanallar, s.Select(x => new KasaEsikSatiri(x))); Secili = null; Tamamlandi(); });
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null) return;
        if (Tutar < 0 || decimal.Round(Tutar, 2) != Tutar) { Hata = "Alt limiti sıfır veya pozitif, en fazla iki ondalıkla girin."; return; }
        var g = new KasaEsikYaz(Secili.Veri.Surum, Tutar, Etkin); var s = await api.KasaEsigiKaydetAsync(Secili.Veri.KanalId, g); if (!Gecerli(n)) return;
        var eski = Kanallar.First(k => k.Veri.KanalId == s.KanalId); Kanallar[Kanallar.IndexOf(eski)] = new(s); Secili = Kanallar.First(k => k.Veri.KanalId == s.KanalId); Mesaj = "Kanal alt limit uyarısı kaydedildi. Bakiye değiştirilmedi."; Tamamlandi();
    });
    protected override void OturumTemizle() { Kanallar.Clear(); Secili = null; Tutar = 0; Etkin = false; }
}
public record KasaEsikSatiri(KasaEsikDto Veri)
{
    public string Ad => Veri.Kanal;
    public string Baslik => Veri.Kanal;
    public string Ozet => $"Bakiye {Bicim.Tl(Veri.Bakiye)} ₺ · " + (Veri.Etkin ? $"Alt limit {Bicim.Tl(Veri.Tutar)} ₺" + (Veri.EsikAltinda ? " — limit altında" : "") : "Uyarı kapalı");
}
