using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>
/// Toplu çek girişi (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md): ortak bilgiler bütün satırlara yazılır; satırlar boş eklenir,
/// seri üretilir ya da Excel'den yapıştırılır. Kayıt ya hep ya hiçtir: hatalı satır kendi altında gösterilir ve düzeltilmeden istek
/// gönderilmez (sunucu da aynı kuralı uygular). Aynı yön, banka ve numaralı çek (kayıtlı ya da listede) önce uyarılır.
/// </summary>
public partial class CekTakipViewModel
{
    /// <summary>Sunucunun bir istekteki en çok çek sınırı (CekEndpoints.EnCokTopluCek).</summary>
    public const int EnCokTopluCek = 200;

    private readonly TekrarAnahtari _topluKayit = new();
    private bool _topluAyniOnaylandi;

    /// <summary>Toplu giriş formunun hataları: ortak alanlar ve formun genel hatası; satır hataları satırdadır.</summary>
    public AlanHatalari TopluHatalari { get; } = new();
    public ObservableCollection<CekTopluSatiri> TopluSatirlar { get; } = new();

    [ObservableProperty] private bool _topluAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TopluVerilen), nameof(TopluKisiBasligi))] private KodCipi? _topluYon;
    [ObservableProperty] private KodCipi? _topluTur;
    [ObservableProperty] private string _topluBanka = "";
    [ObservableProperty] private string? _topluKasa;
    [ObservableProperty] private string _topluKisi = "";
    [ObservableProperty] private string _topluNot = "";
    [ObservableProperty] private string _seriAdet = "";
    [ObservableProperty] private string _seriIlkNo = "";
    [ObservableProperty] private decimal _seriTutar;
    [ObservableProperty] private DateTime _seriIlkVade;
    [ObservableProperty] private string _seriAralik = "1";
    [ObservableProperty] private string _yapistirilan = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(TopluAyniVar))] private string? _topluAyniUyarisi;

    public bool TopluVerilen => TopluYon?.Kod == CekYonleri.Verilen;
    public bool TopluAyniVar => TopluAyniUyarisi is not null;
    public string TopluKisiBasligi => TopluVerilen ? "Kime (satırda boşsa bu geçer)" : "Kimden (satırda boşsa bu geçer)";
    public string TopluToplam => TopluSatirlar.Count == 0 ? "Henüz satır yok."
        : $"{TopluSatirlar.Count} çek · toplam {ParaAyristirici.Goster(TopluSatirlar.All(s => ParaAyristirici.GecerliMi(s.Tutar)) ? TopluSatirlar.Sum(s => s.Tutar) : ParaAyristirici.Gecersiz)} ₺";

    /// <summary>Toplu formun açıldığı andaki değerleri. Seri alanlarının ve yapıştırma kutusunun yazılmış olması da değişikliktir.</summary>
    private KaydedilmemisDegisiklik? _topluIzi;
    private KaydedilmemisDegisiklik TopluIzi => _topluIzi ??= new(() => new
    {
        Yon = TopluYon?.Kod,
        Tur = TopluTur?.Kod,
        TopluBanka,
        TopluKasa,
        TopluKisi,
        TopluNot,
        SeriAdet,
        SeriIlkNo,
        SeriTutar,
        Yapistirilan,
        Satirlar = TopluSatirlar.Select(s => new { s.No, s.Kisi, s.Tutar, s.Vade }).ToList(),
    });

    partial void OnTopluYonChanged(KodCipi? value) => TopluAyniSifirla();
    partial void OnTopluBankaChanged(string value) => TopluAyniSifirla();

    private void TopluAyniSifirla()
    {
        _topluAyniOnaylandi = false;
        TopluAyniUyarisi = null;
    }

    /// <summary>"Toplu çek gir": yazılmış toplu form varsa önce onay sorulur.</summary>
    [RelayCommand]
    private async Task TopluAcAsync()
    {
        if (!await BirakilabilirAsync(TopluIzi))
            return;
        TopluBosalt();
        TopluYon = YonSecenekleri.First(y => y.Kod == Yon);
        TopluTur = TurSecenekleri[0];
        SeriIlkVade = Bugun.ToDateTime(TimeOnly.MinValue);
        _topluKayit.Temizle();
        TopluAcik = true;
        TopluIzi.Ac();
    }

    private void TopluBosalt()
    {
        TopluBanka = TopluKisi = TopluNot = SeriAdet = SeriIlkNo = Yapistirilan = "";
        TopluKasa = null;
        SeriTutar = 0;
        SeriAralik = "1";
        TopluSatirlar.Clear();
        TopluSatirlariDegisti();
        TopluHatalari.Temizle();
    }

    [RelayCommand]
    private void TopluKapat() => TopluKapatOnaysiz();

    private void TopluKapatOnaysiz()
    {
        TopluAcik = false;
        TopluIzi.Kapat();
        TopluBosalt();
    }

    [RelayCommand]
    private void TopluSatirEkle() => TopluEkle([new CekTaslagi("", "", 0, TopluSatirlar.Count > 0 ? DateOnly.FromDateTime(TopluSatirlar[^1].Vade) : Bugun)]);

    [RelayCommand]
    private void TopluSatirSil(CekTopluSatiri satir)
    {
        satir.Degisti -= TopluSatirDegisti;
        TopluSatirlar.Remove(satir);
        TopluSatirlariDegisti();
    }

    /// <summary>Serideki çekleri satırlara ekler.</summary>
    [RelayCommand]
    private void SeriEkle()
    {
        var h = TopluHatalari;
        h.Temizle();
        h.Denetle(SeriSayisi(SeriAdet, EnCokTopluCek, out var adet), nameof(SeriAdet), $"Adet 1 ile {EnCokTopluCek} arasında olmalı.");
        h.Denetle(!string.IsNullOrWhiteSpace(SeriIlkNo), nameof(SeriIlkNo), "İlk numarayı yazın.");
        h.Denetle(ParaAyristirici.GecerliMi(SeriTutar), nameof(SeriTutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(SeriTutar > 0, nameof(SeriTutar), "Tutar sıfırdan büyük olmalı.");
        h.Denetle(SeriSayisi(SeriAralik, 24, out var aralik), nameof(SeriAralik), "Aralık 1 ile 24 ay arasında olmalı.");
        if (!h.Var)
        {
            try
            {
                if (TopluEkle(CekSerisi.Uret(adet, SeriIlkNo, "", SeriTutar, DateOnly.FromDateTime(SeriIlkVade), aralik)))
                {
                    SeriAdet = SeriIlkNo = "";
                    SeriTutar = 0;
                }
            }
            catch (DogrulamaHatasi e) { h.Ayarla(nameof(SeriIlkNo), e.Message); }
        }
        h.GosterIste();
    }

    private static bool SeriSayisi(string metin, int enCok, out int sayi) =>
        int.TryParse(metin.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out sayi) && sayi >= 1 && sayi <= enCok;

    /// <summary>Yapıştırılan satırları ekler. Bir satır bile okunamazsa hiçbiri eklenmez; metin düzeltilmek üzere kutuda kalır.</summary>
    [RelayCommand]
    private void YapistirilaniEkle()
    {
        var h = TopluHatalari;
        h.Temizle();
        var sonuc = CekYapistirma.Coz(Yapistirilan, TopluKisi);
        if (sonuc.Hatalar.Count > 0)
            h.Ayarla(nameof(Yapistirilan), "Hiçbir satır eklenmedi.\n" + string.Join("\n", sonuc.Hatalar));
        else if (TopluEkle(sonuc.Satirlar))
            Yapistirilan = "";
        h.GosterIste();
    }

    private bool TopluEkle(IReadOnlyList<CekTaslagi> taslaklar)
    {
        if (TopluSatirlar.Count + taslaklar.Count > EnCokTopluCek)
        {
            TopluHatalari.Genel = $"Bir seferde en çok {EnCokTopluCek} çek girilir; {TopluSatirlar.Count} satır var, {taslaklar.Count} satır eklenemedi.";
            TopluHatalari.GosterIste();
            return false;
        }
        foreach (var t in taslaklar)
        {
            var satir = new CekTopluSatiri { No = t.No, Kisi = t.Kisi, Tutar = t.Tutar, Vade = t.Vade.ToDateTime(TimeOnly.MinValue) };
            satir.Degisti += TopluSatirDegisti;
            TopluSatirlar.Add(satir);
        }
        TopluSatirlariDegisti();
        return true;
    }

    private void TopluSatirDegisti(object? sender, EventArgs e) => TopluDegisti();

    private void TopluSatirlariDegisti()
    {
        for (var i = 0; i < TopluSatirlar.Count; i++)
            TopluSatirlar[i].Sira = i + 1;
        TopluDegisti();
    }

    private void TopluDegisti()
    {
        TopluAyniSifirla();
        TopluHatalari.Genel = null;
        OnPropertyChanged(nameof(TopluToplam));
    }

    [RelayCommand]
    private Task TopluYineDeKaydetAsync()
    {
        _topluAyniOnaylandi = true;
        return TopluKaydetAsync();
    }

    private string SatirKisisi(CekTopluSatiri s) => string.IsNullOrWhiteSpace(s.Kisi) ? TopluKisi.Trim() : s.Kisi.Trim();

    /// <summary>Ön doğrulama: ortak alanlar kendi altında, satır hatası satırın altında; biri bile varsa istek gönderilmez.</summary>
    private bool TopluGecerli(KodCipi yon, KodCipi tur)
    {
        var h = TopluHatalari;
        h.Denetle(tur.Kod != CekTurleri.Cek || !string.IsNullOrWhiteSpace(TopluBanka), nameof(TopluBanka), "Banka boş olamaz.");
        h.Denetle(yon.Kod != CekYonleri.Verilen || !string.IsNullOrWhiteSpace(TopluKasa), nameof(TopluKasa), "Ödeneceği kasayı (kanal ya da Ortak) seçin.");
        foreach (var s in TopluSatirlar)
            s.Hata = string.IsNullOrWhiteSpace(s.No) ? "Çek / senet numarası boş olamaz."
                : SatirKisisi(s).Length == 0 ? "Kişi boş olamaz."
                : !ParaAyristirici.GecerliMi(s.Tutar) ? ParaAyristirici.GecersizMesaji
                : s.Tutar <= 0 ? "Tutar sıfırdan büyük olmalı."
                : s.Vade.Year < 2000 ? "Vade tarihi 01.01.2000 tarihinden önce olamaz."
                : null;
        if (TopluSatirlar.Count == 0)
            h.Genel = "En az bir satır ekleyin.";
        else
            TopluSatirHatalariniOzetle();
        return !h.Var;
    }

    private void TopluSatirHatalariniOzetle()
    {
        var hatali = TopluSatirlar.Where(s => s.Hata is not null).Select(s => s.Sira).ToList();
        if (hatali.Count > 0)
            TopluHatalari.Genel = $"Hatalı satırlar: {string.Join(", ", hatali)}. Düzeltilmeden hiçbiri kaydedilmez.";
    }

    [RelayCommand]
    private Task TopluKaydetAsync() => FormIsleAsync(TopluHatalari, async n =>
    {
        if (!EditorMu || TopluYon is not { } yon || TopluTur is not { } tur || !TopluGecerli(yon, tur))
            return;
        static string? Bos(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var satirlar = TopluSatirlar.Select(s => new CekTopluSatirYaz(tur.Kod, yon.Kod, s.No.Trim(), Bos(TopluBanka), SatirKisisi(s), s.Tutar,
            DateOnly.FromDateTime(s.Vade), yon.Kod == CekYonleri.Verilen ? TopluKasa : null, false, null, Bos(TopluNot))).ToList();
        if (!_topluAyniOnaylandi)
        {
            var kayitli = await api.CeklerAsync(yon.Kod, CekSuzgecleri.Hepsi);
            if (!Gecerli(n))
                return;
            if (AyniCekler(satirlar, kayitli) is { Count: > 0 } ayni)
            {
                TopluAyniUyarisi = $"Aynı yön, banka ve numarayla başka çek var: {string.Join("; ", ayni)}. Ayrı çeklerse \"Yine de kaydet\"i seçin.";
                return;
            }
        }
        var g = new CekTopluYaz(Guid.Empty, satirlar);
        g = g with { IstekId = _topluKayit.Al(g) };
        CekTopluSonuc sonuc;
        try
        { sonuc = await api.CekTopluEkleAsync(g); }
        catch (KasaApiException e) when (Gecerli(n) && SunucuSatirHatalari(e.Message))
        {
            _topluKayit.Temizle();
            TopluSatirHatalariniOzetle();
            return;
        }
        if (!Gecerli(n))
            return;
        _topluKayit.Temizle();
        TopluKapatOnaysiz();
        var gorunen = TopluyuListeyeYaz(sonuc.Cekler);
        Mesaj = $"{sonuc.Cekler.Count} çek kaydedildi · toplam {Bicim.Tl(sonuc.Cekler.Sum(c => c.Tutar))} ₺" + (gorunen == sonuc.Cekler.Count ? "."
            : $"; {sonuc.Cekler.Count - gorunen} tanesi seçili süzgeç ({SuzgecMetni()}) dışında kaldığı için listede görünmüyor.");
        await OzetiYenileAsync(n);
    });

    /// <summary>Kayıtlı çeklerle ve satırların kendi arasında aynı çek (<see cref="CekKurallari.AyniCek"/>): "Satır 3 (12345) — Ahmet".</summary>
    private static List<string> AyniCekler(IReadOnlyList<CekTopluSatirYaz> satirlar, IReadOnlyList<CekDto> kayitli)
    {
        var ayni = new List<string>();
        for (var i = 0; i < satirlar.Count; i++)
        {
            var s = satirlar[i];
            if (kayitli.FirstOrDefault(c => CekKurallari.AyniCek(c.Yon, c.Banka, c.No, s.Yon, s.Banka, s.No)) is { } kayit)
                ayni.Add($"satır {i + 1} ({s.No}) — kayıtlı: {kayit.Kisi} · {Bicim.Tl(kayit.Tutar)} ₺ · vade {kayit.VadeTarihi:dd.MM.yyyy}");
            else if (Enumerable.Range(0, i).Any(j => CekKurallari.AyniCek(satirlar[j].Yon, satirlar[j].Banka, satirlar[j].No, s.Yon, s.Banka, s.No)))
                ayni.Add($"satır {i + 1} ({s.No}) — listede birden çok");
        }
        return ayni;
    }

    [GeneratedRegex(@"^Satır (?<no>\d+): (?<ileti>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TopluSatirIletisi();

    /// <summary>Sunucunun "Satır N: …" iletilerini satırlara yazar; hiçbiri eşleşmezse false (ileti genel hataya gider).</summary>
    private bool SunucuSatirHatalari(string ileti)
    {
        var yazilan = false;
        foreach (var parca in ileti.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (TopluSatirIletisi().Match(parca.Trim()) is { Success: true } m
                && int.TryParse(m.Groups["no"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var no) && no >= 1 && no <= TopluSatirlar.Count)
            {
                TopluSatirlar[no - 1].Hata = m.Groups["ileti"].Value;
                yazilan = true;
            }
        return yazilan;
    }

    /// <summary>Kaydedilen çeklerden seçili süzgece uyanları listeye vade sırasıyla yazar; görünenlerin sayısını döndürür. Yanıtı kaybolan
    /// kayıttan sonra liste yenilendiyse çekler zaten listededir: aynı kimlikli satır yenisiyle değişir, ikinci kez görünmez.</summary>
    private int TopluyuListeyeYaz(IReadOnlyList<CekDto> cekler)
    {
        var gorunen = cekler.Where(SuzgecteGorunur).ToList();
        var idler = cekler.Select(c => c.Id).ToHashSet();
        foreach (var eski in Cekler.Where(s => idler.Contains(s.Veri.Id)).ToList())
            Cekler.Remove(eski);
        foreach (var c in gorunen)
            Cekler.Insert(SiraDizini(c), new CekSatiri(c, Bugun));
        SatirlariBol();
        return gorunen.Count;
    }
}
