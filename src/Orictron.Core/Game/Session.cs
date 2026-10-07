using System;
using System.Collections.Generic;

namespace Orictron.Game;

/// <summary>One frame of player intent, in the original's screen-relative terms.</summary>
public struct Controls
{
    public bool Up, Down, Left, Right, Fire, Grapple, Lift, Abandon;
}

/// <summary>What the screen should be showing (the original's text screens versus the deck).</summary>
public enum View
{
    Deck,
    /// <summary>"UNIT.. xx / PREPARE TO ENGAGE / SECURITY DEVICE".</summary>
    Briefing,
    /// <summary>The wires-and-cells transfer battle.</summary>
    Transfer,
    /// <summary>"SECURITY CLASS … / TRANSFER COMPLETE".</summary>
    Captured,
    /// <summary>Game over or ship secured.</summary>
    End,
}

/// <summary>Sound cues from the simulation (the enhanced audio turns them into sounds).</summary>
public enum Cue
{
    Shot,
    EnemyShot,
    Boom,
    Ram,
    /// <summary>The original's general-purpose blip; the parameter is its AY tone period.</summary>
    Blip,
    Lift,
    Charge,
    Grapple,
    PulseFire,
    EnemyPulse,
    CellTaken,
    EnemyCellTaken,
    TransferWin,
    TransferLose,
    Deadlock,
    Ejected,
    Cleared,
    Flash,
    GameOver,
    ShipSecured,
}

public readonly record struct CueEvent(Cue Cue, int Param, int X, int Y);

/// <summary>Something was hit (droid index, or -1 for the player), for sparks and a hit flash.</summary>
public readonly record struct HitEvent(int Droid, int X, int Y, int Z);

/// <summary>An explosion was spawned (for particles).</summary>
public readonly record struct BoomEvent(int X, int Y, int Z, bool Big);

/// <summary>
/// The enhanced remake's game simulation: a line-by-line port of the Oric original's
/// <c>main.c</c> (decks, droids, combat, grapple and transfer battle, autopilot). It runs at the
/// original's 25 frames per second; the renderer interpolates between frames. The original's
/// blocking loops (pauses, the transfer battle, end screens) are an iterator, so the code keeps
/// the original's shape - each <c>yield</c> is one <c>wait_frame()</c>.
/// </summary>
public sealed class Session
{
    public const int FramesPerSecond = 25;
    public const int Decks = DeckData.Count;
    public const int MaxDroids = 12, MaxBullets = 6, MaxBooms = 4;

    private static readonly int[] DeckDroids = { 6, 7, 8, 9, 10, 12 };
    private static readonly int[] DeckTypeMin = { 1, 1, 2, 3, 4, 5 };
    private static readonly int[] DeckTypeMax = { 3, 4, 5, 6, 7, 7 };

    // ---- ship ----
    private readonly int[,] _eType = new int[Decks, MaxDroids];
    private readonly int[,] _eHp = new int[Decks, MaxDroids];
    public readonly int[] DeckAlive = new int[Decks];

    // ---- current deck: droids ----
    public readonly int[] DroidX = new int[MaxDroids], DroidY = new int[MaxDroids];
    public readonly int[] PrevDroidX = new int[MaxDroids], PrevDroidY = new int[MaxDroids];
    private readonly int[] _aTm = new int[MaxDroids], _aCool = new int[MaxDroids];
    private readonly int[] _aDx = new int[MaxDroids], _aDy = new int[MaxDroids];

    // ---- bullets and explosions ----
    public readonly bool[] BulletOn = new bool[MaxBullets];
    public readonly int[] BulletX = new int[MaxBullets], BulletY = new int[MaxBullets], BulletZ = new int[MaxBullets];
    public readonly int[] PrevBulletX = new int[MaxBullets], PrevBulletY = new int[MaxBullets];
    public readonly int[] BulletDx = new int[MaxBullets], BulletDy = new int[MaxBullets];
    public readonly int[] BulletOwner = new int[MaxBullets];
    private readonly int[] _bLife = new int[MaxBullets], _bDmg = new int[MaxBullets];
    public readonly int[] BoomTime = new int[MaxBooms];
    public readonly int[] BoomX = new int[MaxBooms], BoomY = new int[MaxBooms], BoomZ = new int[MaxBooms];

