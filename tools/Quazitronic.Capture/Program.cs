using System;
using System.IO;
using Quazitronic;
using Quazitronic.Capture;
using Quazitronic.Persistence;

// Usage: dotnet run -c Release --project tools/Quazitronic.Capture -- <look> <output-dir> [--video] [--script name]
//   looks: iphone, ipad, mac, hd, android-phone, android-tablet, preview
//   stills go to <output-dir>/stills; --video records footage.mkv + soundtrack.wav instead.
string lookName = args.Length > 0 ? args[0] : "mac";
string outDir = Path.GetFullPath(args.Length > 1 ? args[1] : $"artifacts/capture/{lookName}");
bool video = Array.IndexOf(args, "--video") >= 0;
int si = Array.IndexOf(args, "--script");
string script = si >= 0 && si + 1 < args.Length ? args[si + 1] : (video ? "video" : "stills");
var look = Looks.Get(lookName);
Directory.CreateDirectory(outDir);

string saveDir = Path.Combine(outDir, "save");
if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true);
var save = new SaveData { Graphics = "Enhanced", Sound = true, Music = true };
save.Scores.Add(new ScoreEntry { Score = 12450, Deck = 4, Date = "2026-10-01" });
new SaveStore(saveDir).Save(save);

var director = new Director(outDir, look, video, script);
using var game = new QuazitronicGame(saveDir, director, mobileLayout: look.Mobile, tiltSensor: look.Mobile ? new StillTilt() : null)
{
    ForcedPixelScale = look.PixelScale,
};
game.ForcedVirtualWidth = look.VirtualWidth;
game.Random = new Random(1986);
director.Attach(game);
game.Run();

internal sealed record Look(string Name, int VirtualWidth, int PixelScale, bool Mobile);

internal static class Looks
{
    public static Look Get(string name) => name switch
    {
        "iphone" => new("iphone", 487, 6, true),            // 2922x1344 -> 2868x1320 / 2622x1206 / 2778x1284
        "ipad" => new("ipad", 299, 10, true),               // 2990x2240 -> 2752x2064
        "mac" => new("mac", 360, 8, false),                 // 2880x1792 -> 2880x1800
        "hd" => new("hd", 398, 5, false),                   // 1990x1120 -> 1920x1080
        "android-phone" => new("android-phone", 398, 5, true),
        "android-tablet" => new("android-tablet", 358, 8, true), // 2864x1792 -> 2560x1600
        "video-iphone" => new("video-iphone", 486, 4, true), // 1944x896 -> 1920x886
        "video-ipad" => new("video-ipad", 299, 6, true),     // 1794x1344 -> 1600x1200
        "video-mac" => new("video-mac", 398, 5, false),      // 1990x1120 -> 1920x1080
        "preview" => new("preview", 358, 3, false),
        "icon" => new("icon", 224, 5, false),             // 1120x1120 -> 1024x1024
        "preview-phone" => new("preview-phone", 487, 3, true),
        _ => throw new ArgumentException("unknown look " + name),
    };
}

/// <summary>A motion sensor that exists (so phone shots show the tilt layout); the autopilot steers.</summary>
internal sealed class StillTilt : Quazitronic.Input.ITiltSensor
{
    public bool IsAvailable => true;
    public void Start() { }
    public void Stop() { }
    public bool TryRead(out Microsoft.Xna.Framework.Vector3 gravity) { gravity = default; return false; }
}
