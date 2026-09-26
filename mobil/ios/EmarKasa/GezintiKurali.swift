import Foundation

/// Hangi adresin uygulama içinde açılacağına, hangisinin indirileceğine,
/// hangisinin Safari'ye (ya da Telefon/Mail'e) gideceğine karar verir.
/// Saf işlevlerdir; birim testleri EmarKasaTests'te.
enum GezintiKurali {
    enum Hedef {
        case anaCerceve
        case altCerceve
        case yeniPencere   // target=_blank ya da window.open (targetFrame == nil)
    }

    enum Karar: Equatable {
        case izinVer               // uygulama içinde aç
        case indir                 // gezintiyi WKDownload'a çevir
        case yeniPenceredenIndir   // yeni pencereyi açma, startDownload ile indir
        case disaridaAc(URL)       // Safari, Telefon, Mail
        case engelle
    }

    /// Adres sitenin kendisi mi? Yalnız https://kasa.emarglobal.com (443).
    static func icerde(_ url: URL?) -> Bool {
        guard let url, url.scheme?.lowercased() == "https" else { return false }
        return url.host?.lowercased() == Kasa.host && (url.port == nil || url.port == 443)
    }

    static func karar(url: URL?, hedef: Hedef, indirilmeli: Bool) -> Karar {
        guard let url, let sema = url.scheme?.lowercased() else { return .engelle }

        // Sitenin kendi adresleri ve sayfanın ürettiği blob: dosyaları
        // (yedek, Excel/CSV raporu). blob: yalnız indirilebilir.
        let blobIcerde = sema == "blob" && icerde(URL(string: String(url.absoluteString.dropFirst(5))))
        if icerde(url) || blobIcerde {
            if indirilmeli { return hedef == .yeniPencere ? .yeniPenceredenIndir : .indir }
            return blobIcerde ? .engelle : .izinVer
        }

        switch sema {
        case "about":
            return ["about:blank", "about:srcdoc"].contains(url.absoluteString) ? .izinVer : .engelle
        case "http", "https", "mailto", "tel", "sms":
            // Başka siteler uygulamada açılmaz; bir çerçeve de dışarıyı açamaz.
            return hedef == .altCerceve ? .engelle : .disaridaAc(url)
        default:
            return .engelle
        }
    }

    /// Sunucu yanıtı gösterilmek yerine indirilmeli mi? Ek (attachment) olarak
    /// gelen ya da görünümün gösteremediği her şey indirilir. Uygulamanın ana
    /// ekranında HTML dışı bir yanıt (PDF, JSON) da indirilir: tek sayfalık
    /// uygulamanın yerine geçerse geri dönülecek bir düğme yoktur.
    static func yanitIndirilmeli(icerikTuru: String?, icerikYerlesimi: String?,
                                 gosterilebilir: Bool, anaEkran: Bool) -> Bool {
        if let yerlesim = icerikYerlesimi?.trimmingCharacters(in: .whitespaces).lowercased(),
           yerlesim.hasPrefix("attachment") {
            return true
        }
        if !gosterilebilir { return true }
        guard anaEkran, let tur = icerikTuru?.lowercased() else { return false }
        return tur != "text/html" && tur != "application/xhtml+xml"
    }

    /// İndirmede sunucu hata döndürdüyse kullanıcıya gösterilecek açıklama;
    /// başarılı (2xx) yanıtta nil. Belge ve ekstre bağlantıları sayfanın
    /// yanıt denetiminden geçmeden doğrudan sunucuya gider.
    static func indirmeHatasi(durumKodu: Int) -> String? {
        switch durumKodu {
        case 200...299:
            return nil
        case 401:
            return "Oturumunuz sona ermiş. Sayfayı yenileyip yeniden giriş yapın."
        case 403:
            return "Bu dosyayı indirme yetkiniz yok."
        case 404, 410:
            return "Dosya bulunamadı, silinmiş olabilir. Sayfayı yenileyip tekrar deneyin."
        default:
            return "Dosya şu anda indirilemiyor. Biraz sonra tekrar deneyin."
        }
    }

    /// Önerilen dosya adından güvenli bir ad: klasör kısmı atılır, boşsa
    /// "kasa-dosyasi" olur. Türkçe harfler korunur.
    static func guvenliAd(_ onerilen: String) -> String {
        let son = onerilen.split(separator: "/").last.map(String.init) ?? ""
        let ad = son.replacingOccurrences(of: ":", with: "-").trimmingCharacters(in: .whitespacesAndNewlines)
        return ad.isEmpty || ad == "." || ad == ".." ? "kasa-dosyasi" : ad
    }
}
