using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quazitronic.Audio;
using Quazitronic.Graphics;
using Quazitronic.Input;
using Quazitronic.Persistence;
using Quazitronic.Screens;

namespace Quazitronic;

/// <summary>
/// Orictron: the Oric Atmos port of Hewson's Quazatron, in two looks over one game - ORIGINAL,
/// recreating the Oric version's screen and sound, and ENHANCED, with new graphics and sound.
/// <para>
/// The picture is laid out in Oric pixels: always 224 high (the Oric's screen), and 240 or wider to
/// fill the display. It's drawn into a render target that is a whole multiple of that size, so the
/// original keeps square fat pixels while the enhanced look gets full resolution.
/// </para>
/// </summary>
public sealed class QuazitronicGame : Microsoft.Xna.Framework.Game
{
    public const int VirtualHeight = 224;
    public const int MinVirtualWidth = 240;
    public const int MaxVirtualWidth = 540;
    private const int MaxPixelScale = 10;

    private readonly GraphicsDeviceManager _graphics;
    private readonly SaveStore _store;
    private Gfx _gfx = null!;
    private SpriteBatch _batch = null!;
    private RenderTarget2D? _target;
    private int _pixelScale;
    private int _virtualWidth = 320;
    private Screen _screen = null!;
    private Rectangle _dest;
    private float _scale = 1f;
    private bool _resizing;
    private Point _windowedSize = new(1280, 800);

    /// <param name="saveDirectory">Override for the save folder (tests / portable builds).</param>
    /// <param name="tiltSensor">Device motion source on phones and tablets; null on desktop.</param>
    public QuazitronicGame(string? saveDirectory = null, ITiltSensor? tiltSensor = null)
        : this(saveDirectory, director: null, mobileLayout: null, tiltSensor)
    {
    }

    internal QuazitronicGame(string? saveDirectory, ICaptureDirector? director, bool? mobileLayout, ITiltSensor? tiltSensor = null)
    {
        Director = director;
        TiltSensor = tiltSensor;
        _graphics = new GraphicsDeviceManager(this) { GraphicsProfile = GraphicsProfile.HiDef, PreferMultiSampling = false };
        _store = new SaveStore(saveDirectory ?? SaveStore.DefaultDirectory());
        bool nativeMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
        IsMobile = mobileLayout ?? nativeMobile;
        Input.IsMobileLayout = IsMobile;

        if (nativeMobile)
        {
            _graphics.IsFullScreen = true;
            _graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = _windowedSize.X;
            _graphics.PreferredBackBufferHeight = _windowedSize.Y;
            _graphics.HardwareModeSwitch = false;
            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += OnClientSizeChanged;
            IsMouseVisible = true;
        }

        Window.Title = "Orictron";
        IsFixedTimeStep = false;
        _graphics.SynchronizeWithVerticalRetrace = true;
    }

    public bool IsMobile { get; }
    public ITiltSensor? TiltSensor { get; }
    public TiltController Tilt { get; } = new();
    public bool UseTilt => IsMobile && Save.TiltControls && TiltSensor?.IsAvailable == true;
    public bool HasTiltOption => IsMobile && TiltSensor?.IsAvailable == true;
    /// <summary>Set by the play screens while a tilt-steered game is running.</summary>
    internal bool TiltActive { get; set; }
    internal ICaptureDirector? Director { get; }
    internal Screen CurrentScreen => _screen;
    internal int? ForcedPixelScale { get; init; }
    internal int? ForcedVirtualWidth { get; set; }
    public Func<int>? BottomInsetProvider { get; init; }
    /// <summary>iOS apps must not quit themselves.</summary>
    public bool CanQuit => !IsMobile;

    public BitmapFont Font { get; private set; } = null!;
    public AudioEngine Audio { get; } = new();
    public Sounds Sounds { get; private set; } = null!;
    public Music Music { get; private set; } = null!;
    /// <summary>ORIGINAL mode's chip-style sound voices.</summary>
    public ChipSound Chip { get; } = new();
    public InputState Input { get; } = new();
    public SaveData Save { get; private set; } = new();
    public bool Enhanced => Save.Graphics != "Original";
    public int VirtualWidth => _virtualWidth;
    public float Clock { get; private set; }
    /// <summary>Seed source for new games (fixed by the capture tool).</summary>
    public Random Random { get; internal set; } = new();

    protected override void Initialize()
    {
        if (Director != null)
        {
            _graphics.SynchronizeWithVerticalRetrace = false;
            _graphics.ApplyChanges();
        }
        Save = _store.Load();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        Font = new BitmapFont(GraphicsDevice);
        _gfx = new Gfx(GraphicsDevice, Font);
        if (Director == null) Audio.Start();
        Sounds = new Sounds(Audio);
        Music = new Music(Audio);
        Audio.Chip = Chip;
        ApplySoundSettings();
        ChangeScreen(new IntroScreen(this));
    }

    protected override void UnloadContent()
    {
        Audio.Dispose();
        base.UnloadContent();
    }

    public void ChangeScreen(Screen screen)
    {
        _screen?.Leave();
        Input.TouchButtons.Clear();
        _screen = screen;
        _screen.Enter();
    }

    public void SetEnhanced(bool enhanced)
    {
        Save.Graphics = enhanced ? "Enhanced" : "Original";
        PersistSave();
    }