    // ---- player ----
    public int PlayerX, PlayerY, PrevPlayerX, PrevPlayerY;
    public int PlayerType, PlayerHp;
    public int FacingX = 1, FacingY = 1;
    private int _pCool, _pRam, _pBurn, _pHold;
    public int Flash, FlashKind, GrappleTime;
    public int Score;

    // ---- game ----
    public int DeckIndex { get; private set; }
    public Deck Deck { get; private set; } = DeckData.Get(0);
    public int DeckCount => DeckDroids[DeckIndex];
    public int Frame { get; private set; }
    public bool GameOver { get; private set; }
    public bool Won { get; private set; }
    public bool Finished { get; private set; }
    public bool Abandoned { get; private set; }
    public bool Demo { get; }
    public View View { get; private set; } = View.Deck;

    /// <summary>The yellow status field (8 characters) and how long a timed message stays.</summary>
    public string Status { get; private set; } = "";
    public int StatusTime { get; private set; }
    /// <summary>The panel's left symbol box is lit while grappling.</summary>
    public bool GrappleLit { get; private set; }

    /// <summary>Droid class being fought in the transfer / named on the briefing.</summary>
    public int Opponent { get; private set; }

    public Transfer Transfer { get; } = new();

    public List<CueEvent> Cues { get; } = new();
    public List<BoomEvent> Booms { get; } = new();
    public List<HitEvent> Hits { get; } = new();

    private uint _rs;
    private int _kprev = 0xFF;
    private Controls _in;
    private int _inDx, _inDy;
    private bool _inFire, _inGrab, _inLift, _inEsc, _inUp, _inDown;
    private readonly IEnumerator<int> _script;
    private readonly Autopilot _autopilot;

    public Session(int seed, bool demo = false)
    {
        _rs = (uint)(seed & 0xFFFF);
        if (_rs == 0) _rs = 0xACE1;
        Demo = demo;
        _autopilot = new Autopilot(this);
        _script = Play().GetEnumerator();
        _script.MoveNext(); // run up to the first wait_frame
    }

    // ------------------------------------------------------------------ rng
    /// <summary>The original's 16-bit xorshift generator.</summary>
    internal int Rnd()
    {
        _rs ^= (_rs << 7) & 0xFFFF;
        _rs ^= _rs >> 9;
        _rs ^= (_rs << 8) & 0xFFFF;
        _rs &= 0xFFFF;
        return (int)(_rs & 0xFF);
    }

    // ---------------------------------------------------------------- frame
    /// <summary>Advances one frame (1/25 s) with this frame's controls.</summary>
    public void Tick(in Controls controls)
    {
        if (Finished) return;
        Cues.Clear();
        Booms.Clear();
        Hits.Clear();
        _in = controls;
        PrevPlayerX = PlayerX; PrevPlayerY = PlayerY;
        Array.Copy(DroidX, PrevDroidX, MaxDroids);
        Array.Copy(DroidY, PrevDroidY, MaxDroids);
        Array.Copy(BulletX, PrevBulletX, MaxBullets);
        Array.Copy(BulletY, PrevBulletY, MaxBullets);
        Frame = (Frame + 1) & 0xFF;
        if (!_script.MoveNext()) Finished = true;
    }

    private void Cue_(Cue c, int param = 0, int x = -1, int y = -1) => Cues.Add(new CueEvent(c, param, x, y));

    // ------------------------------------------------------------ helpers
    public int DroidType(int a) => _eType[DeckIndex, a];
    public int DroidHp(int a) => _eHp[DeckIndex, a];
    public bool DroidAlive(int a) => a < DeckCount && _eHp[DeckIndex, a] > 0;
    public int DroidsLeftOnDeck => DeckAlive[DeckIndex];
    public int DroidsLeft { get { int n = 0; foreach (int k in DeckAlive) n += k; return n; } }

    private int Eh(int a) => _eHp[DeckIndex, a];
    private void SetEh(int a, int v) => _eHp[DeckIndex, a] = v;
    private int Et(int a) => _eType[DeckIndex, a];

