using Kasa.App.Core;
using Kasa.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App;

/// <summary>Paket F kayıtları: fatura takibi, POS, ERP12 karşılaştırma ve ek/dosya servisleri.</summary>
public static class PaketFServisleri
{
    public static IServiceCollection AddPaketF(this IServiceCollection s)
    {
        s.AddSingleton<IDosyaSecici, MauiDosyaSecici>();
        s.AddSingleton<IEkAcici, MauiEkAcici>();
        s.AddSingleton<IAyarDeposu, PreferencesAyarDeposu>();

        s.AddTransient(sp =>
        {
            var vm = ActivatorUtilities.CreateInstance<FaturaTakibiViewModel>(sp);
            vm.EkAcici = sp.GetService<IEkAcici>();
            vm.DosyaSecici = sp.GetService<IDosyaSecici>();   // "Fatura geldi": gelen faturanın fotoğrafı/PDF'i
            return vm;
        });
        s.AddTransient<PosViewModel>();
        s.AddTransient(sp =>
        {
            var vm = ActivatorUtilities.CreateInstance<Erp12KarsilastirmaViewModel>(sp);
            vm.DosyaSecici = sp.GetService<IDosyaSecici>();
            return vm;
        });

        s.AddTransient<Views.FaturaTakibiPage>();
        s.AddTransient<Views.PosPage>();
        s.AddTransient<Views.Erp12Page>();
        return s;
    }

    /// <summary>İşlemler görünüm modeli: belge bölümü için dosya seçici ve ek açıcı bağlanır.</summary>
    public static IslemlerViewModel IslemlerModeli(IServiceProvider sp)
    {
        var vm = ActivatorUtilities.CreateInstance<IslemlerViewModel>(sp);
        vm.DosyaSecici = sp.GetService<IDosyaSecici>();
        vm.EkAcici = sp.GetService<IEkAcici>();
        return vm;
    }
}