    public void SetSound(bool on)
    {
        Save.Sound = on;
        ApplySoundSettings();
        PersistSave();
    }

    public void SetMusic(bool on)
    {
        Save.Music = on;
        ApplySoundSettings();
        PersistSave();
    }

    public void SetTiltControls(bool tilt)
    {
        Save.TiltControls = tilt;
        PersistSave();
    }

    private void ApplySoundSettings()
    {
        Audio.Muted = !Save.Sound;
        Music.Enabled = Save.Sound && Save.Music;
        Music.Update();
    }

    public void PersistSave() => _store.Save(Save);

    protected override void Update(GameTime gameTime)
    {
        Director?.BeforeUpdate(this);
        float dt = Director?.FixedDelta ?? (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = MathF.Min(dt, 0.1f);
        Clock += dt;
        if (Director == null) Input.Update(ScreenToVirtual);
        Input.TiltDiagonal = (_screen as PlayScreen)?.OnDeck == true;
        if (TiltActive && UseTilt && TiltSensor!.TryRead(out var g))
        {
            Tilt.Orientation = Window.CurrentOrientation;
            Input.SetTilt(Tilt.Update(g));
        }
        else if (Director == null || !TiltActive) Input.SetTilt(null);

        if (!IsMobile && Input.ToggleFullScreen) ToggleFullScreen();

        _screen.Update(dt);
        Music.Update();
        Audio.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        UpdateDestination();
        var target = _target!;
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        _gfx.Time = Clock;
        _gfx.BeginFrame(target, _virtualWidth, _pixelScale);
        _screen.Draw(_gfx);
        _gfx.End();

        GraphicsDevice.SetRenderTarget(null);
        Director?.AfterDraw(this, target);
        GraphicsDevice.Clear(Color.Black);
        var sampler = _dest.Width == target.Width ? SamplerState.PointClamp : SamplerState.LinearClamp;
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, sampler);
        _batch.Draw(target, _dest, Color.White);
        _batch.End();
        base.Draw(gameTime);
    }

    private void UpdateDestination()
    {
        var pp = GraphicsDevice.PresentationParameters;
        int w = Math.Max(1, pp.BackBufferWidth), h = Math.Max(1, pp.BackBufferHeight);
        int inset = Math.Clamp(BottomInsetProvider?.Invoke() ?? 0, 0, h / 4);
        h -= inset;

        int vw = ForcedVirtualWidth ?? Math.Clamp((int)MathF.Round(VirtualHeight * (float)w / h), MinVirtualWidth, MaxVirtualWidth);
        float scale = MathF.Min(w / (float)vw, h / (float)VirtualHeight);
        float whole = MathF.Floor(scale);
        if (whole >= 2 && whole / scale >= 0.9f) scale = whole;
        _scale = MathF.Max(scale, 0.01f);
        int dw = (int)(vw * _scale), dh = (int)(VirtualHeight * _scale);
        _dest = new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh);

        int pixelScale = ForcedPixelScale ?? Math.Clamp((int)MathF.Ceiling(_scale - 0.001f), 1, MaxPixelScale);
        if (pixelScale != _pixelScale || vw != _virtualWidth || _target == null)
        {
            _target?.Dispose();
            _pixelScale = pixelScale;
            _virtualWidth = vw;
            // Multisampled on desktop for smooth 3D edges (GLES can't resolve MSAA targets).
            _target = new RenderTarget2D(GraphicsDevice, vw * pixelScale, VirtualHeight * pixelScale, false,
                SurfaceFormat.Color, DepthFormat.Depth24, SupportsMsaaTargets ? 4 : 0, RenderTargetUsage.PreserveContents);
        }
    }

    private static bool SupportsMsaaTargets => !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();

    private Vector2 ScreenToVirtual(Vector2 p) => new((p.X - _dest.X) / _scale, (p.Y - _dest.Y) / _scale);

    private void ToggleFullScreen()
    {
        if (!_graphics.IsFullScreen)
        {
            _windowedSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
            _graphics.IsFullScreen = true;
        }
        else
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = _windowedSize.X;
            _graphics.PreferredBackBufferHeight = _windowedSize.Y;
        }
        _resizing = true;
        _graphics.ApplyChanges();
        _resizing = false;
    }

    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        if (_resizing || _graphics.IsFullScreen) return;
        var b = Window.ClientBounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        _resizing = true;
        _graphics.PreferredBackBufferWidth = b.Width;
        _graphics.PreferredBackBufferHeight = b.Height;
        _graphics.ApplyChanges();
        _resizing = false;
    }

    protected override void OnDeactivated(object sender, EventArgs args)
    {
        _screen?.OnDeactivated();
        TiltSensor?.Stop();
        PersistSave();
        base.OnDeactivated(sender, args);
    }

    protected override void OnActivated(object sender, EventArgs args)
    {
        if (TiltActive) TiltSensor?.Start(); // stopped in OnDeactivated
        _screen?.OnActivated();
        base.OnActivated(sender, args);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _screen?.OnDeactivated();
        PersistSave();
        base.OnExiting(sender, args);
    }
}

/// <summary>Drives the real game for store screenshots and videos (tools/Quazitronic.Capture). Never set in shipping builds.</summary>
internal interface ICaptureDirector
{
    float FixedDelta { get; }
    void BeforeUpdate(QuazitronicGame game);
    void AfterDraw(QuazitronicGame game, RenderTarget2D frame);
}