    private void Stat(string s, int time)
    {
        Status = s.Length > 8 ? s[..8] : s;
        StatusTime = time;
    }

    private int Zat(int x, int y) => Deck.FloorZ(x, y);
    private int TileAt(int x, int y) => Deck.TileAt(x, y);

    private int _curT;

    private bool CornerOk(int t)
    {
        int l = t & 3, cl = _curT & 3;
        if (!Deck.Walkable((byte)t)) return false;
        if (l == cl) return true;
        if ((l == cl + 1 || l + 1 == cl) && ((t >> 2) == (int)TileKind.Pad || (_curT >> 2) == (int)TileKind.Pad)) return true;
        return false;
    }

    /// <summary>Can something at (x, y) move to (nx, ny)? All four corners of its 8 x 8 footprint must be reachable.</summary>
    internal bool CanGo(int x, int y, int nx, int ny)
    {
        _curT = TileAt(x, y);
        return CornerOk(TileAt(nx - 4, ny - 4)) && CornerOk(TileAt(nx + 3, ny - 4)) &&
               CornerOk(TileAt(nx - 4, ny + 3)) && CornerOk(TileAt(nx + 3, ny + 3));
    }

    internal static bool CloseTo(int ax, int ay, int bx, int by, int r) =>
        ((ax - bx + r) & 0xFF) < ((r * 2) & 0xFF) && ((ay - by + r) & 0xFF) < ((r * 2) & 0xFF);

    internal static int Sgn(int a, int b, int dead) => a > b + dead ? 1 : b > a + dead ? -1 : 0;

    private int Touching(int x, int y, int r)
    {
        for (int a = 0; a < DeckCount; a++)
            if (Eh(a) != 0 && CloseTo(x, y, DroidX[a], DroidY[a], r)) return a;
        return -1;
    }

    // ------------------------------------------------------------ decks
    private void PlaceDroids()
    {
        for (int k = 0; k < DeckCount; k++)
        {
            int x = 0, y = 0;
            for (int tries = 0; tries < 60; tries++)
            {
                x = (6 + Rnd() % (Deck.Size * 12 - 12)) & 0xFF;
                y = (6 + Rnd() % (Deck.Size * 12 - 12)) & 0xFF;
                int t = TileAt(x, y) >> 2;
                if (t != (int)TileKind.Floor) continue;
                if (!CanGo(x, y, x, y)) continue;
                if (((x - PlayerX + 48) & 0xFF) < 96 && ((y - PlayerY + 48) & 0xFF) < 96) continue;
                break;
            }
            DroidX[k] = x;
            DroidY[k] = y;
            _aTm[k] = 1;
            _aCool[k] = 30;
            _aDx[k] = _aDy[k] = 0;
        }
    }

    /// <summary>Raised when the deck changes (the renderer rebuilds its geometry).</summary>
    public event Action<int>? DeckEntered;

    private void EnterDeck(int d)
    {
        DeckIndex = d;
        Deck = DeckData.Get(d);
        _autopilot.Invalidate();
        PlayerX = Deck.StartX;
        PlayerY = Deck.StartY;
        for (int k = 0; k < MaxBullets; k++) BulletOn[k] = false;
        for (int k = 0; k < MaxBooms; k++) BoomTime[k] = 0;
        PlaceDroids();
        PrevPlayerX = PlayerX; PrevPlayerY = PlayerY;
        Array.Copy(DroidX, PrevDroidX, MaxDroids);
        Array.Copy(DroidY, PrevDroidY, MaxDroids);
        View = View.Deck;
        Stat("DECK " + (d + 1), 50);
        DeckEntered?.Invoke(d);
    }

    private void InitShip()
    {
        for (int d = 0; d < Decks; d++)
        {
            DeckAlive[d] = DeckDroids[d];
            int span = DeckTypeMax[d] - DeckTypeMin[d] + 1;
            for (int k = 0; k < MaxDroids; k++)
            {
                _eHp[d, k] = 0;
                if (k >= DeckDroids[d]) continue;
                int t = DeckTypeMin[d] + Rnd() % span;
                if (d == Decks - 1 && k == 0) t = 8; // the command unit
                _eType[d, k] = t;
                _eHp[d, k] = Droids.MaxHp[t];
            }
        }
    }

