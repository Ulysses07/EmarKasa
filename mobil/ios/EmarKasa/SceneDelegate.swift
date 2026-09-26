import UIKit

final class SceneDelegate: UIResponder, UIWindowSceneDelegate {
    var window: UIWindow?

    func scene(_ scene: UIScene, willConnectTo session: UISceneSession,
               options connectionOptions: UIScene.ConnectionOptions) {
        guard let sahne = scene as? UIWindowScene else { return }
        let pencere = UIWindow(windowScene: sahne)
        pencere.rootViewController = KasaViewController()
        pencere.tintColor = .kasaYesil
        pencere.makeKeyAndVisible()
        window = pencere
    }
}
