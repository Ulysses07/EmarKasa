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

    /// Önceki açılışlardan kalan dosyaları siler.
    static func eskileriTemizle() {
        try? FileManager.default.removeItem(at: klasor)
    }

    func takip(_ indirme: WKDownload) {
        indirme.delegate = self
    }

    private func siradakiniGoster() {
        guard gosterilen == nil, !bekleyenler.isEmpty, let ust = sahip?.enUstteki else { return }
        let dosya = bekleyenler.removeFirst()
        gosterilen = dosya
        if dosya.pathExtension.lowercased() != "zip", QLPreviewController.canPreview(dosya as NSURL) {
            let onizleme = QLPreviewController()
            onizleme.dataSource = self
            onizleme.delegate = self
            ust.present(onizleme, animated: true)
        } else {
            let paylas = UIActivityViewController(activityItems: [dosya], applicationActivities: nil)
            paylas.completionWithItemsHandler = { [weak self] _, _, _, _ in self?.kapandi() }
            if let balon = paylas.popoverPresentationController {   // iPad
                balon.sourceView = ust.view
                balon.sourceRect = CGRect(x: ust.view.bounds.midX, y: ust.view.bounds.midY, width: 0, height: 0)
                balon.permittedArrowDirections = []
            }
            ust.present(paylas, animated: true)
        }
    }

    private func kapandi() {
        if let dosya = gosterilen { sil(dosya) }
        gosterilen = nil
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

extension IndirmeYoneticisi: QLPreviewControllerDataSource, QLPreviewControllerDelegate {
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
