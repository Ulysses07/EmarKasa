import UIKit

/// Site hiç açılamadığında (internet yok, sunucu kapalı) web görünümünün
/// üstünde gösterilen ekran: kısa açıklama ve "Tekrar dene" düğmesi.
final class HataEkrani: UIView {
    var tekrarDene: () -> Void = {}

    private let baslikEtiketi = UILabel()
    private let mesajEtiketi = UILabel()

    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = .kasaKrem

        let simge = UIImageView(image: UIImage(systemName: "wifi.exclamationmark"))
        simge.tintColor = .kasaYesil
        simge.preferredSymbolConfiguration = UIImage.SymbolConfiguration(pointSize: 48)

        baslikEtiketi.font = .preferredFont(forTextStyle: .title2)
        mesajEtiketi.font = .preferredFont(forTextStyle: .body)
        mesajEtiketi.textColor = .secondaryLabel
        for etiket in [baslikEtiketi, mesajEtiketi] {
            etiket.adjustsFontForContentSizeCategory = true
            etiket.textAlignment = .center
            etiket.numberOfLines = 0
        }

        var ayar = UIButton.Configuration.filled()
        ayar.title = "Tekrar dene"
        ayar.baseBackgroundColor = .kasaYesil
        ayar.cornerStyle = .large
        let dugme = UIButton(configuration: ayar)
        dugme.addTarget(self, action: #selector(dugmeyeBasildi), for: .touchUpInside)

        let yigin = UIStackView(arrangedSubviews: [simge, baslikEtiketi, mesajEtiketi, dugme])
        yigin.axis = .vertical
        yigin.alignment = .center
        yigin.spacing = 12
        yigin.setCustomSpacing(24, after: mesajEtiketi)
        yigin.translatesAutoresizingMaskIntoConstraints = false
        addSubview(yigin)
        NSLayoutConstraint.activate([
            yigin.centerYAnchor.constraint(equalTo: centerYAnchor),
            yigin.leadingAnchor.constraint(equalTo: layoutMarginsGuide.leadingAnchor, constant: 16),
            yigin.trailingAnchor.constraint(equalTo: layoutMarginsGuide.trailingAnchor, constant: -16),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) kullanılmıyor")
    }

    func goster(_ mesaj: String, baslik: String = "Bağlantı kurulamadı") {
        baslikEtiketi.text = baslik
        mesajEtiketi.text = mesaj
        isHidden = false
    }

    @objc private func dugmeyeBasildi() {
        tekrarDene()
    }

    /// Yükleme hatasının kullanıcıya gösterilecek açıklaması.
    static func mesaj(_ hata: Error) -> String {
        guard let kod = (hata as? URLError)?.code else { return "Sayfa açılamadı. Tekrar deneyin." }
        switch kod {
        case .notConnectedToInternet, .networkConnectionLost, .dataNotAllowed, .internationalRoamingOff:
            return "İnternet bağlantınızı kontrol edip tekrar deneyin."
        case .timedOut, .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed, .badServerResponse:
            return "Kasa sunucusuna şu anda ulaşılamıyor. Biraz sonra tekrar deneyin."
        case .secureConnectionFailed, .serverCertificateUntrusted, .serverCertificateHasBadDate,
             .serverCertificateNotYetValid, .serverCertificateHasUnknownRoot:
            return "Güvenli bağlantı kurulamadı. Telefonun tarih ve saatini kontrol edip tekrar deneyin."
        default:
            return "Sayfa açılamadı. Tekrar deneyin."
        }
    }
}
