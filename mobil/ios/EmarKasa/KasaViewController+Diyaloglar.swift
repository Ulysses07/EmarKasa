import UIKit
import WebKit

/// Yeni pencere istekleri ve JavaScript alert/confirm/prompt pencereleri.
/// Site bugün kendi <dialog> pencerelerini kullanıyor; bunlar güvenlik ağı.
extension KasaViewController: WKUIDelegate {
    // MARK: Yeni pencereler (target=_blank)

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        let istek = navigationAction.request
        switch GezintiKurali.karar(url: istek.url, hedef: .yeniPencere, indirilmeli: navigationAction.shouldPerformDownload) {
        case .izinVer:
            guard GezintiKurali.icerde(istek.url) else { return nil }   // boş (about:blank) pencere açılmaz
            // Rapor penceresinden istenen yeni pencere aynı pencerede açılır.
            if webView !== self.webView {
                webView.load(istek)
                return nil
            }
            // Yazdırılabilir rapor: ayrı bir sayfada, aynı çerezlerle (WebKit'in
            // verdiği yapılandırma kullanılmak zorunda). Uygulama ekranı yerinde kalır.
            let yeni = WKWebView(frame: .zero, configuration: configuration)
            yeni.navigationDelegate = self
            yeni.uiDelegate = self
            yeni.allowsLinkPreview = false
            let pencere = RaporPenceresi(webView: yeni)
            rapor = pencere
            enUstteki.present(UINavigationController(rootViewController: pencere), animated: true)
            return yeni
        case .indir, .yeniPenceredenIndir:
            webView.startDownload(using: istek) { [weak self] yeni in self?.indirme.takip(yeni) }
        case .disaridaAc(let url):
            UIApplication.shared.open(url)
        case .engelle:
            break
        }
        return nil
    }

    func webViewDidClose(_ webView: WKWebView) {
        if webView === rapor?.webView { raporuKapat(uyari: nil) }
    }

    // MARK: JavaScript pencereleri

    // WebKit, tamamlayıcı hiç çağrılmazsa ya da iki kez çağrılırsa uygulamayı
    // durdurur. TekSeferlik ikinci çağrıyı yutar; gösterilemeyen uyarıda
    // varsayılan yanıt hemen verilir, düğmesine basılmadan kapanan uyarıda
    // (rapor sayfasıyla birlikte) raporuKapat verir.

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping @MainActor @Sendable () -> Void) {
        let bitir = TekSeferlik<Void> { _ in completionHandler() }
        let uyari = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        uyari.addAction(UIAlertAction(title: "Tamam", style: .default) { _ in bitir.cagir(()) })
        goster(uyari) { bitir.cagir(()) }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping @MainActor @Sendable (Bool) -> Void) {
        let bitir = TekSeferlik<Bool>(completionHandler)
        let uyari = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        uyari.addAction(UIAlertAction(title: "Vazgeç", style: .cancel) { _ in bitir.cagir(false) })
        uyari.addAction(UIAlertAction(title: "Tamam", style: .default) { _ in bitir.cagir(true) })
        goster(uyari) { bitir.cagir(false) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String,
                 defaultText: String?, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping @MainActor @Sendable (String?) -> Void) {
        let bitir = TekSeferlik<String?>(completionHandler)
        let uyari = UIAlertController(title: nil, message: prompt, preferredStyle: .alert)
        uyari.addTextField { $0.text = defaultText }
        uyari.addAction(UIAlertAction(title: "Vazgeç", style: .cancel) { _ in bitir.cagir(nil) })
        uyari.addAction(UIAlertAction(title: "Tamam", style: .default) { [weak uyari] _ in
            bitir.cagir(uyari?.textFields?.first?.text ?? "")
        })
        goster(uyari) { bitir.cagir(nil) }
    }

    /// Uyarıyı en üstteki ekranda gösterir. Uygulama arka plandaysa, ekranda
    /// zaten bir uyarı varsa ya da sunum tutmazsa `olmazsa` hemen çağrılır.
    /// Gösterildiyse `olmazsa` varsayılan yanıt olarak saklanır.
    private func goster(_ uyari: UIAlertController, olmazsa: @escaping @MainActor () -> Void) {
        let ust = enUstteki
        guard let sahne = view.window?.windowScene, sahne.activationState != .background,
              !(ust is UIAlertController) else {
            olmazsa()
            return
        }
        ust.present(uyari, animated: true)
        if uyari.presentingViewController == nil {
            olmazsa()
        } else {
            jsVarsayilanYaniti = olmazsa
        }
    }
}

/// Bir tamamlayıcıyı (completion handler) en fazla bir kez çağırır; sonraki
/// çağrılar etkisizdir.
@MainActor
final class TekSeferlik<Deger> {
    private var islev: (@MainActor (Deger) -> Void)?

    init(_ islev: @escaping @MainActor (Deger) -> Void) {
        self.islev = islev
    }

    func cagir(_ deger: Deger) {
        guard let islev else { return }
        self.islev = nil
        islev(deger)
    }
}
