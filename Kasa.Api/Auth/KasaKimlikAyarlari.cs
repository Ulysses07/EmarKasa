using System.Text;

namespace Kasa.Api.Auth;

public static class KasaKimlikAyarlari
{
    public static string AnahtariDogrula(IConfiguration cfg, bool gelistirme)
    {
        var key = cfg["Kasa:JwtKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Kasa:JwtKey en az 32 baytlık bir imzalama anahtarı olmalıdır.");

        if (!gelistirme)
        {
            if (key.StartsWith("gelistirme-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Üretimde geliştirme JWT anahtarı kullanılamaz.");
            if (string.IsNullOrWhiteSpace(cfg["Kasa:EditorKullanici"])
                || string.IsNullOrWhiteSpace(cfg["Kasa:EditorSifre"])
                || cfg["Kasa:EditorSifre"] == "degistir-beni")
                throw new InvalidOperationException("Üretimde editör kullanıcı adı ve özel bir şifre yapılandırılmalıdır.");
        }
        return key;
    }
}