    // ------------------------------------------------------------ combat
    private void Boom(int x, int y, int z, bool big = false)
    {
        for (int k = 0; k < MaxBooms; k++)
            if (BoomTime[k] == 0)
            {
                BoomTime[k] = 12; BoomX[k] = x; BoomY[k] = y; BoomZ[k] = z;
                break;
            }
        Booms.Add(new BoomEvent(x, y, z, big));
        Cue_(Cue.Boom, 0, x, y);
    }

    private void Fire(int x, int y, int dx, int dy, int dmg, int own)
    {
        for (int k = 0; k < MaxBullets; k++)
        {
            if (BulletOn[k]) continue;
            BulletOn[k] = true;
            BulletX[k] = (x + dx * 6) & 0xFF;
            BulletY[k] = (y + dy * 6) & 0xFF;
            PrevBulletX[k] = BulletX[k];
            PrevBulletY[k] = BulletY[k];
            BulletZ[k] = Zat(x, y);
            BulletDx[k] = dx * 4;
            BulletDy[k] = dy * 4;
            _bLife[k] = 24;
            _bDmg[k] = dmg;
            BulletOwner[k] = own;
            if (own == 0) Cue_(Cue.Shot, 0, x, y);
            else Cue_(Cue.EnemyShot, 200, x, y);
            return;
        }
    }

    private void CheckCleared()
    {
        if (DeckAlive[DeckIndex] != 0) return;
        Score += 50;
        for (int d = 0; d < Decks; d++)
            if (DeckAlive[d] != 0)
            {
                Stat("CLEARED", 75);
                Cue_(Cue.Cleared);
                return;
            }
        Won = true;
        GameOver = true;
    }

    private void KillDroid(int a)
    {
        SetEh(a, 0);
        DeckAlive[DeckIndex]--;
        Score += Droids.Score[Et(a)];
        Boom(DroidX[a], DroidY[a], Zat(DroidX[a], DroidY[a]), Et(a) >= 5);
        CheckCleared();
    }

    private void HurtDroid(int a, int dmg)
    {
        Hits.Add(new HitEvent(a, DroidX[a], DroidY[a], Zat(DroidX[a], DroidY[a])));
        if (Eh(a) <= dmg) KillDroid(a);
        else SetEh(a, Eh(a) - dmg);
    }

    private void HurtPlayer(int dmg)
    {
        if (Demo) dmg = (dmg + 1) >> 1; // keep the attract mode going
        Hits.Add(new HitEvent(-1, PlayerX, PlayerY, Zat(PlayerX, PlayerY)));
        if (PlayerHp <= dmg)
        {
            Boom(PlayerX, PlayerY, Zat(PlayerX, PlayerY), true);
            if (PlayerType != 0)
            {
                PlayerType = 0;
                PlayerHp = Droids.MaxHp[0] / 2;
                Stat("EJECTED", 60);
                Cue_(Cue.Ejected);
            }
            else
            {
                PlayerHp = 0;
                GameOver = true;
            }
        }
        else PlayerHp -= dmg;
    }

    private void Ram(int a)
    {
        if (_pRam != 0) return;
        _pRam = 12;
        Cue_(Cue.Ram, 0, DroidX[a], DroidY[a]);
        int type = Et(a);
        HurtDroid(a, Droids.Mass[PlayerType]);
        HurtPlayer(Droids.Mass[type]);
    }

    private void ReadInput()
    {
        if (Demo)
        {
            _inEsc = false;
            _autopilot.Drive(out _inDx, out _inDy, out _inFire, out _inGrab, out _inLift);
            return;
        }
        int dx = 0, dy = 0;
        if (_in.Up) { dx--; dy--; }
        if (_in.Down) { dx++; dy++; }
        if (_in.Left) { dx--; dy++; }
        if (_in.Right) { dx++; dy--; }
        _inDx = Math.Clamp(dx, -1, 1);
        _inDy = Math.Clamp(dy, -1, 1);
        _inFire = _in.Fire;
        _inGrab = _in.Grapple;
        _inLift = _in.Lift;
        _inEsc = _in.Abandon;
        _inUp = _in.Up;
        _inDown = _in.Down;
    }

