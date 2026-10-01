using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Kasa.App.Core;

namespace Kasa.App.WinUI;

/// <summary>
/// Başlat menüsündeki "Emar Kasa" kısayolu (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Emar Kasa.lnk; tasarım 2026-09-30
/// masaüstü bildirimleri, "Uygulamada verilen kararlar"). Kısayol uygulamayı açar; ayrıca paketsiz uygulamanın bildirim kimliğini
/// Windows kabuğuna bildirmenin belgelenmiş yoludur: System.AppUserModel.ID bildirimlerin AppUserModelID'si
/// (<see cref="BildirimKimligi.Aumid"/>), System.AppUserModel.ToastActivatorCLSID tıklamada Windows'un CoCreateInstance ile
/// oluşturduğu INotificationActivationCallback sınıfıdır (Windows App SDK'nın etkinleştiricisi). Kısayol yalnız denetlenen alanlardan
/// biri farklıysa (<see cref="KisayolBilgisi.AyniMi"/>) yeniden yazılır. Kaynaklar:
/// learn.microsoft.com/windows/win32/properties/props-system-appusermodel-id,
/// learn.microsoft.com/windows/win32/properties/props-system-appusermodel-toastactivatorclsid.
/// </summary>
internal static class BaslatKisayolu
{
    /// <summary>System.AppUserModel.ID ve ToastActivatorCLSID'nin biçim kimliği (propkey.h).</summary>
    private static readonly Guid AppUserModelBicimi = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint AppUserModelIdNo = 5;
    private const uint ToastActivatorClsidNo = 26;
    private const ushort VtLpwstr = 31;
    private const ushort VtClsid = 72;
    /// <summary>IShellLinkW.GetPath: ortam değişkenleri açılmadan, kayıtlı yol.</summary>
    private const uint SlgpRawPath = 0x4;
    private const int YolUzunlugu = 32768;

    public static string Yol => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), BildirimKimligi.KisayolAdi);

    /// <summary>Kısayol yoksa ya da farklıysa yazar; yazdıysa true. Okunamayan (bozuk) kısayol yeniden yazılır.</summary>
    public static bool Guncelle(KisayolBilgisi istenen)
    {
        var yol = Yol;
        if (File.Exists(yol) && Oku(yol) is { } mevcut && mevcut.AyniMi(istenen))
            return false;
        Yaz(yol, istenen);
        return true;
    }

    private static KisayolBilgisi? Oku(string yol)
    {
        var baglanti = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)baglanti).Load(yol, 0 /*STGM_READ*/);
            var hedef = new StringBuilder(YolUzunlugu);
            baglanti.GetPath(hedef, hedef.Capacity, IntPtr.Zero, SlgpRawPath);
            var dizin = new StringBuilder(YolUzunlugu);
            baglanti.GetWorkingDirectory(dizin, dizin.Capacity);
            var simge = new StringBuilder(YolUzunlugu);
            baglanti.GetIconLocation(simge, simge.Capacity, out var simgeSirasi);
            var ozellikler = (IPropertyStore)baglanti;
            string? aumid = null;
            Guid? clsid = null;
            var anahtar = new OzellikAnahtari(AppUserModelBicimi, AppUserModelIdNo);
            ozellikler.GetValue(ref anahtar, out var deger);
            try
            {
                if (deger.Tur == VtLpwstr && deger.Isaretci != IntPtr.Zero)
                    aumid = Marshal.PtrToStringUni(deger.Isaretci);
            }
            finally { PropVariantClear(ref deger); }
            anahtar = new OzellikAnahtari(AppUserModelBicimi, ToastActivatorClsidNo);
            ozellikler.GetValue(ref anahtar, out deger);
            try
            {
                if (deger.Tur == VtClsid && deger.Isaretci != IntPtr.Zero)
                    clsid = Marshal.PtrToStructure<Guid>(deger.Isaretci);
            }
            finally { PropVariantClear(ref deger); }
            return new KisayolBilgisi(hedef.ToString(), dizin.ToString(), simge.ToString(), simgeSirasi, aumid, clsid);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(baglanti);
        }
    }

    private static void Yaz(string yol, KisayolBilgisi istenen)
    {
        var baglanti = (IShellLinkW)new ShellLink();
        try
        {
            baglanti.SetPath(istenen.Hedef);
            baglanti.SetWorkingDirectory(istenen.CalismaDizini);
            baglanti.SetIconLocation(istenen.SimgeDosyasi, istenen.SimgeSirasi);
            baglanti.SetDescription(WindowsBildirimGosterici.GorunenAd);
            var ozellikler = (IPropertyStore)baglanti;
            var anahtar = new OzellikAnahtari(AppUserModelBicimi, AppUserModelIdNo);
            var deger = new OzellikDegeri { Tur = VtLpwstr, Isaretci = Marshal.StringToCoTaskMemUni(istenen.Aumid ?? "") };
            try
            {
                ozellikler.SetValue(ref anahtar, ref deger);
            }
            finally { PropVariantClear(ref deger); }
            if (istenen.EtkinlestiriciClsid is { } clsid)
            {
                anahtar = new OzellikAnahtari(AppUserModelBicimi, ToastActivatorClsidNo);
                deger = new OzellikDegeri { Tur = VtClsid, Isaretci = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>()) };
                Marshal.StructureToPtr(clsid, deger.Isaretci, false);
                try
                {
                    ozellikler.SetValue(ref anahtar, ref deger);
                }
                finally { PropVariantClear(ref deger); }
            }
            ozellikler.Commit();
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            ((IPersistFile)baglanti).Save(yol, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(baglanti);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref OzellikDegeri deger);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dosya, int uzunluk, IntPtr bulunan, uint bayrak);
        void GetIDList(out IntPtr kimlikListesi);
        void SetIDList(IntPtr kimlikListesi);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ad, int uzunluk);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string ad);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dizin, int uzunluk);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dizin);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder argumanlar, int uzunluk);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string argumanlar);
        void GetHotkey(out short kisayolTusu);
        void SetHotkey(short kisayolTusu);
        void GetShowCmd(out int gosterim);
        void SetShowCmd(int gosterim);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder simge, int uzunluk, out int sira);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string simge, int sira);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string goreliYol, uint ayrilmis);
        void Resolve(IntPtr pencere, uint bayrak);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string dosya);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint sayi);
        void GetAt(uint sira, out OzellikAnahtari anahtar);
        void GetValue(ref OzellikAnahtari anahtar, out OzellikDegeri deger);
        void SetValue(ref OzellikAnahtari anahtar, ref OzellikDegeri deger);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct OzellikAnahtari(Guid bicim, uint no)
    {
        public readonly Guid Bicim = bicim;
        public readonly uint No = no;
    }

    /// <summary>PROPVARIANT: yalnız işaretçi taşıyan türler (VT_LPWSTR, VT_CLSID) kullanılır; x64'te 24 bayt.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct OzellikDegeri
    {
        [FieldOffset(0)] public ushort Tur;
        [FieldOffset(8)] public IntPtr Isaretci;
    }
}
