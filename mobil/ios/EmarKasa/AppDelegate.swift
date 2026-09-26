import UIKit

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {
    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // Önceki açılıştan kalmış indirilen dosyalar silinir (yedek dosyası
        // bütün veritabanını içerir).
        IndirmeYoneticisi.eskileriTemizle()
        return true
    }
}
