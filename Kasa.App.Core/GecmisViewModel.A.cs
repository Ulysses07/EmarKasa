namespace Kasa.App.Core;

// Paket A — Geçmiş'te "son bakışınızdan beri" yeni satırlar ve "Geçmişe dönük" etiketi.
public partial class GecmisViewModel
{
    private readonly IYerelDepo _depo;

    /// <summary>Sayfa açıldığında okunan "son görülen" satır Id'si; bundan yeni satırlar vurgulanır.
    /// İlk açılışta (hiç bakılmamış) null: birikmiş eski satırlar "yeni" sayılmaz.</summary>
    private int? _yeniSiniri;

    /// <summary>Bu açılışta vurgulanan yeni satır sayısı (özet satırı için).</summary>
    public int YeniSayisi => Kayitlar.Count(k => k.Yeni);

    /// <summary>"Son bakışınızdan beri 3 yeni değişiklik"; yeni satır yoksa null.</summary>
    public string? YeniMetni => YeniSayisi > 0 ? $"Son bakışınızdan beri {YeniSayisi} yeni değişiklik" : null;

    private void YeniSiniriniAl() => _yeniSiniri = _depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId);

    private bool YeniMi(Kasa.ApiClient.DegisiklikDto d) => _yeniSiniri is { } s && d.Id > s;

    /// <summary>
    /// Filtresiz ilk sayfa en yeni satırları içerir: en büyük Id "görüldü" olarak saklanır (Panel'deki
    /// "son bakışınızdan beri" sayısı sıfırlanır). Geçmiş boşsa (sunucu sıfırlandı) 0 yazılır.
    /// Vurgular bu açılış boyunca kalır; sonraki açılışta yeni sınır kullanılır.
    /// </summary>
    private void GorulduIsaretle()
    {
        OnPropertyChanged(nameof(YeniSayisi));
        OnPropertyChanged(nameof(YeniMetni));
        if (FiltreTur is not null) return;
        _depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, Kayitlar.Count > 0 ? Kayitlar.Max(k => k.Id) : 0);
    }
}

public partial class GecmisSatiri
{
    /// <summary>Son bakıştan sonra eklenen satır (bu cihazda).</summary>
    public bool Yeni { get; init; }

    /// <summary>Önceki bir ayın rakamını değiştiren değişiklik (sunucu kuralı).</summary>
    public bool GecmiseDonuk => Dto.GecmiseDonuk;

    /// <summary>Satırda en az bir etiket ("Yeni" / "Geçmişe dönük") var mı?</summary>
    public bool EtiketVar => Yeni || GecmiseDonuk;
}
