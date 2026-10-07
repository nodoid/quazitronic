using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orictron.Emulation;
using Orictron.Game;
using Orictron.Screens;

namespace Orictron.Capture;

/// <summary>
/// Scripts the real game for store assets: a list of steps (a screen, the self-playing enhanced
/// game, or the original tape in the emulator) held for a few seconds each, grabbing stills at
/// set moments or recording every frame to a lossless video with its soundtrack rendered
/// sample-exact by the game's own mixer.
/// </summary>
internal sealed class Director : ICaptureDirector
{
    public const float Fps = 30f;

    private sealed record Shot(float At, string Name);

    private sealed record Step(string Name, float Seconds, Action<OrictronGame> Setup, Shot[]? Shots = null, Action<OrictronGame, float>? During = null);

    private readonly string _outDir;
    private readonly Look _look;
    private readonly bool _video;
    private readonly List<Step> _steps;
    private readonly List<float> _audio = new();
    private Process? _ffmpeg;
    private Stream? _ffmpegIn;
    private Color[]? _pixels;
    private int _frame;
    private int _stepIndex = -1;
    private float _stepTime;
    private readonly HashSet<string> _taken = new();

    public Director(string outDir, Look look, bool video, string script)
    {
        _outDir = outDir;
        _look = look;
        _video = video;
        Directory.CreateDirectory(Path.Combine(outDir, "stills"));
        _steps = script switch
        {
            "check" => CheckScript(),
            "phone" => PhoneScript(),
            "video" => VideoScript(),
            "candidates" => CandidatesScript(),
            "icon" => new List<Step> { new("icon", 0.3f, g => g.ChangeScreen(new IconScreen(g) { LogoPath = Path.Combine(outDir, "stills", "logo.png") }), new[] { new Shot(0.2f, "icon") }) },
            _ => StillsScript(),
        };
    }

    public float FixedDelta => 1f / Fps;

    public void Attach(OrictronGame game)
    {
        if (_video) game.Audio.OfflineSink = s => { foreach (float v in s) _audio.Add(v); };
        else game.Audio.Muted = true;
    }

    // ------------------------------------------------------------------ building blocks

    private static Action<OrictronGame> Intro(bool enhanced) => g =>
    {
        g.SetEnhanced(enhanced);
        g.ChangeScreen(new IntroScreen(g));
    };

    private static Action<OrictronGame> Instructions(int page, bool enhanced) => g =>
    {
        g.SetEnhanced(enhanced);
        g.ChangeScreen(new InstructionsScreen(g) { StartPage = page });
    };

    /// <summary>The enhanced game playing itself, fast-forwarded silently by <paramref name="skipFrames"/> or until <paramref name="until"/>.</summary>
    private static Action<OrictronGame> Play(int seed, int skipFrames = 0, Func<Session, bool>? until = null, int afterUntil = 0) => g =>
    {
        g.SetEnhanced(true);
        var ps = new PlayScreen(g, seed, autoplay: true) { ShowDemoLabel = false, ShowTouchControls = true };
        g.ChangeScreen(ps);
        FastForward(ps, skipFrames, until, afterUntil);
    };

    private static void FastForward(PlayScreen ps, int frames, Func<Session, bool>? until, int after)
    {
        var view = ps.DeckViewer;
        var sounds = view.Sounds;
        view.Sounds = null;
        for (int i = 0; i < frames; i++) view.Step(default);
        if (until != null)
        {
            for (int i = 0; i < 25 * 60 * 20 && !until(ps.Session) && !ps.Session.Finished; i++) view.Step(default);
            for (int i = 0; i < after; i++) view.Step(default);
        }
        view.Sounds = sounds;
    }

    /// <summary>The original tape in the emulator, run on silently to <paramref name="seconds"/> with optional key presses.</summary>
    private static Action<OrictronGame> Original(float seconds, Action<OricMachine>? keys = null) => g =>
    {
        g.SetEnhanced(false);
        var os = new OriginalScreen(g);
        g.ChangeScreen(os);
        var m = os.Machine;
        keys?.Invoke(m);
        if (keys == null) m.Run((long)(seconds * OricMachine.CpuHz));
        m.Ay.Flush(m.Cycles);
    };

    /// <summary>Starts a game on the tape and plays a little of it by holding keys.</summary>
    private static void TapeGame(OricMachine m)
    {
        void Hold(OricKey k, float s) { m.SetKey(k, true); m.Run((long)(s * OricMachine.CpuHz)); m.SetKey(k, false); }
        m.Run(2 * OricMachine.CpuHz);
        Hold(OricKey.Space, 0.2f);
        m.Run(OricMachine.CpuHz);
        Hold(OricKey.Right, 1.2f);
        Hold(OricKey.Down, 0.8f);
    }