    private void MovePlayer()
    {
        int dx = _inDx, dy = _inDy;
        if (dx != 0 || dy != 0) { FacingX = dx; FacingY = dy; }
        int sp = Droids.Speed[PlayerType];
        for (int k = 0; k < sp; k++)
        {
            int nx = (PlayerX + dx) & 0xFF;
            if (dx != 0 && CanGo(PlayerX, PlayerY, nx, PlayerY))
            {
                int a = Touching(nx, PlayerY, 11);
                if (a < 0) PlayerX = nx;
                else Ram(a);
            }
            int ny = (PlayerY + dy) & 0xFF;
            if (dy != 0 && CanGo(PlayerX, PlayerY, PlayerX, ny))
            {
                int a = Touching(PlayerX, ny, 11);
                if (a < 0) PlayerY = ny;
                else Ram(a);
            }
        }
        if (_pRam != 0) _pRam--;
    }

    private void UpdateDroids()
    {
        for (int a = 0; a < DeckCount; a++)
        {
            if (Eh(a) == 0) continue;
            // droids far from the player doze
            if (!CloseTo(DroidX[a], DroidY[a], PlayerX, PlayerY, 100)) continue;
            int t = Et(a);
            if (--_aTm[a] == 0)
            {
                _aTm[a] = 12 + (Rnd() & 31);
                if (Rnd() < 40 + t * 20)
                {
                    _aDx[a] = Sgn(PlayerX, DroidX[a], 6);
                    _aDy[a] = Sgn(PlayerY, DroidY[a], 6);
                    if (t == 2) _aDx[a] = _aDy[a] = 0; // sentries hold still
                }
                else
                {
                    _aDx[a] = Rnd() % 3 - 1;
                    _aDy[a] = Rnd() % 3 - 1;
                }
            }
            int sp = (Frame & 1) != 0 || Droids.Speed[t] > 1 ? 1 : 0;
            for (int k = 0; k < sp; k++)
            {
                int nx = (DroidX[a] + _aDx[a]) & 0xFF;
                int ny = (DroidY[a] + _aDy[a]) & 0xFF;
                if (!CanGo(DroidX[a], DroidY[a], nx, ny) || CloseTo(nx, ny, PlayerX, PlayerY, 11))
                {
                    _aTm[a] = 1;
                    break;
                }
                DroidX[a] = nx;
                DroidY[a] = ny;
            }
            if (Droids.Damage[t] != 0)
            {
                if (_aCool[a] != 0) _aCool[a]--;
                else if (CloseTo(DroidX[a], DroidY[a], PlayerX, PlayerY, 70))
                {
                    int fx = Sgn(PlayerX, DroidX[a], 10), fy = Sgn(PlayerY, DroidY[a], 10);
                    if (fx != 0 || fy != 0) Fire(DroidX[a], DroidY[a], fx, fy, Droids.Damage[t], 1);
                    _aCool[a] = Droids.FireRate[t] + (Rnd() & 15);
                }
            }
        }
    }

    private void UpdateBullets()
    {
        for (int k = 0; k < MaxBullets; k++)
        {
            if (!BulletOn[k]) continue;
            BulletX[k] = (BulletX[k] + BulletDx[k]) & 0xFF;
            BulletY[k] = (BulletY[k] + BulletDy[k]) & 0xFF;
            int t = TileAt(BulletX[k], BulletY[k]);
            if (--_bLife[k] == 0 || !Deck.Walkable((byte)t) || (t & 3) * 12 > BulletZ[k])
            {
                BulletOn[k] = false;
                continue;
            }
            if (BulletOwner[k] == 0)
            {
                for (int a = 0; a < DeckCount; a++)
                    if (Eh(a) != 0 && CloseTo(BulletX[k], BulletY[k], DroidX[a], DroidY[a], 8))
                    {
                        BulletOn[k] = false;
                        HurtDroid(a, _bDmg[k]);
                        break;
                    }
            }
            else if (CloseTo(BulletX[k], BulletY[k], PlayerX, PlayerY, 7))
            {
                BulletOn[k] = false;
                HurtPlayer(_bDmg[k]);
            }
        }
    }

