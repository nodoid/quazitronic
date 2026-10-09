using Foundation;
using UIKit;

namespace Quazitronic.iOS;

[Register("AppDelegate")]
internal sealed class Program : UIApplicationDelegate
{
    // iOS 27 refuses to launch apps without the scene lifecycle, so the game starts in SceneDelegate.
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;

    public override UISceneConfiguration GetConfiguration(
        UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Default", connectingSceneSession.Role) { DelegateType = typeof(SceneDelegate) };

    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(Program));
}

[Register("SceneDelegate")]
internal sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    private static QuazitronicGame? _game;

    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (_game is not null || scene is not UIWindowScene windowScene)
            return;

        _game = new QuazitronicGame(tiltSensor: new IosTiltSensor())
        {
            // Keep the picture clear of the home indicator.
            BottomInsetProvider = () =>
                (int)((Window?.SafeAreaInsets.Bottom ?? 0) * (Window?.Screen.NativeScale ?? 1)),
        };
        _game.Run();

        // MonoGame makes its own scene-less window; hand it to the scene so it is shown.
        if (_game.Services.GetService(typeof(UIViewController)) is UIViewController controller)
        {
            Window = controller.View?.Window ?? new UIWindow(windowScene) { RootViewController = controller };
            Window.WindowScene = windowScene;
            Window.MakeKeyAndVisible();
        }
    }
}
