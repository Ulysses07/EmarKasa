import UIKit
import WebKit

/// Uygulamanın tek ekranı: canlı Kasa web ekranı, güvenli alana oturmuş bir
/// WKWebView'da. Üstteki durum çubuğu şeridi sitenin yeşili, alt kısım kremi.
final class KasaViewController: UIViewController {
    let webView = WKWebView(frame: .zero, configuration: KasaViewController.yapilandirma())
    let indirme = IndirmeYoneticisi()
    /// Açık rapor penceresi (target=_blank ile açılan yazdırılabilir rapor).
    weak var rapor: RaporPenceresi?
    /// Ekrandaki JavaScript penceresinin (alert/confirm/prompt) varsayılan
    /// yanıtı. Pencere düğmesine basılmadan kapanırsa (rapor sayfasıyla
    /// birlikte) WebKit'e yanıt yine verilmeli; yoksa uygulamayı durdurur.
    var jsVarsayilanYaniti: (@MainActor () -> Void)?

    private let durumCubugu = UIView()
    private let ilerleme = UIProgressView(progressViewStyle: .bar)
    private let hataEkrani = HataEkrani()
    private let yenileme = UIRefreshControl()
    private var ilerlemeGozlemi: NSKeyValueObservation?
    /// Site en az bir kez tam yüklendi mi? Yüklendiyse sonraki hatalarda
    /// sayfa yerinde kalır, yalnız kısa bir uyarı çıkar.
    private var sayfaGosterildi = false
    private var sonCokme: Date?

    override var preferredStatusBarStyle: UIStatusBarStyle { .lightContent }

