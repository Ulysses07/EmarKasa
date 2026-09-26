import UIKit
import WebKit

/// Sitenin yeni pencerede açtığı yazdırılabilir gider raporu
/// (/api/disari-aktar?bicim=html). Uygulama ekranının üstünde ayrı bir
/// sayfada açılır; "Yazdır" menüsünden PDF olarak da kaydedilebilir.
final class RaporPenceresi: UIViewController {
    let webView: WKWebView

    init(webView: WKWebView) {
        self.webView = webView
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) kullanılmıyor")
    }

    override func loadView() {
        view = webView
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = "Rapor"
        navigationItem.leftBarButtonItem = UIBarButtonItem(
            title: "Kapat", style: .done, target: self, action: #selector(kapat))
        if UIPrintInteractionController.isPrintingAvailable {
            navigationItem.rightBarButtonItem = UIBarButtonItem(
                title: "Yazdır", style: .plain, target: self, action: #selector(yazdir(_:)))
        }
    }

    @objc private func kapat() {
        dismiss(animated: true)
    }

    /// Sistem yazdırma ekranı; oradan yazıcıya gönderilir ya da paylaş
    /// düğmesiyle PDF olarak Dosyalar'a kaydedilir.
    @objc private func yazdir(_ dugme: UIBarButtonItem) {
        let bilgi = UIPrintInfo(dictionary: nil)
        bilgi.outputType = .general
        bilgi.jobName = (webView.title?.isEmpty == false ? webView.title : nil) ?? "Kasa raporu"
        let yazici = UIPrintInteractionController.shared
        yazici.printInfo = bilgi
        yazici.printFormatter = webView.viewPrintFormatter()
        if traitCollection.userInterfaceIdiom == .pad {
            _ = yazici.present(from: dugme, animated: true, completionHandler: nil)
        } else {
            _ = yazici.present(animated: true, completionHandler: nil)
        }
    }
}
