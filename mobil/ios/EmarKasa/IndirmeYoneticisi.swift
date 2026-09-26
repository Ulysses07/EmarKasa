import QuickLook
import UIKit
import WebKit

/// Web ekranındaki indirmeler: yedek (zip), Excel/CSV raporu, alış belgeleri
/// ve ekstre PDF'i. Dosya geçici klasöre iner; PDF, görsel ve tablolar
/// önizlenir, yedek paylaşım menüsüyle (Dosyalar'a Kaydet) verilir. Ekran
/// kapanınca dosya silinir.
@MainActor
final class IndirmeYoneticisi: NSObject {
    /// Önizleme ve uyarıların üstünde açılacağı ekran.
    weak var sahip: UIViewController?

    private static let klasor = FileManager.default.temporaryDirectory
        .appendingPathComponent("Indirilenler", isDirectory: true)

    private var hedefler: [WKDownload: URL] = [:]   // süren indirme → yazıldığı dosya
    private var bekleyenler: [URL] = []             // gösterilmeyi bekleyen dosyalar
    private var gosterilen: URL?                    // önizlemede ya da paylaşımda olan dosya
    private weak var ekran: UIViewController?       // o dosyanın önizleme ya da paylaşım ekranı
    private var denemeBekliyor = false              // siradakiniGoster yeniden çağrılacak
    private var dusenSunum = 0                      // üst üste tutmayan sunum sayısı

    /// Önceki açılışlardan kalan dosyaları siler.
    static func eskileriTemizle() {
        try? FileManager.default.removeItem(at: klasor)
    }

    func takip(_ indirme: WKDownload) {
        indirme.delegate = self
    }

    private func siradakiniGoster() {
        if let dosya = gosterilen {
            // Ekran duruyor (açık ya da açılmayı bekliyor): kapanınca sıradaki gelir.
            if ekran != nil { return }
            // Ekran, kapanış bildirimi gelmeden yok oldu: sunum tutmadı ya da
            // üstünde açıldığı rapor sayfasıyla birlikte kapandı. Dosya yeniden
            // gösterilir; yoksa sıra uygulama kapanana dek kilitli kalırdı.
            gosterilen = nil
            bekleyenler.insert(dosya, at: 0)
            dusenSunum += 1
        }
        guard !bekleyenler.isEmpty, let ust = sahip?.enUstteki else { return }
        // O an bir ekran açılıyor ya da kapanıyorsa (az önce çıkan bir uyarı,
        // kapanmakta olan paylaşım menüsü) UIKit yeni sunumu yalnız günlüğe
        // yazıp yok sayar. Ekranda bir uyarı varsa dosya onu örtmez. Geçiş
        // bitince ya da uyarı kapanınca yeniden denenir.
        if ust.transitionCoordinator != nil || ust.presentedViewController != nil || ust is UIAlertController {
            birazSonraDene()
            return
        }
        let dosya = bekleyenler.removeFirst()
        let yeni: UIViewController
        if dosya.pathExtension.lowercased() != "zip", QLPreviewController.canPreview(dosya as NSURL) {
            let onizleme = QLPreviewController()
            onizleme.dataSource = self
            onizleme.delegate = self
            yeni = onizleme
        } else {
            let paylas = UIActivityViewController(activityItems: [dosya], applicationActivities: nil)
            paylas.completionWithItemsHandler = { [weak self] _, _, _, _ in self?.kapandi() }
            if let balon = paylas.popoverPresentationController {   // iPad
                balon.sourceView = ust.view
                balon.sourceRect = CGRect(x: ust.view.bounds.midX, y: ust.view.bounds.midY, width: 0, height: 0)
                balon.permittedArrowDirections = []
            }
            yeni = paylas
        }
        gosterilen = dosya
        ekran = yeni
        ust.present(yeni, animated: true)
        // Sunumun tutup tutmadığı hemen sorulmaz: paylaşım menüsü sunumunu
        // hazır olana dek geciktirebilir, o arada presentingViewController
        // boştur ve ikinci bir menü açılırdı. Tutmayan sunumun ekranını kimse
        // tutmaz (`ekran` boşalır); biraz sonra yukarıdaki denetim dosyayı
        // yeniden sıraya alır. Üst üste tutmuyorsa sonraki indirmede denenir.
        if dusenSunum < 3 { birazSonraDene() }
    }

    private func birazSonraDene() {
        guard !denemeBekliyor else { return }
        denemeBekliyor = true
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 700_000_000)
            guard let self else { return }
            self.denemeBekliyor = false
            self.siradakiniGoster()
        }
    }

    private func kapandi() {
        if let dosya = gosterilen { sil(dosya) }
        gosterilen = nil
        ekran = nil
        dusenSunum = 0
        siradakiniGoster()
    }

    /// Dosyayı, içinde durduğu tek kullanımlık klasörle birlikte siler.
    private func sil(_ dosya: URL) {
        try? FileManager.default.removeItem(at: dosya.deletingLastPathComponent())
    }
}

extension IndirmeYoneticisi: WKDownloadDelegate {
    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse,
                  suggestedFilename: String,
                  completionHandler: @escaping @MainActor @Sendable (URL?) -> Void) {
        // Silinmiş belge (404) ya da süresi dolmuş oturum (401) boş bir yanıtla
        // gelir; kaydedilirse kullanıcının elinde adı "7" olan 0 baytlık bir
        // dosya kalır. Sayfanın ürettiği blob: dosyaları (yedek, Excel/CSV)
        // HTTP yanıtı değildir ya da 200'dür, etkilenmez. nil verilince
        // indirme iptal olur; didFailWithError iptali sessizce geçer.
        if let http = response as? HTTPURLResponse,
           let hata = GezintiKurali.indirmeHatasi(durumKodu: http.statusCode) {
            sahip?.uyar("İndirme tamamlanamadı", hata)
            completionHandler(nil)
            return
        }
        // Her indirme kendi klasörüne: hedef dosya önceden var olmamalı, özgün
        // ad (Türkçe harfler dahil) de korunur.
        let klasor = Self.klasor.appendingPathComponent(UUID().uuidString, isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: klasor, withIntermediateDirectories: true)
        } catch {
            sahip?.uyar("İndirme tamamlanamadı", "Dosya kaydedilemedi. Telefonda yer olup olmadığını kontrol edin.")
            completionHandler(nil)
            return
        }
        let hedef = klasor.appendingPathComponent(GezintiKurali.guvenliAd(suggestedFilename), isDirectory: false)
        hedefler[download] = hedef
        completionHandler(hedef)
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard let dosya = hedefler.removeValue(forKey: download) else { return }
        bekleyenler.append(dosya)
        siradakiniGoster()
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        if let dosya = hedefler.removeValue(forKey: download) { sil(dosya) }
        if (error as? URLError)?.code == .cancelled { return }
        sahip?.uyar("İndirme tamamlanamadı",
                    "Dosya indirilirken bir sorun oluştu. Bağlantınızı kontrol edip tekrar deneyin.")
    }
}

extension IndirmeYoneticisi: QLPreviewControllerDataSource, @preconcurrency QLPreviewControllerDelegate {
    func numberOfPreviewItems(in controller: QLPreviewController) -> Int {
        gosterilen == nil ? 0 : 1
    }

    func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem {
        (gosterilen ?? Self.klasor) as NSURL
    }

    func previewControllerDidDismiss(_ controller: QLPreviewController) {
        kapandi()
    }
}
