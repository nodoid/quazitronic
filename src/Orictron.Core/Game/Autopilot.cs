namespace Orictron.Game;

/// <summary>
/// Plays the game by itself, as the original's demo mode does: breadth-first search over the deck's
/// tiles to reach the nearest droid, shooting along the iso axes, grappling droids that outclass
/// its host, and riding the lift once a deck is clear. Used by the title-screen demo, the store
/// capture and the tests.
/// </summary>
internal sealed class Autopilot
{
    private readonly Session _s;
    private readonly int[] _dist = new int[256];
    private readonly int[] _queue = new int[256];
    private int _target = -1;
    private bool _ok;
    private int _lastX, _lastY, _still, _alt, _altDx, _altDy;

    public Autopilot(Session s) => _s = s;

    public void Invalidate() => _ok = false;

    private bool Step(int a, int b)
    {
        var d = _s.Deck;
        byte ta = d.Tile(a & 15, a >> 4), tb = d.Tile(b & 15, b >> 4);
        int la = ta & 3, lb = tb & 3;
        if (!Deck.Walkable(tb)) return false;
        if (la == lb) return true;
        return (la + 1 == lb || lb + 1 == la) && ((ta >> 2) == (int)TileKind.Pad || (tb >> 2) == (int)TileKind.Pad);
    }

    private static int Neighbour(int c, int k)
    {
        int i = c & 15;
        return k switch
        {
            0 => i != 0 ? c - 1 : c,
            1 => i < 15 ? c + 1 : c,
            2 => c >= 16 ? c - 16 : c,
            _ => c < 240 ? c + 16 : c,
        };
    }

    private void Bfs(int t)
    {
        for (int c = 0; c < 256; c++) _dist[c] = 0xFF;
        int head = 0, tail = 0;
        _dist[t] = 0;
        _queue[tail++] = t;
        while (head != tail)
        {
            int c = _queue[head++];
            int d = _dist[c] + 1;
            for (int k = 0; k < 4; k++)
            {
                int m = Neighbour(c, k);
                if (m != c && _dist[m] == 0xFF && Step(m, c))
                {
                    _dist[m] = d;
                    _queue[tail++] = m;
                }
            }
        }
        _target = t;
        _ok = true;
    }

    private void GoTo(int tx, int ty, out int dx, out int dy)
    {
        int n = (((ty & 0xFF) / 12) << 4 | ((tx & 0xFF) / 12)) & 0xFF;
        if (n != _target || !_ok) Bfs(n);
        int px = _s.PlayerX, py = _s.PlayerY;
        int c = ((py / 12) << 4 | (px / 12)) & 0xFF;
        if (c == _target || _dist[c] == 0xFF)
        {
            dx = Session.Sgn(tx, px, 1);
            dy = Session.Sgn(ty, py, 1);
            return;
        }
        int best = c, bd = _dist[c];
        for (int k = 0; k < 4; k++)
        {
            int m = Neighbour(c, k);
            if (_dist[m] < bd) { bd = _dist[m]; best = m; }
        }
        int cx = (best & 15) * 12 + 6;
        int cy = (best >> 4) * 12 + 6;
        // centre on the cross axis first so corners don't snag
        if ((best ^ c) < 16)
        {
            dy = Session.Sgn(cy, py, 1); dx = Session.Sgn(cx, px, 0);
        }
        else
        {
            dx = Session.Sgn(cx, px, 1); dy = Session.Sgn(cy, py, 0);
        }
    }

    public void Drive(out int dx, out int dy, out bool fire, out bool grab, out bool lift)
    {
        var s = _s;
        fire = grab = lift = false;
        if (_alt != 0)
        {
            _alt--;
            dx = _altDx;
            dy = _altDy;
            return;
        }
        int px = s.PlayerX, py = s.PlayerY;
        int best = -1, bd = 0xFF;
        for (int a = 0; a < s.DeckCount; a++)
        {
            if (!s.DroidAlive(a)) continue;
            int adx = System.Math.Abs(s.DroidX[a] - px), ady = System.Math.Abs(s.DroidY[a] - py);
            int d = adx > ady ? adx : ady;
            if (d < bd) { bd = d; best = a; }
        }
        if (best >= 0)
        {
            int a = best;
            int t = s.DroidType(a);
            int adx = System.Math.Abs(s.DroidX[a] - px), ady = System.Math.Abs(s.DroidY[a] - py);
            // grapple droids that outclass our host
            if (t > s.PlayerType && s.PlayerHp > 12 && (s.Rnd() & 3) == 0)
            {
                if (bd < 15) { grab = true; dx = dy = 0; return; }
                GoTo(s.DroidX[a], s.DroidY[a], out dx, out dy);
                return;
            }
            if (bd < 60 && s.Deck.FloorZ(s.DroidX[a], s.DroidY[a]) == s.Deck.FloorZ(px, py) &&
                (adx < 5 || ady < 5 || System.Math.Abs(adx - ady) < 5))
            {
                int sx = adx < 5 ? 0 : Session.Sgn(s.DroidX[a], px, 0);
                int sy = ady < 5 ? 0 : Session.Sgn(s.DroidY[a], py, 0);
                s.FacingX = sx; s.FacingY = sy;
                fire = true;
                dx = dy = 0;
                if (bd < 20) { dx = -sx; dy = -sy; } // back off
            }
            else GoTo(s.DroidX[a], s.DroidY[a], out dx, out dy);
        }
        else
        {
            // deck clear: back to the lift
            GoTo(s.Deck.StartX, s.Deck.StartY, out dx, out dy);
            lift = (s.Deck.TileAt(px, py) >> 2) == (int)TileKind.Lift && (s.Frame & 1) != 0;
        }

        if ((dx != 0 || dy != 0) && px == _lastX && py == _lastY)
        {
            if (++_still > 6)
            {
                _still = 0;
                _alt = 8 + (s.Rnd() & 15);
                _altDx = s.Rnd() % 3 - 1;
                _altDy = s.Rnd() % 3 - 1;
            }
        }
        else _still = 0;
        _lastX = px;
        _lastY = py;
    }
}
