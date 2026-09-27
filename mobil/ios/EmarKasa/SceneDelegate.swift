import UIKit

final class SceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?
    private var kilit: UygulamaKilidi?

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession,
               options connectionOptions: UIScene.ConnectionOptions) {
        guard let sahne = scene as? UIWindowScene else { return }
        let pencere = UIWindow(windowScene: sahne)
        pencere.rootViewController = KasaViewController()
        pencere.tintColor = .kasaYesil
        pencere.makeKeyAndVisible()
        window = pencere

        let kilit = UygulamaKilidi(sahne: sahne)
        kilit.acilista()
        self.kilit = kilit
    }

    func sceneWillResignActive(_ scene: UIScene) {
        kilit?.etkinligiKaybetti()
    }

    func sceneDidEnterBackground(_ scene: UIScene) {
        kilit?.arkaPlanaGecti()
    }

    func sceneDidBecomeActive(_ scene: UIScene) {
        kilit?.etkinlesti()
    }
}
