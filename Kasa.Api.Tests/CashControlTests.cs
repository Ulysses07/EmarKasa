using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class CashControlTests
{
    private static DateOnly Today => FinansTakipServisi.Bugun;
    private static DateOnly Start => new(Today.Year, 1, 1);

    [Fact]
    public async Task Kart_masrafi_kalan_borca_dagilir_kasayi_odemeye_kadar_degistirmez_ve_tekrar_cogaltilmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1,60m),new(2,40m)]);
        card = await Pay(c, card, 20m);
        var before = await c.GetStringAsync("/api/rapor/panel");
        var request = Fee(card, 10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        Assert.Equal(80m, preview.DevredenBorc);
        Shares(preview.Dagilimlar, (1,6m),(2,4m));
        request = request with { DagilimOzeti = preview.DagilimOzeti };
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/masraflar", request);
        Shares(card.KanalKartBorclari!, (1,54m),(2,36m)); Assert.Equal(90m,card.Borc);
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel"));
        var retry = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/masraflar", request);
        Assert.Equal(card.Surum,retry.Surum); Assert.Equal(2,retry.Harcamalar.Count);
        card = await Pay(c, card, 90m);
        Assert.Equal(0m,card.Borc); Assert.Equal(890m,await Cash(c));
    }

    [Fact]
    public async Task Faiz_gelecek_taksitleri_agirlik_olarak_kullanmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1,100m)]);
        card = await Charge(c, card, 200m, [new(2,200m)],2);
        var request = Fee(card, 10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        Assert.Equal(200m, preview.DevredenBorc); Shares(preview.Dagilimlar,(1,5m),(2,5m));
    }

    [Fact]
    public async Task Bankanin_masrafi_kalan_anaparayi_asabilir_oranlar_korunur()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        var card=await Charge(c,await Card(c),.03m,[new(1,.01m),new(2,.02m)]);
        var request=Fee(card,1m);
        var preview=await Post<KartMasrafOnizlemeDto>(c,$"/api/takip/kartlar/{card.Id}/masraf-onizleme",request);
        Assert.Equal(.03m,preview.DevredenBorc); Shares(preview.Dagilimlar,(1,.33m),(2,.67m));
        card=await Post<KartTakipDto>(c,$"/api/takip/kartlar/{card.Id}/masraflar",request with { DagilimOzeti=preview.DagilimOzeti });
        Assert.Equal(1.03m,card.Borc); Assert.Equal(1000m,await Cash(c));
        var single=await Charge(c,await Card(c),.01m,[new(1,.01m)]);
        var second=await Post<KartMasrafOnizlemeDto>(c,$"/api/takip/kartlar/{single.Id}/masraf-onizleme",Fee(single,1m));
        Shares(second.Dagilimlar,(1,1m));
    }

    [Fact]
    public async Task Masraf_onizlemesinden_sonra_odeme_yapilirsa_eski_onizleme_kaydedilemez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1,60m),new(2,40m)]);
        var request = Fee(card,10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c,$"/api/takip/kartlar/{card.Id}/masraf-onizleme",request);
        card = await Pay(c,card,10m);
        // Even when a client supplies the new version, the old allocation digest is rejected.
        request = request with { Surum=card.Surum,DagilimOzeti=preview.DagilimOzeti };
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraflar",request)).StatusCode);
        var after=(await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Single(after.Harcamalar); Assert.Equal(90m,after.Borc);
    }

    [Fact]
    public async Task Dagilimi_bekleyen_alis_borcuna_tahmini_faiz_payi_yazilamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card=await Card(c);
        var purchase=await Post<AlisDto>(c,"/api/alis",new AlisYaz(0,Start,"Firma",null,[new("Mal",100m,[new(1,100m)])]));
        await Post<AlisDto>(c,$"/api/alis/{purchase.Id}/odemeler",new AlisOdemeYaz(purchase.Surum,Guid.NewGuid(),Start,100m,card.Id));
        card=(await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        var response=await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme",Fee(card,5m));
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.Equal(1000m,await Cash(c));
    }

    [Fact]
    public async Task Baska_kartin_ekstresi_ve_odenmis_ekstreye_masraf_reddedilir()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        var card=await Charge(c,await Card(c),100m,[new(1,100m)]);
        var other=await Charge(c,await Card(c),100m,[new(2,100m)]);
        var request=Fee(card,5m) with { EkstreId=other.Ekstreler.First(e=>e.Borc>0).Id };
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme",request)).StatusCode);
        var statement=Fee(card,5m).EkstreId;
        card=await Pay(c,card,100m);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme",new KartMasrafYaz(Guid.NewGuid(),card.Surum,statement,Today,5m,"Faiz"))).StatusCode);
    }

    [Fact]
    public async Task Kasa_kontrolu_farki_saklar_bakiyeyi_degistirmez_ve_tekrar_tek_kayittir()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        var before=await c.GetStringAsync("/api/rapor/panel");
        var preview=await Post<KasaKontrolOnizlemeDto>(c,"/api/kasa-kontrol/onizleme",new KasaKontrolOnizle(-100m,"Sayım"));
        Assert.Equal(1000m,preview.SistemBakiye); Assert.Equal(-1100m,preview.Fark);
        var request=new KasaKontrolYaz(Guid.NewGuid(),-100m,preview.KontrolOzeti,"Sayım");
        var saved=await Post<KasaKontrolDto>(c,"/api/kasa-kontrol",request);
        var retry=await Post<KasaKontrolDto>(c,"/api/kasa-kontrol",request);
        Assert.Equal(saved,retry); Assert.Equal(before,await c.GetStringAsync("/api/rapor/panel"));
        Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol"))!);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/kasa-kontrol",request with { GercekBakiye=0 })).StatusCode);
    }

    [Fact]
    public async Task Kasa_onizlemesi_sonrasi_bakiye_degisirse_yeniden_karsilastirma_gerekir()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        var preview=await Post<KasaKontrolOnizlemeDto>(c,"/api/kasa-kontrol/onizleme",new KasaKontrolOnizle(1000m));
        (await c.PutAsJsonAsync("/api/ayarlar",new { takipBaslangic=Start,kasaAcilisDevri=900m })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/kasa-kontrol",new KasaKontrolYaz(Guid.NewGuid(),1000m,preview.KontrolOzeti))).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol"))!);
    }

    [Fact]
    public async Task Esik_surumu_ve_para_dogrulanir_izleyici_yazamaz()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        var first=(await c.GetFromJsonAsync<KasaEsikDto[]>("/api/kasa-esikleri"))!.First();
        Assert.False(first.Etkin); Assert.Equal(0,first.Surum);
        var result=await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}",new KasaEsikYaz(0,0m,true)); result.EnsureSuccessStatusCode();
        var saved=(await result.Content.ReadFromJsonAsync<KasaEsikDto>())!; Assert.Equal(1,saved.Surum);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}",new KasaEsikYaz(0,5m,true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}",new KasaEsikYaz(1,-1m,true))).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre",new { yeniSifre="izleyici123" })).EnsureSuccessStatusCode();
        using var viewer=f.CreateClient(); (await viewer.PostAsJsonAsync("/api/auth/login",new { kullanici="",sifre="izleyici123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync("/api/kasa-esikleri")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync("/api/kasa-kontrol")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}",new KasaEsikYaz(1,10m,true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.PostAsJsonAsync("/api/kasa-kontrol/onizleme",new KasaKontrolOnizle(0m))).StatusCode);
    }

    [Fact]
    public async Task Dusuk_bakiye_olayi_gun_degisiminde_tekrarlamaz_toparlanip_yeniden_dusunce_yenidir()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1",new KasaEsikYaz(0,100m,true))).EnsureSuccessStatusCode();
        using var scope=f.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var first=Assert.Single(KasaEsikServisi.Oku(db,Today,true));
        Assert.Equal(first.Anahtar,Assert.Single(KasaEsikServisi.Oku(db,Today,true)).Anahtar);
        db.ChangeTracker.Clear(); Assert.Empty(KasaEsikServisi.Oku(db,Today.AddDays(1),true));
        var channel=db.Kanallar.Single(k=>k.Id==1); channel.AcilisDevri=100m; db.SaveChanges();
        Assert.Empty(KasaEsikServisi.Oku(db,Today.AddDays(1),true));
        channel.AcilisDevri=0m; db.SaveChanges();
        var next=Assert.Single(KasaEsikServisi.Oku(db,Today.AddDays(1),true)); Assert.NotEqual(first.Anahtar,next.Anahtar);
    }

    [Fact]
    public async Task Bildirimler_kapaliyken_yeni_esik_olayi_tuketilmez()
    {
        await using var f=new KasaWebFactory(); using var c=await Editor(f);
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1",new KasaEsikYaz(0,100m,true))).EnsureSuccessStatusCode();
        using var scope=f.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Empty(KasaEsikServisi.Oku(db,Today,false));
        Assert.False(db.KasaEsikleri.Single().AlarmAcik); Assert.Equal(0,db.KasaEsikleri.Single().OlaySayisi);
        Assert.Single(KasaEsikServisi.Oku(db,Today.AddDays(1),true));
    }

    private static void Shares(IReadOnlyList<TakipKanalPayi> actual,params (int? Id,decimal Amount)[] expected) =>
        Assert.Equal(expected.OrderBy(p=>p.Id),actual.Select(p=>(p.KanalId,p.Tutar)).OrderBy(p=>p.KanalId));
    private static async Task<HttpClient> Editor(KasaWebFactory f)
    { var c=await f.EditorClientAsync(); (await c.PutAsJsonAsync("/api/ayarlar",new { takipBaslangic=Start,kasaAcilisDevri=1000m })).EnsureSuccessStatusCode(); return c; }
    private static async Task<T> Post<T>(HttpClient c,string path,object body)
    { var r=await c.PostAsJsonAsync(path,body); Assert.True(r.IsSuccessStatusCode,$"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}"); return (await r.Content.ReadFromJsonAsync<T>())!; }
    private static Task<KartTakipDto> Card(HttpClient c) => Post<KartTakipDto>(c,"/api/takip/kartlar",new KartTakipYaz(Guid.NewGuid(),0,"Kart",10000m,5,25,Start,0m,[]));
    private static Task<KartTakipDto> Charge(HttpClient c,KartTakipDto card,decimal amount,IReadOnlyList<KanalPayYaz> shares,int installments=1) =>
        Post<KartTakipDto>(c,$"/api/takip/kartlar/{card.Id}/harcamalar",new KartHarcamaYaz(Guid.NewGuid(),card.Surum,Start,"Mal",amount,installments,null,shares));
    private static Task<KartTakipDto> Pay(HttpClient c,KartTakipDto card,decimal amount) => Post<KartTakipDto>(c,$"/api/takip/kartlar/{card.Id}/odemeler",new KartTakipOdemeYaz(Guid.NewGuid(),card.Surum,Today,amount));
    private static KartMasrafYaz Fee(KartTakipDto card,decimal amount) => new(Guid.NewGuid(),card.Surum,card.Ekstreler.Where(e=>e.Borc>0).OrderBy(e=>e.KesimTarihi).First().Id,Today,amount,"Bankanın bildirdiği faiz");
    private static async Task<decimal> Cash(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
}
