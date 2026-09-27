import LocalAuthentication
import UIKit

/// Uygulama kilidi. Kasa defteri telefonda açık kalmasın diye uygulama
/// açılırken ve bir dakikadan uzun arka planda kaldıktan sonra Face ID ya da
/// Touch ID ister; ikisi de yoksa telefonun şifresi sorulur. Telefonda şifre
/// hiç yoksa kilit devre dışıdır. Ayarlar > Emar Kasa > "Face ID kilidi" ile
/// kapatılabilir (Settings.bundle, varsayılan açık).
///
/// Uygulama etkinliğini her kaybettiğinde (çoklu görev ekranı, Denetim
/// Merkezi) içerik bir örtüyle gizlenir; böylece uygulama değiştiricideki
/// görüntüde de kasa rakamları görünmez.
@MainActor
final class UygulamaKilidi {
    static let ayarAnahtari = "uygulamaKilidi"
    /// Bu süreden kısa arka plan (ör. bir mesaja bakıp dönmek) kilitlemez.
    static let esikSaniye: TimeInterval = 60

    private let pencere: UIWindow
    private let ekran = KilitEkrani()
    /// Kimlik doğrulaması bekleniyor mu?
    private var kilitli = false
    /// Sistem Face ID penceresi açıkken uygulama etkinliğini kaybedip geri
    /// kazanır; o geçişler yok sayılır, yoksa doğrulama döngüye girer.
    private var dogrulaniyor = false
    private var arkaPlanaGecis: Date?

    init(sahne: UIWindowScene) {
        UserDefaults.standard.register(defaults: [Self.ayarAnahtari: true])
        pencere = UIWindow(windowScene: sahne)
        pencere.windowLevel = .alert + 1
        pencere.rootViewController = ekran
        pencere.isHidden = true
        ekran.kilidiAc = { [weak self] in self?.dogrula() }
    }

    /// Ayar açık ve telefonda kilit kurulu mu?
    private var etkin: Bool {
        guard UserDefaults.standard.bool(forKey: Self.ayarAnahtari) else { return false }
        var hata: NSError?
        return LAContext().canEvaluatePolicy(.deviceOwnerAuthentication, error: &hata)
    }

    /// Soğuk açılış: site görünmeden önce kilitlenir. Doğrulama, sahne
    /// etkinleşince (`etkinlesti`) başlar.
    func acilista() {
        guard etkin else { return }
        kilitli = true
        goster()
    }

    /// sceneWillResignActive
    func etkinligiKaybetti() {
        guard !dogrulaniyor else { return }
        goster()
    }

    /// sceneDidEnterBackground
    func arkaPlanaGecti() {
        guard !dogrulaniyor else { return }
        arkaPlanaGecis = Date()
        goster()
    }

    /// sceneDidBecomeActive
    func etkinlesti() {
        guard !dogrulaniyor else { return }
        if let gecis = arkaPlanaGecis {
            arkaPlanaGecis = nil
            if Date().timeIntervalSince(gecis) >= Self.esikSaniye, etkin { kilitli = true }
        }
        if kilitli { dogrula() } else { gizle() }
    }

    private func dogrula() {
        guard !dogrulaniyor else { return }
        // Ayar kapatıldıysa ya da telefonun şifresi kaldırıldıysa kilit açılır.
        guard etkin else {
            kilitli = false
            gizle()
            return
        }
        dogrulaniyor = true
        goster()
        ekran.durum(.dogrulaniyor)
        let baglam = LAContext()
        baglam.localizedCancelTitle = "Vazgeç"
        baglam.evaluatePolicy(.deviceOwnerAuthentication,
                              localizedReason: "Kasa defterinizi açmak için kimliğinizi doğrulayın.") { basarili, _ in
            Task { @MainActor [weak self] in
                guard let self else { return }
                self.dogrulaniyor = false
                if basarili {
                    self.kilitli = false
                    self.gizle()
                } else {
                    self.ekran.durum(.kilitli)
                }
            }
        }
    }

    private func goster() {
        ekran.durum(kilitli ? .kilitli : .ortu)
        pencere.isHidden = false
    }

    private func gizle() {
        pencere.isHidden = true
    }
}

/// Kilit ve gizleme örtüsü: uygulama adı, kilit simgesi ve gerektiğinde
/// "Kilidi aç" düğmesi.
final class KilitEkrani: UIViewController {
    enum Durum { case ortu, kilitli, dogrulaniyor }

    var kilidiAc: (() -> Void)?

    private let simge = UIImageView(image: UIImage(systemName: "lock.fill"))
    private let baslik = UILabel()
    private let dugme = UIButton(type: .system)

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .kasaKrem

        simge.tintColor = .kasaYesil
        simge.contentMode = .scaleAspectFit
        simge.preferredSymbolConfiguration = UIImage.SymbolConfiguration(pointSize: 44, weight: .semibold)

        baslik.text = "Emar Kasa"
        baslik.font = .systemFont(ofSize: 28, weight: .bold)
        baslik.textColor = .kasaYesil
        baslik.textAlignment = .center

        var ayar = UIButton.Configuration.filled()
        ayar.title = "Kilidi aç"
        ayar.baseBackgroundColor = .kasaYesil
        ayar.baseForegroundColor = .kasaKrem
        ayar.cornerStyle = .medium
        ayar.contentInsets = NSDirectionalEdgeInsets(top: 12, leading: 28, bottom: 12, trailing: 28)
        dugme.configuration = ayar
        dugme.addAction(UIAction { [weak self] _ in self?.kilidiAc?() }, for: .touchUpInside)

        let yigin = UIStackView(arrangedSubviews: [simge, baslik, dugme])
        yigin.axis = .vertical
        yigin.alignment = .center
        yigin.spacing = 20
        yigin.setCustomSpacing(32, after: baslik)
        yigin.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(yigin)
        NSLayoutConstraint.activate([
            yigin.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            yigin.centerYAnchor.constraint(equalTo: view.centerYAnchor),
        ])
    }

    func durum(_ yeni: Durum) {
        loadViewIfNeeded()
        // Düğme yalnız kilitliyken görünür; örtüdeyken ve sistem penceresi
        // açıkken gizli kalır, yer kaplamaya devam eder (yerleşim zıplamasın).
        dugme.alpha = yeni == .kilitli ? 1 : 0
        dugme.isEnabled = yeni == .kilitli
        simge.image = UIImage(systemName: yeni == .ortu ? "lock.shield" : "lock.fill")
    }
}
