namespace Kasa.App.Core;

/// <summary>Kullanıcının seçtiği dosya (ad + içerik).</summary>
public sealed record SecilenDosya(string Ad, byte[] Icerik);

/// <summary>
/// Platform dosya seçici (MAUI FilePicker). Toplu yüklemede xlsx ya da CSV dosyası seçmek için; kullanıcı
/// vazgeçerse null.
/// </summary>
public interface IDosyaSecici
{
    Task<SecilenDosya?> SecAsync();
}