    /// <summary>The bits of the original's render() that change game state: the player's blink and explosion timers.</summary>
    private void RenderEffects()
    {
        if (Flash != 0)
        {
            Flash--;
            if (FlashKind == 1 && (Frame & 1) != 0) Cue_(Cue.Flash, 12 + Flash * 4);
        }
        for (int a = 0; a < MaxBooms; a++)
            if (BoomTime[a] != 0) BoomTime[a]--;
    }

    /// <summary>The player is drawn this frame (it blinks after a transfer and vanishes when destroyed).</summary>
    public bool PlayerVisible => (!GameOver || PlayerHp != 0) && (Flash == 0 || (Frame & 2) != 0);

    // ------------------------------------------------------------ main loop
    private IEnumerable<int> Pause(int n)
    {
        for (int i = 0; i < n; i++) yield return 0;
    }

    private IEnumerable<int> Play()
    {
        Score = 0;
        GameOver = Won = false;
        PlayerType = 0;
        PlayerHp = Droids.MaxHp[0];
        FacingX = FacingY = 1;
        _pCool = _pRam = _pBurn = 0;
        Flash = GrappleTime = _pHold = 0;
        InitShip();
        EnterDeck(0);
        _kprev = 0xFF;

        while (!GameOver)
        {
            yield return 0;
            ReadInput();
            int keyt = 0;
            if (_inGrab) keyt |= 1;
            if (_inLift) keyt |= 2;
            if (_inEsc) keyt |= 4;
            int k = keyt & ~_kprev;
            _kprev = keyt;
            if ((k & 4) != 0)
            {
                Abandoned = true;
                yield break;
            }

            MovePlayer();

            // hold fire while standing still to grapple, as on the Spectrum; then run into a droid
            if (_inFire && _inDx == 0 && _inDy == 0)
            {
                if (++_pHold == 12)
                {
                    GrappleTime = 150;
                    Stat("GRAPPLE", 0);
                    GrappleLit = true;
                    Cue_(Cue.Grapple);
                }
            }
            else _pHold = 0;
            if (GrappleTime != 0 && --GrappleTime == 0) { Stat("MOBILE", 0); GrappleLit = false; }
            if (GrappleTime != 0) k |= 1;

            if (_pCool != 0) _pCool--;
            else if (_inFire && Droids.Damage[PlayerType] != 0 && GrappleTime == 0)
            {
                Fire(PlayerX, PlayerY, FacingX, FacingY, Droids.Damage[PlayerType], 0);
                _pCool = Droids.FireRate[PlayerType];
            }

            int t = TileAt(PlayerX, PlayerY) >> 2;
            if (t == (int)TileKind.Lift && (k & 2) != 0)
            {
                Stat("LIFT", 0);
                Cue_(Cue.Lift);
                foreach (int f in Pause(10)) yield return f;
                EnterDeck(DeckIndex + 1 < Decks ? DeckIndex + 1 : 0);
                continue;
            }
            if (t == (int)TileKind.Energiser && (Frame & 3) == 0 && PlayerHp < Droids.MaxHp[PlayerType])
            {
                PlayerHp++;
                Cue_(Cue.Charge, 40 + PlayerHp);
                if (StatusTime == 0) Stat("CHARGING", 10);
            }

            // host burnout
            if (PlayerType != 0 && ++_pBurn >= 75)
            {
                _pBurn = 0;
                HurtPlayer(1);
            }

            if ((k & 1) != 0)
            {
                int a = Touching(PlayerX, PlayerY, 16);
                if (a >= 0)
                {
                    int ty = Et(a);
                    GrappleTime = _pHold = 0;
                    Stat("GRAPPLE", 0);
                    GrappleLit = true;
                    Cue_(Cue.Grapple);
                    foreach (int f in Pause(10)) yield return f;
                    bool won = false;
                    foreach (int f in RunTransfer(ty, w => won = w)) yield return f;
                    if (Finished || Abandoned) yield break;
                    View = View.Deck;
                    if (won)
                    {
                        PlayerType = ty;
                        PlayerHp = Eh(a);
                        if (PlayerHp < Droids.MaxHp[ty] / 2) PlayerHp = Droids.MaxHp[ty] / 2;
                        SetEh(a, 0);
                        DeckAlive[DeckIndex]--;
                        Score += Droids.Score[ty] * 2;
                        Flash = 30; FlashKind = 1;
                        Stat("MOBILE", 0);
                        CheckCleared();
                    }
                    else
                    {
                        // the droid throws you off: push it back a little
                        for (int s = 0; s < 14; s++)
                        {
                            int nx = (DroidX[a] + Sgn(DroidX[a], PlayerX, 0)) & 0xFF;
                            int ny = (DroidY[a] + Sgn(DroidY[a], PlayerY, 0)) & 0xFF;
                            if (!CanGo(DroidX[a], DroidY[a], nx, ny)) break;
                            DroidX[a] = nx; DroidY[a] = ny;
                        }
                        PrevDroidX[a] = DroidX[a]; PrevDroidY[a] = DroidY[a];
                        _aTm[a] = 1;
                        Boom(PlayerX, PlayerY, Zat(PlayerX, PlayerY));
                        Flash = 25; FlashKind = 2;
                        if (PlayerType != 0)
                        {
                            PlayerType = 0;
                            PlayerHp = Droids.MaxHp[0] / 2;
                        }
                        else HurtPlayer(Droids.MaxHp[0] / 2);
                        Stat("FAILED", 50);
                    }
                    GrappleLit = false;
                    if (GameOver && PlayerHp == 0) break;
                    continue;
                }
            }

            UpdateDroids();
            UpdateBullets();
            RenderEffects();
            if (StatusTime != 0 && --StatusTime == 0) Stat(Demo ? "DEMO" : "MOBILE", 0);
        }

        if (PlayerHp == 0 || Won)
        {
            Cue_(Won ? Cue.ShipSecured : Cue.GameOver);
            for (int f = 0; f < 25; f++)
            {
                yield return 0;
                RenderEffects();
            }
        }
        View = View.End;
        foreach (int f in Pause(150)) yield return f;
        Finished = true;
    }

