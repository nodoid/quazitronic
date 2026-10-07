using Foundation;
using UIKit;

namespace Orictron.iOS;

[Register("AppDelegate")]
internal sealed class Program : UIApplicationDelegate
{
    private static OrictronGame? _game;

    public override void FinishedLaunching(UIApplication app)
    {
        _game = new OrictronGame(tiltSensor: new IosTiltSensor())
        {
            // Keep the picture clear of the home indicator.
            BottomInsetProvider = () =>
                (int)((app.KeyWindow?.SafeAreaInsets.Bottom ?? 0) * UIScreen.MainScreen.NativeScale),
        };
        _game.Run();
    }

    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(Program));
}
