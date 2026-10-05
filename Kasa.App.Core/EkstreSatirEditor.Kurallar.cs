using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

public partial class EkstreSatirEditor
{
    private bool _degisiklikIzle, _oneriUygulaniyor;
    public bool ElleDegisti { get; private set; }
    [ObservableProperty] private EkstreKuralOnerisi? _oneri;
    public string OneriMetni => Oneri is { } o ? $"{string.Join(", ", o.KuralAdlari)} · {o.Aciklama}" : "Kişisel kural önerisi henüz alınmadı.";
    public bool OneriUygulanabilir => Secilebilir && Oneri?.Durum == "Oneri" && Aciklama == Kaynak.Aciklama;
    partial void OnOneriChanged(EkstreKuralOnerisi? value)
    {
        OnPropertyChanged(nameof(OneriMetni));
        OnPropertyChanged(nameof(OneriUygulanabilir));
    }
    private void ElleDegisiklik()
    {
        if (!_degisiklikIzle || _oneriUygulaniyor)
            return;
        ElleDegisti = true;
        OnPropertyChanged(nameof(ElleDegisti));
        OnPropertyChanged(nameof(OneriUygulanabilir));
    }
    public bool OneriyiUygula(bool toplu)
    {
        if (!OneriUygulanabilir || toplu && ElleDegisti || Oneri is not { } o)
            return false;
        if (o.KanalIds.Any(id => !Kanallar.Any(k => k.Id == id && k.Aktif)))
            return false;
        _oneriUygulaniyor = true;
        try
        {
            if (o.IslemTuru == EkstreIslemTurleri.Atla)
            {
                Secili = false;
                IslemTuru = null;
                Paylar.Clear();
                DagilimTuru = null;
            }
            else
            {
                var tur = IslemTurleri.FirstOrDefault(t => t.Kod == o.IslemTuru);
                var dagilim = DagilimTurleri.FirstOrDefault(t => t.Kod == o.DagilimTuru);
                if (tur is null || dagilim is null)
                    return false;
                IslemTuru = tur;
                DagilimTuru = dagilim;
                Paylar.Clear();
                foreach (var id in o.KanalIds)
                    Paylar.Add(new(Kanallar) { Kanal = Kanallar.Single(k => k.Id == id) });
            }
            return true;
        }
        finally { _oneriUygulaniyor = false; }
    }
    public EkstreKuralYaz HatirlanacakKural(EkstreBelgeDto belge)
    {
        var tur = IslemTuru?.Kod ?? (Kaynak.OnerilenIslem == EkstreIslemTurleri.Atla ? EkstreIslemTurleri.Atla : null);
        if (tur is not (EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama or EkstreIslemTurleri.Atla))
            throw new DogrulamaHatasi("Ödeme/iade ve mevcut kayıt eşleşmesi kural olarak hatırlanmaz; satırda seçilir.");
        var dagilim = tur == EkstreIslemTurleri.Atla ? DagilimBicimleri.Genel : DagilimTuru?.Kod;
        if (dagilim == DagilimBicimleri.Ozel)
        {
            if (Paylar.Count != 1 || Paylar[0].Kanal is null)
                throw new DogrulamaHatasi("Özel kanal tutarları başka hareketlere kopyalanamaz. Kurallar bölümünden genel kasa veya eşit kanal dağılımı tanımlayın.");
            dagilim = DagilimBicimleri.Esit;
        }
        if (dagilim is not (DagilimBicimleri.Genel or DagilimBicimleri.Esit))
            throw new DogrulamaHatasi("Hatırlamadan önce genel kasa veya eşit kanal dağılımını seçin.");
        var ids = dagilim == DagilimBicimleri.Esit ? Paylar.Where(p => p.Kanal is not null).Select(p => p.Kanal!.Id).Distinct().ToArray() : [];
        if (dagilim == DagilimBicimleri.Esit && (ids.Length == 0 || ids.Length != Paylar.Count))
            throw new DogrulamaHatasi("Hatırlanacak kanalları birer kez seçin.");
        var text = Aciklama[..Math.Min(200, Aciklama.Length)];
        return new(Guid.NewGuid(), 0, text[..Math.Min(100, text.Length)], belge.Kaynak, belge.Banka, text,
            Kaynak.Yon is "Giris" or "Cikis" ? Kaynak.Yon : null, tur, dagilim, ids);
    }
}