    // ------------------------------------------------------------ transfer
    private IEnumerable<int> RunTransfer(int et, Action<bool> result)
    {
        var tr = Transfer;
        Opponent = et;
        View = View.Briefing;
        Stat("GRAPPLE", 0);
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
            ReadInput();
            if (i > 15 && !Demo && (_in.Fire || _in.Grapple)) break;
        }

        while (true)
        {
            tr.Setup(this, et);
            View = View.Transfer;
            Stat("TIME 99", 0);
            foreach (int f in Pause(20)) yield return f;

            int time = 0, rep = 0;
            tr.Seconds = 99;
            int ecool = 20 + (Rnd() & 31);
            bool fired = true;
            while (tr.Seconds != 0)
            {
                yield return 0;
                if (++time == 2)
                {
                    time = 0;
                    tr.Seconds--;
                    Stat("TIME " + tr.Seconds.ToString("00"), 0);
                }

                bool up, down, fire;
                if (Demo)
                {
                    // autopilot: hold pulses back, then flip enemy cells late on
                    up = down = fire = false;
                    if (tr.Seconds < 55 && tr.PlayerPulses != 0)
                    {
                        int w;
                        for (w = 0; w < Transfer.Wires; w++)
                            if (tr.LeftKind[w] != 1 && tr.LeftKind[w] != 3 && tr.LeftPulse[w] == 0 && tr.Owner[w] != 0) break;
                        if (w < Transfer.Wires)
                        {
                            if (w < tr.Cursor) up = true;
                            else if (w > tr.Cursor) down = true;
                            else fire = !fired && (Frame & 3) == 0;
                        }
                    }
                }
                else
                {
                    ReadInput();
                    up = _inUp;
                    down = _inDown;
                    fire = _inFire || _in.Grapple;
                    if (_inEsc)
                    {
                        Abandoned = true;
                        yield break;
                    }
                }
                if (rep != 0) rep--;
                if (rep == 0 && up && tr.Cursor > 0) { tr.Cursor--; rep = 3; }
                if (rep == 0 && down && tr.Cursor < Transfer.Wires - 1) { tr.Cursor++; rep = 3; }
                if (fire)
                {
                    int c = tr.Cursor;
                    if (!fired && tr.PlayerPulses != 0 && tr.LeftPulse[c] == 0 && tr.LeftKind[c] != 3)
                    {
                        tr.PlayerPulses--;
                        tr.LeftPulse[c] = 1;
                        Cue_(Cue.PulseFire, 60);
                    }
                    fired = true;
                }
                else fired = false;

                // enemy AI: saves its pulses, keener as the clock runs down
                if (ecool != 0) ecool--;
                else if (tr.EnemyPulses != 0 && Rnd() < 8 + et * 4 + ((99 - tr.Seconds) >> 1))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        int w = Rnd() % Transfer.Wires;
                        if ((tr.RightKind[w] == 1 || tr.RightKind[w] == 3) && Rnd() > 24) continue;
                        if (tr.RightKind[w] == 3 || tr.RightPulse[w] != 0 || (tr.Owner[w] != 0 && tr.Hold[w] > 25)) continue;
                        if (tr.Owner[w] != 0 && tr.Hold[w] == 0 && Rnd() > 60) continue;
                        tr.EnemyPulses--;
                        tr.RightPulse[w] = 1;
                        Cue_(Cue.EnemyPulse, 90);
                        ecool = 30 - et * 2;
                        break;
                    }
                }

