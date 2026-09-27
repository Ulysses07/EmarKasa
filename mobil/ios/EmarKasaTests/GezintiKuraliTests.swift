import XCTest
@testable import EmarKasa

/// Uygulama içinde yalnız kasa.emarglobal.com açılır; indirmeler ve dış
/// bağlantılar doğru yola gider.
final class GezintiKuraliTests: XCTestCase {
    private func karar(_ adres: String, _ hedef: GezintiKurali.Hedef = .anaCerceve,
                       indir: Bool = false) -> GezintiKurali.Karar {
        GezintiKurali.karar(url: URL(string: adres), hedef: hedef, indirilmeli: indir)
    }

    func testYalnizKasaSitesiIcerideAcilir() {
        XCTAssertEqual(karar("https://kasa.emarglobal.com/"), .izinVer)
        XCTAssertEqual(karar("https://KASA.emarglobal.com/#cards/5"), .izinVer)
        XCTAssertEqual(karar("https://kasa.emarglobal.com:443/"), .izinVer)
        XCTAssertEqual(karar("https://kasa.emarglobal.com/api/disari-aktar?bicim=html", .yeniPencere), .izinVer)
        XCTAssertEqual(karar("about:blank"), .izinVer)

        let dis = URL(string: "https://kasa.emarglobal.com@example.com/")!
        XCTAssertEqual(karar(dis.absoluteString), .disaridaAc(dis))
        XCTAssertEqual(karar("https://kasa.emarglobal.com.example.com/"),
                       .disaridaAc(URL(string: "https://kasa.emarglobal.com.example.com/")!))
        XCTAssertEqual(karar("https://kasa.emarglobal.com:8443/"),
                       .disaridaAc(URL(string: "https://kasa.emarglobal.com:8443/")!))
        XCTAssertEqual(karar("http://kasa.emarglobal.com/"),
                       .disaridaAc(URL(string: "http://kasa.emarglobal.com/")!))
        XCTAssertEqual(karar("https://example.com/", .yeniPencere), .disaridaAc(URL(string: "https://example.com/")!))
        XCTAssertEqual(karar("mailto:a@example.com"), .disaridaAc(URL(string: "mailto:a@example.com")!))
    }

    func testCerceveDisariyiAcamaz() {
        XCTAssertEqual(karar("https://example.com/", .altCerceve), .engelle)
        XCTAssertEqual(karar("file:///etc/hosts"), .engelle)
        XCTAssertEqual(karar("javascript:alert(1)"), .engelle)
        XCTAssertEqual(karar("kasa://ac"), .engelle)
    }

    func testIndirmeler() {
        // Sayfanın ürettiği yedek ve Excel/CSV dosyaları (a[download] + blob:).
        XCTAssertEqual(karar("blob:https://kasa.emarglobal.com/0f1e", indir: true), .indir)
        XCTAssertEqual(karar("blob:https://kasa.emarglobal.com/0f1e"), .engelle)
        XCTAssertEqual(karar("blob:https://example.com/0f1e", indir: true), .engelle)
        // Ekstre PDF'i (aynı pencere) ve alış belgesi (target=_blank + download).
        XCTAssertEqual(karar("https://kasa.emarglobal.com/api/ekstre-aktar/3/dosya", indir: true), .indir)
        XCTAssertEqual(karar("https://kasa.emarglobal.com/api/belgeler/7", .yeniPencere, indir: true),
                       .yeniPenceredenIndir)
        XCTAssertEqual(karar("data:text/plain,a", indir: true), .engelle)
    }

    func testYanitIndirilmeli() {
        XCTAssertTrue(GezintiKurali.yanitIndirilmeli(icerikTuru: "application/pdf",
                                                     icerikYerlesimi: "attachment; filename=a.pdf",
                                                     gosterilebilir: true, anaEkran: false))
        XCTAssertTrue(GezintiKurali.yanitIndirilmeli(icerikTuru: "application/zip", icerikYerlesimi: nil,
                                                     gosterilebilir: false, anaEkran: false))
        XCTAssertTrue(GezintiKurali.yanitIndirilmeli(icerikTuru: "application/json", icerikYerlesimi: nil,
                                                     gosterilebilir: true, anaEkran: true))
        XCTAssertFalse(GezintiKurali.yanitIndirilmeli(icerikTuru: "text/html", icerikYerlesimi: "inline",
                                                      gosterilebilir: true, anaEkran: true))
        XCTAssertFalse(GezintiKurali.yanitIndirilmeli(icerikTuru: "text/html", icerikYerlesimi: nil,
                                                      gosterilebilir: true, anaEkran: false))
    }

    func testIndirmeHatasi() {
        XCTAssertNil(GezintiKurali.indirmeHatasi(durumKodu: 200))
        XCTAssertNil(GezintiKurali.indirmeHatasi(durumKodu: 206))
        XCTAssertEqual(GezintiKurali.indirmeHatasi(durumKodu: 401),
                       "Oturumunuz sona ermiş. Sayfayı yenileyip yeniden giriş yapın.")
        XCTAssertEqual(GezintiKurali.indirmeHatasi(durumKodu: 404),
                       "Dosya bulunamadı, silinmiş olabilir. Sayfayı yenileyip tekrar deneyin.")
        XCTAssertNotNil(GezintiKurali.indirmeHatasi(durumKodu: 403))
        XCTAssertNotNil(GezintiKurali.indirmeHatasi(durumKodu: 500))
        XCTAssertNotNil(GezintiKurali.indirmeHatasi(durumKodu: 302))
    }

    func testGuvenliAd() {
        XCTAssertEqual(GezintiKurali.guvenliAd("kasa-yedek-2026-09-25.zip"), "kasa-yedek-2026-09-25.zip")
        XCTAssertEqual(GezintiKurali.guvenliAd("Şubat faturası.pdf"), "Şubat faturası.pdf")
        XCTAssertEqual(GezintiKurali.guvenliAd("../../gizli.pdf"), "gizli.pdf")
        XCTAssertEqual(GezintiKurali.guvenliAd("a:b.csv"), "a-b.csv")
        XCTAssertEqual(GezintiKurali.guvenliAd(" "), "kasa-dosyasi")
        XCTAssertEqual(GezintiKurali.guvenliAd(".."), "kasa-dosyasi")
        XCTAssertEqual(GezintiKurali.guvenliAd("/"), "kasa-dosyasi")
    }
}
