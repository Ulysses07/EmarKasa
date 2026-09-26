import UIKit

/// Uygulamanın açtığı tek site. Uygulama bir kabuktur: ekranlar, giriş ve
/// menüler sunucudan gelir, sunucudaki her güncelleme yeni sürüm gerekmeden
/// uygulamaya da gelir.
enum Kasa {
    static let host = "kasa.emarglobal.com"
    static let adres = URL(string: "https://kasa.emarglobal.com/")!

    /// Kullanıcı aracısının (User-Agent) sonuna eklenen işaret, örn.
    /// "EmarKasa-iOS/1.0.0". Web ekranı ileride iPhone uygulamasını bununla
    /// tanıyıp ona uymayan metinleri gizleyebilir.
    static var uaEki: String {
        let surum = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0"
        return "EmarKasa-iOS/\(surum)"
    }
}

extension UIColor {
    /// Sitenin tema rengi (theme-color #194d3c).
    static var kasaYesil: UIColor { UIColor(red: 0x19 / 255, green: 0x4D / 255, blue: 0x3C / 255, alpha: 1) }
    /// Sayfa arka planı (--cream #f6f3e9).
    static var kasaKrem: UIColor { UIColor(red: 0xF6 / 255, green: 0xF3 / 255, blue: 0xE9 / 255, alpha: 1) }
}

extension UIViewController {
    /// Bunun üstünde sunulan en üstteki ekran (kapanmakta olanlar sayılmaz).
    var enUstteki: UIViewController {
        var ust: UIViewController = self
        while let sunulan = ust.presentedViewController, !sunulan.isBeingDismissed {
            ust = sunulan
        }
        return ust
    }

    /// "Tamam" düğmeli kısa bir uyarı; en üstteki ekranın üstünde açılır.
    func uyar(_ baslik: String, _ mesaj: String) {
        let ust = enUstteki
        guard !(ust is UIAlertController) else { return }
        let uyari = UIAlertController(title: baslik, message: mesaj, preferredStyle: .alert)
        uyari.addAction(UIAlertAction(title: "Tamam", style: .default))
        ust.present(uyari, animated: true)
    }
}