                // pulses travel one column every other frame
                for (int w = 0; w < Transfer.Wires; w++)
                {
                    if ((Frame & 1) != 0)
                    {
                        if (tr.LeftPulse[w] != 0) tr.AdvancePulse(this, 0, w);
                        if (tr.RightPulse[w] != 0) tr.AdvancePulse(this, 1, w);
                    }
                    if (tr.Hold[w] != 0 && --tr.Hold[w] == 0 && tr.Owner[w] != tr.Base[w])
                        tr.Owner[w] = tr.Base[w];
                }
                // the wire behind a finished pulse goes dark again
                if ((Frame & 15) == 0)
                    for (int w = 0; w < Transfer.Wires; w++)
                    {
                        if (tr.LeftPulse[w] == 0 && tr.Hold[w] == 0) tr.LeftLit[w] = 0;
                        if (tr.RightPulse[w] == 0 && tr.Hold[w] == 0) tr.RightLit[w] = 0;
                    }
            }

            int mine = 0, theirs = 0;
            for (int w = 0; w < Transfer.Wires; w++)
            {
                if (tr.Owner[w] != 0) theirs++;
                else mine++;
            }
            if (mine == theirs)
            {
                Stat("DEADLOCK", 0);
                Cue_(Cue.Deadlock);
                foreach (int f in Pause(40)) yield return f;
                continue;
            }

            bool win = mine > theirs;
            tr.ResultWin = win;
            tr.ResultStep = 0;
            for (int w = 0; w < Transfer.Wires; w++)
            {
                tr.Owner[w] = win ? 0 : 1;
                tr.ResultStep = w + 1;
                Cue_(Cue.Blip, win ? 140 - w * 10 : 40 + w * 14);
                foreach (int f in Pause(2)) yield return f;
            }
            Cue_(win ? Cue.TransferWin : Cue.TransferLose);
            if (!win) Cue_(Cue.Boom);
            for (int i = 0; i < 8; i++)
            {
                tr.FlashPhase = i;
                if (win) Cue_(Cue.Blip, (i & 1) != 0 ? 24 : 18);
                foreach (int f in Pause(2)) yield return f;
            }
            tr.FlashPhase = -1;
            Stat(win ? "COMPLETE" : "FAILED", 0);
            foreach (int f in Pause(40)) yield return f;
            if (win)
            {
                View = View.Captured;
                foreach (int f in Pause(50)) yield return f;
            }
            result(win);
            yield break;
        }
    }

    internal void CueFromTransfer(Cue c, int param) => Cue_(c, param);
}