    // ------------------------------------------------------------------ scripts

    private List<Step> CheckScript() => new()
    {
        new("intro", 2.0f, Intro(true), new[] { new Shot(1.8f, "intro") }),
        new("intro-orig", 0.6f, Intro(false), new[] { new Shot(0.5f, "intro-original") }),
        new("howto0", 0.4f, Instructions(0, true), new[] { new Shot(0.3f, "howto-0") }),
        new("howto1", 0.4f, Instructions(1, true), new[] { new Shot(0.3f, "howto-1") }),
        new("howto2", 0.6f, Instructions(2, true), new[] { new Shot(0.5f, "howto-2") }),
        new("howto2o", 0.4f, Instructions(2, false), new[] { new Shot(0.3f, "howto-2-original") }),
        new("play", 4.0f, Play(1234, 25), new[] { new Shot(1.0f, "play-1"), new Shot(3.8f, "play-2") }),
        new("brief", 0.8f, Play(1234, 0, s => s.View == View.Briefing, 10), new[] { new Shot(0.6f, "briefing") }),
        new("transfer", 2.5f, Play(1234, 0, s => s.View == View.Transfer, 60), new[] { new Shot(2.3f, "transfer") }),
        new("deck3", 3.0f, Play(1234, 0, s => s.DeckIndex >= 2, 40), new[] { new Shot(2.8f, "deck-3") }),
        new("end", 1.5f, Play(77, 0, s => s.View == View.End, 30), new[] { new Shot(1.3f, "end") }),
        new("orig-title", 0.5f, Original(3), new[] { new Shot(0.4f, "original-title") }),
        new("orig-play", 1.0f, Original(0, TapeGame), new[] { new Shot(0.9f, "original-play") }),
        new("done", 0.1f, Intro(true)),
    };

    private List<Step> PhoneScript() => new()
    {
        new("intro", 1.0f, Intro(true), new[] { new Shot(0.9f, "phone-intro") }),
        new("play", 2.0f, Play(4242, 60), new[] { new Shot(1.8f, "phone-play") }),
        new("transfer", 2.5f, Play(4242, 0, s => s.View == View.Transfer, 60), new[] { new Shot(2.3f, "phone-transfer") }),
        new("orig", 1.0f, Original(0, TapeGame), new[] { new Shot(0.9f, "phone-original") }),
        new("pause", 0.5f, g => { Original(0, TapeGame)(g); g.Input.Force(Orictron.Input.Pad.Pause, true); }, new[] { new Shot(0.4f, "phone-pause") },
            During: (g, t) => { if (t > 0.1f) g.Input.Force(Orictron.Input.Pad.Pause, false); }),
        new("done", 0.1f, Intro(true)),
    };

    private const int Seed = 372; // a demo game that gets to deck 4 with plenty of captures

    /// <summary>A predicate that also needs <paramref name="minFrames"/> frames to have passed.</summary>
    private static Func<Session, bool> After(int minFrames, Func<Session, bool> also)
    {
        int n = 0;
        return s => ++n >= minFrames && also(s);
    }

    private static bool Shooting(Session s) => s.View == View.Deck && Array.Exists(s.BulletOn, b => b) && !s.GrappleLit && s.Flash == 0;

    private List<Step> StillsScript() => new()
    {
        new("intro", 2.4f, Intro(true), new[] { new Shot(2.2f, "01-intro") }),
        new("play", 1.6f, Play(Seed, 0, After(160, Shooting), 2), new[] { new Shot(1.4f, "02-deck") }),
        new("combat", 1.2f, Play(Seed, 0, After(10, s => s.DeckIndex >= 1 && s.Booms.Count > 0), 1), new[] { new Shot(0.2f, "03-combat") }),
        new("briefing", 1.0f, Play(Seed, 0, After(2700, s => s.View == View.Briefing), 14), new[] { new Shot(0.8f, "04-briefing") }),
        new("transfer", 1.0f, Play(Seed, 0, After(2700, s => s.View == View.Transfer), 95), new[] { new Shot(0.8f, "05-transfer") }),
        new("deck", 1.6f, Play(Seed, 0, After(10, s => s.DeckIndex >= 3 && Shooting(s)), 2), new[] { new Shot(1.4f, "06-deep") }),
        new("original-play", 1.2f, Original(0, TapeGame), new[] { new Shot(1.1f, "07-original") }),
        new("howto", 0.8f, Instructions(2, true), new[] { new Shot(0.7f, "08-droids") }),
        new("original-title", 0.6f, Original(2.6f), new[] { new Shot(0.5f, "original-title") }),
        new("captured", 1.4f, Play(Seed, 0, After(10, s => s.View == View.Captured), 10), new[] { new Shot(1.2f, "captured") }),
        new("done", 0.1f, Intro(true)),
    };

