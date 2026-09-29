namespace Kasa.App.Core;

/// <summary>Yerel girdi doğrulaması: istek gönderilmeden girdi reddedildi (geçersiz tutar, eksik seçim, hatalı kanal
/// dağılımı). Sunucunun reddinden (<see cref="Kasa.ApiClient.KasaApiException"/>, 400) ayrı türdür: yerel ret sunucuya
/// gitmiş bir isteğin sonucuyla karışmaz. İleti kullanıcıya olduğu gibi gösterilir (<see cref="Yurutucu.HataMesaji"/>).</summary>
public sealed class DogrulamaHatasi(string mesaj) : Exception(mesaj);
