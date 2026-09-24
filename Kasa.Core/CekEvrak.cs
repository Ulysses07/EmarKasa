namespace Kasa.Core;

/// <summary>
/// Kıymetli evrakın türü. Senet çekle aynı vade ve kasa kurallarına tabidir
/// (<see cref="CekKurali"/>): tür yalnız ayırt etmek, süzmek ve raporlamak içindir.
/// </summary>
public enum CekTuru
{
    Cek,    // varsayılan (eski kayıtların hepsi çektir)
    Senet
}

/// <summary>
/// Portföydeki (alınan) evrakın fiziksel konumu. Kasaya etkisi yoktur; yalnız takip içindir.
/// Verilen evrak her zaman <see cref="Elde"/> sayılır.
/// </summary>
public enum CekKonumu
{
    Elde,             // varsayılan
    BankadaTahsilde,  // tahsile verildi
    Teminatta,        // bankaya/tedarikçiye teminat olarak verildi
    Icrada            // takibe verildi
}