    private List<Step> CandidatesScript()
    {
        var steps = new List<Step>();
        var shots = new List<Shot>();
        for (int i = 0; i < 20; i++) shots.Add(new Shot(1 + i * 1.5f, $"cand-{i:00}"));
        steps.Add(new("cand", 31, Play(4242, 30), shots.ToArray()));
        return steps;
    }

    /// <summary>A ~29 second app preview: title, play, a transfer battle, a later deck, and the original tape.</summary>
    private List<Step> VideoScript() => new()
    {
        new("intro", 3.2f, Intro(true)),
        new("play", 6.5f, Play(Seed, 120)),
        new("transfer", 6.0f, Play(Seed, 0, After(2700, s => s.View == View.Briefing), 30)),
        new("deck", 5.0f, Play(Seed, 0, After(10, s => s.DeckIndex >= 3), 60)),
        new("original-title", 1.6f, Intro(false)),
        new("original", 4.6f, Original(0, TapeGame)),
        new("end", 2.1f, Intro(true)),
    };

    // ------------------------------------------------------------------ driving

    public void BeforeUpdate(OrictronGame game)
    {
        if (_stepIndex < 0 || _stepTime >= _steps[_stepIndex].Seconds)
        {
            _stepIndex++;
            if (_stepIndex >= _steps.Count)
            {
                Finish(game);
                return;
            }
            _stepTime = 0;
            var step = _steps[_stepIndex];
            step.Setup(game);
            Console.WriteLine($"[{_look.Name}] {step.Name}");
        }
        _steps[_stepIndex].During?.Invoke(game, _stepTime);
        _stepTime += 1 / Fps;
    }

    public void AfterDraw(OrictronGame game, RenderTarget2D frame)
    {
        if (_stepIndex < 0 || _stepIndex >= _steps.Count) return;
        if (_video)
        {
            WriteFrame(frame);
            _frame++;
        }
        var step = _steps[_stepIndex];
        if (step.Shots == null) return;
        foreach (var shot in step.Shots)
        {
            if (_stepTime < shot.At || !_taken.Add(shot.Name)) continue;
            using var f = File.Create(Path.Combine(_outDir, "stills", shot.Name + ".png"));
            frame.SaveAsPng(f, frame.Width, frame.Height);
        }
    }

    private void Finish(OrictronGame game)
    {
        if (_video) StopVideo();
        game.Exit();
    }

    private void WriteFrame(RenderTarget2D frame)
    {
        if (_ffmpeg == null)
        {
            var psi = new ProcessStartInfo("ffmpeg",
                $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {frame.Width}x{frame.Height} -r {Fps} -i - " +
                $"-c:v ffv1 -pix_fmt bgr0 \"{Path.Combine(_outDir, "footage.mkv")}\"")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            _ffmpeg = Process.Start(psi)!;
            _ffmpegIn = _ffmpeg.StandardInput.BaseStream;
            _pixels = new Color[frame.Width * frame.Height];
        }
        frame.GetData(_pixels!);
        _ffmpegIn!.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
    }

    private void StopVideo()
    {
        _ffmpegIn?.Flush();
        _ffmpegIn?.Dispose();
        _ffmpeg?.WaitForExit();
        _ffmpeg = null;
        const int rate = Audio.AudioEngine.Rate;
        using var wav = new BinaryWriter(File.Create(Path.Combine(_outDir, "soundtrack.wav")));
        int n = _audio.Count;
        wav.Write("RIFF"u8); wav.Write(36 + n * 2); wav.Write("WAVE"u8);
        wav.Write("fmt "u8); wav.Write(16); wav.Write((short)1); wav.Write((short)1);
        wav.Write(rate); wav.Write(rate * 2); wav.Write((short)2); wav.Write((short)16);
        wav.Write("data"u8); wav.Write(n * 2);
        foreach (float v in _audio) wav.Write((short)(Math.Clamp(v, -1, 1) * short.MaxValue));
        Console.WriteLine($"footage: {_frame} frames ({_frame / Fps:F2}s), {n / (double)rate:F2}s audio");
    }
}
