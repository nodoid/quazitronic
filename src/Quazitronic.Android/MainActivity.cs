using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Quazitronic.Android;

[Activity(
    Label = "Orictron",
    MainLauncher = true,
    Icon = "@drawable/icon",
    Theme = "@style/Theme.Splash",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.UiMode)]
public class MainActivity : Microsoft.Xna.Framework.AndroidGameActivity
{
    private QuazitronicGame? _game;

    protected override void OnCreate(Bundle? bundle)
    {
        base.OnCreate(bundle);
        _game = new QuazitronicGame(tiltSensor: new AndroidTiltSensor(this));
        var view = (View)_game.Services.GetService(typeof(View))!;
        SetContentView(view);
        _game.Run();
    }
}