    /// Web görünümü ayarları; WKWebView oluşturulurken kopyalanır.
    private static func yapilandirma() -> WKWebViewConfiguration {
        let ayar = WKWebViewConfiguration()
        // Kalıcı depo: giriş çerezi (kasa_auth, 30 gün) uygulama kapanınca da kalır.
        ayar.websiteDataStore = .default()
        // Varsayılan "Mobile/15E148" korunur, sonuna uygulama işareti eklenir.
        ayar.applicationNameForUserAgent = (ayar.applicationNameForUserAgent ?? "Mobile/15E148") + " " + Kasa.uaEki
        // Rapor bağlantısı betikle tıklanıyor; her yeni pencere isteğine biz karar veriyoruz.
        ayar.preferences.javaScriptCanOpenWindowsAutomatically = true
        let sayfa = WKWebpagePreferences()
        sayfa.preferredContentMode = .mobile
        ayar.defaultWebpagePreferences = sayfa
        // iOS, 16 pikselden küçük yazılı bir alana dokununca sayfayı büyütür ve
        // geri küçültmez. Uygulama ekranında (/) ölçek sabitlenir; rapor gibi
        // diğer sayfalar büyütülebilir kalır.
        let olcek = WKUserScript(source: """
            if (location.pathname === '/') {
              var m = document.querySelector('meta[name=viewport]');
              if (m) m.content = 'width=device-width, initial-scale=1, maximum-scale=1';
            }
            """, injectionTime: .atDocumentEnd, forMainFrameOnly: true)
        ayar.userContentController.addUserScript(olcek)
        return ayar
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .kasaKrem
        durumCubugu.backgroundColor = .kasaYesil
        indirme.sahip = self

        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = false   // tek sayfalık uygulama; geri gidilecek sayfa yok
        webView.allowsLinkPreview = false
        webView.isOpaque = false
        webView.backgroundColor = .kasaKrem
        webView.scrollView.backgroundColor = .kasaKrem
        webView.underPageBackgroundColor = .kasaKrem
        #if DEBUG
        if #available(iOS 16.4, *) { webView.isInspectable = true }
        #endif

        yenileme.tintColor = .kasaYesil
        yenileme.addTarget(self, action: #selector(yenilemeIstendi), for: .valueChanged)
        webView.scrollView.refreshControl = yenileme

        ilerleme.progressTintColor = .kasaYesil
        ilerleme.trackTintColor = .clear
        ilerleme.isHidden = true
        ilerlemeGozlemi = webView.observe(\.estimatedProgress) { [weak self] _, _ in
            Task { @MainActor [weak self] in self?.ilerlemeyiGoster() }
        }

        hataEkrani.isHidden = true
        hataEkrani.tekrarDene = { [weak self] in self?.webView.load(URLRequest(url: Kasa.adres)) }

        yerlestir()
        webView.load(URLRequest(url: Kasa.adres))
    }

    private func yerlestir() {
        for gorunum in [durumCubugu, webView, hataEkrani, ilerleme] as [UIView] {
            gorunum.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(gorunum)
        }
        // Site çentik ve ana ekran çizgisi için boşluk bırakmıyor; web görünümü
        // güvenli alanın içinde kalır.
        let guvenli = view.safeAreaLayoutGuide
        NSLayoutConstraint.activate([
            durumCubugu.topAnchor.constraint(equalTo: view.topAnchor),
            durumCubugu.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            durumCubugu.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            durumCubugu.bottomAnchor.constraint(equalTo: guvenli.topAnchor),

            webView.topAnchor.constraint(equalTo: guvenli.topAnchor),
            webView.leadingAnchor.constraint(equalTo: guvenli.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: guvenli.trailingAnchor),
            webView.bottomAnchor.constraint(equalTo: guvenli.bottomAnchor),

            hataEkrani.topAnchor.constraint(equalTo: webView.topAnchor),
            hataEkrani.leadingAnchor.constraint(equalTo: webView.leadingAnchor),
            hataEkrani.trailingAnchor.constraint(equalTo: webView.trailingAnchor),
            hataEkrani.bottomAnchor.constraint(equalTo: webView.bottomAnchor),

            ilerleme.topAnchor.constraint(equalTo: webView.topAnchor),
            ilerleme.leadingAnchor.constraint(equalTo: webView.leadingAnchor),
            ilerleme.trailingAnchor.constraint(equalTo: webView.trailingAnchor),
            ilerleme.heightAnchor.constraint(equalToConstant: 2),
        ])
    }

    private func ilerlemeyiGoster() {
        let deger = Float(webView.estimatedProgress)
        ilerleme.setProgress(deger, animated: deger > ilerleme.progress)
        ilerleme.isHidden = deger >= 1
    }

    // MARK: Aşağı çekip yenileme

    @objc private func yenilemeIstendi() {
        // Yenileme kaydedilmemiş girdiyi siler: açık bir form penceresi
        // (<dialog>) ya da ekstre incelemesinde elle seçilmiş satırlar
        // (düzenleme alanları yalnız seçili satırda açılır) varsa yenilenmez.
        webView.evaluateJavaScript("!!document.querySelector('dialog[open], .import-row.selected')") { [weak self] sonuc, _ in
            self?.yenile(formAcik: (sonuc as? Bool) == true)
        }
    }

    private func yenile(formAcik: Bool) {
        if formAcik {
            yenileme.endRefreshing()
        } else if !sayfaGosterildi || webView.reload() == nil {
            webView.load(URLRequest(url: Kasa.adres))
        }
    }

    // MARK: Hatalar

    private func yuklemeBasarisiz(_ gorunum: WKWebView, _ hata: Error) {
        let h = hata as NSError
        // İptal (-999), indirmeye dönen yükleme (102) ve eklentinin üstlendiği
        // yükleme (204) hata sayılmaz.
        if h.domain == NSURLErrorDomain && h.code == NSURLErrorCancelled { return }
        if h.domain == "WebKitErrorDomain" && (h.code == 102 || h.code == 204) { return }
        if gorunum === webView {
            hataGoster(hata)
        } else {
            raporuKapat(uyari: "İnternet bağlantınızı kontrol edip tekrar deneyin.")
        }
    }

    private func hataGoster(_ hata: Error) {
        yenileme.endRefreshing()
        if sayfaGosterildi {
            uyar("Sayfa yenilenemedi", HataEkrani.mesaj(hata))   // eski sayfa yerinde, kullanılabilir
        } else {
            hataEkrani.goster(HataEkrani.mesaj(hata))
        }
    }

    func raporuKapat(uyari: String?) {
        guard let kap = rapor?.navigationController, kap.presentingViewController != nil else { return }
        // Açılış animasyonu sürerken (çevrimdışıyken yükleme milisaniyeler
        // içinde düşer) ya da üstünde bir ekran açılıp kapanırken UIKit
        // kapatmayı yok sayar ve uyarı hiç çıkmaz. Geçiş bitince yeniden denenir.
        if kap.transitionCoordinator != nil {
            Task { @MainActor [weak self] in
                try? await Task.sleep(nanoseconds: 300_000_000)
                self?.raporuKapat(uyari: uyari)
            }
            return
        }
        // Rapor sayfasının üstünde bekleyen bir JavaScript penceresi sayfayla
        // birlikte kapanır, düğmesine basılmaz; yanıtı burada verilir.
        // (Yanıtlanmış pencerede ikinci çağrı etkisizdir.)
        if kap.presentedViewController != nil, let yanit = jsVarsayilanYaniti {
            jsVarsayilanYaniti = nil
            yanit()
        }
        kap.dismiss(animated: true) { [weak self] in
            if let uyari { self?.uyar("Rapor açılamadı", uyari) }
        }
    }
}

// MARK: - Gezinti

extension KasaViewController: WKNavigationDelegate {
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void) {
        let hedef: GezintiKurali.Hedef
        if let cerceve = navigationAction.targetFrame {
            hedef = cerceve.isMainFrame ? .anaCerceve : .altCerceve
        } else {
            hedef = .yeniPencere
        }
        let istek = navigationAction.request
        switch GezintiKurali.karar(url: istek.url, hedef: hedef, indirilmeli: navigationAction.shouldPerformDownload) {
        case .izinVer:
            decisionHandler(.allow)   // yeni pencereyse ardından createWebViewWith gelir
        case .indir:
            decisionHandler(.download)
        case .yeniPenceredenIndir:
            decisionHandler(.cancel)
            webView.startDownload(using: istek) { [weak self] yeni in self?.indirme.takip(yeni) }
        case .disaridaAc(let url):
            decisionHandler(.cancel)
            UIApplication.shared.open(url)
        case .engelle:
            decisionHandler(.cancel)
        }
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse,
                 decisionHandler: @escaping @MainActor @Sendable (WKNavigationResponsePolicy) -> Void) {
        let yanit = navigationResponse.response as? HTTPURLResponse
        let anaEkran = webView === self.webView && navigationResponse.isForMainFrame
        if anaEkran, let kod = yanit?.statusCode, kod >= 500 {
            // Sunucu çalışmıyor (ör. 502): ham hata sayfası yerine kendi ekranımız.
            decisionHandler(.cancel)
            hataGoster(URLError(.badServerResponse))
            return
        }
        let indir = GezintiKurali.yanitIndirilmeli(
            icerikTuru: navigationResponse.response.mimeType,
            icerikYerlesimi: yanit?.value(forHTTPHeaderField: "Content-Disposition"),
            gosterilebilir: navigationResponse.canShowMIMEType,
            anaEkran: anaEkran)
        decisionHandler(indir ? .download : .allow)
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        indirme.takip(download)
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        indirme.takip(download)
    }

    func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
        guard webView === self.webView else { return }
        hataEkrani.isHidden = true
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard webView === self.webView else { return }
        sayfaGosterildi = true
        yenileme.endRefreshing()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        yuklemeBasarisiz(webView, error)
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        yuklemeBasarisiz(webView, error)
    }

    /// Web içerik süreci kapandı (bellek baskısı vb.): boş beyaz ekran yerine
    /// sayfa yeniden yüklenir. Oturum çerezde olduğu için giriş korunur.
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        guard webView === self.webView else {
            raporuKapat(uyari: nil)
            return
        }
        sayfaGosterildi = false
        yenileme.endRefreshing()
        if let son = sonCokme, Date().timeIntervalSince(son) < 10 {
            // Arka arkaya çöküyor: sonsuz döngü yerine hata ekranı.
            hataEkrani.goster("Sayfa beklenmedik biçimde kapandı. Tekrar deneyin.", baslik: "Sayfa açılamadı")
        } else if webView.reload() == nil {
            webView.load(URLRequest(url: Kasa.adres))
        }
        sonCokme = Date()
    }
}
