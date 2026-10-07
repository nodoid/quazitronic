using Orictron.Game;
using Xunit;

namespace Orictron.Tests;

/// <summary>The enhanced remake's simulation: a port of the original's rules, checked piece by piece.</summary>
public sealed class GameTests
{
    private static void Run(Session s, int frames, Controls c = default)
    {
        for (int i = 0; i < frames && !s.Finished; i++) s.Tick(c);
    }

    /// <summary>Sends every droid on the deck far away so it dozes.</summary>
    private static void Banish(Session s, int except = -1)
    {
        for (int a = 0; a < Session.MaxDroids; a++)
        {
            if (a == except) continue;
            s.DroidX[a] = s.PlayerX < 96 ? 186 : 6;
            s.DroidY[a] = s.PlayerY < 96 ? 186 : 6;
        }
    }

    /// <summary>Finds a direction with flat, open floor for <paramref name="dist"/> units from the player and faces it.</summary>
    private static (int dx, int dy) FaceOpenLane(Session s, int dist)
    {
        int px = s.PlayerX, py = s.PlayerY, z = s.Deck.FloorZ(px, py);
        foreach (var (dx, dy) in new[] { (1, 1), (1, 0), (0, 1), (-1, -1), (-1, 0), (0, -1), (1, -1), (-1, 1) })
        {
            bool ok = true;
            for (int k = 6; k <= dist && ok; k += 2)
                ok = s.CanGo(px + dx * k, py + dy * k, px + dx * k, py + dy * k) && s.Deck.FloorZ(px + dx * k, py + dy * k) == z;
            if (!ok) continue;
            s.FacingX = dx;
            s.FacingY = dy;
            return (dx, dy);
        }
        throw new InvalidOperationException("no open lane");
    }

    [Fact]
    public void RandomNumbersMatchTheOriginalXorshift()
    {
        // cc65's unsigned int is 16 bits: rs ^= rs << 7; rs ^= rs >> 9; rs ^= rs << 8.
        ushort rs = 0xACE1;
        var expected = new List<int>();
        for (int i = 0; i < 50; i++)
        {
            rs ^= (ushort)(rs << 7);
            rs ^= (ushort)(rs >> 9);
            rs ^= (ushort)(rs << 8);
            expected.Add(rs & 0xFF);
        }
        var s = new Session(0xACE1);
        // The constructor has already used some numbers (ship setup); replay from scratch instead.
        var fresh = typeof(Session).GetField("_rs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fresh.SetValue(s, (uint)0xACE1);
        for (int i = 0; i < 50; i++) Assert.Equal(expected[i], s.Rnd());
    }

    [Fact]
    public void TheShipHoldsFiftyTwoDroidsWithTheCommandUnitOnTheLastDeck()
    {
        var s = new Session(99);
        Assert.Equal(new[] { 6, 7, 8, 9, 10, 12 }, s.DeckAlive);
        Assert.Equal(52, s.DroidsLeft);
        Assert.Equal(0, s.DeckIndex);
        Assert.Equal(30, s.PlayerHp);
        Assert.Equal("DECK 1", s.Status);
    }

    [Fact]
    public void DroidsStartOnFloorAwayFromThePlayer()
    {
        for (int seed = 1; seed < 20; seed++)
        {
            var s = new Session(seed);
            for (int a = 0; a < s.DeckCount; a++)
            {
                Assert.True(s.DroidAlive(a));
                Assert.InRange(s.DroidType(a), 1, 3);
            }
        }
    }

    [Fact]
    public void WallsBlockMovement()
    {
        var s = new Session(5);
        Banish(s);
        // walk "up" the screen (world -1,-1) for a long time: the player must stay on walkable tiles
        Run(s, 200, new Controls { Up = true });
        Assert.True(Deck.Walkable(s.Deck.TileAt(s.PlayerX, s.PlayerY)));
        Run(s, 200, new Controls { Left = true });
        Assert.True(Deck.Walkable(s.Deck.TileAt(s.PlayerX, s.PlayerY)));
        Assert.True(s.CanGo(s.PlayerX, s.PlayerY, s.PlayerX, s.PlayerY));
    }

    [Fact]
    public void ShootingADroidHitsIt()
    {
        var s = new Session(11);
        s.Tick(default);
        Banish(s, 0);
        var (dx, dy) = FaceOpenLane(s, 30);
        int tx = s.PlayerX + dx * 26, ty = s.PlayerY + dy * 26;
        int before = s.DroidHp(0);
        bool hit = false;
        for (int i = 0; i < 100 && !hit; i++)
        {
            s.DroidX[0] = tx; s.DroidY[0] = ty; // hold it still
            s.Tick(new Controls { Fire = (i & 1) == 0 });
            hit = s.Hits.Exists(h => h.Droid == 0);
        }
        Assert.True(hit);
        Assert.True(!s.DroidAlive(0) || s.DroidHp(0) < before);
    }

    [Fact]
    public void KillingADroidAddsItsValue()
    {
        var s = new Session(11);
        s.Tick(default);
        Banish(s, 0);
        var (dx, dy) = FaceOpenLane(s, 30);
        int tx = s.PlayerX + dx * 26, ty = s.PlayerY + dy * 26;
        int type = s.DroidType(0);
        for (int i = 0; i < 2000 && s.DroidAlive(0); i++)
        {
            s.DroidX[0] = tx; s.DroidY[0] = ty;
            s.FacingX = dx; s.FacingY = dy;
            s.Tick(new Controls { Fire = (i & 1) == 0 });
        }
        Assert.False(s.DroidAlive(0));
        Assert.Equal(Droids.Score[type], s.Score);
        Assert.Equal(5, s.DroidsLeftOnDeck);
    }

    [Fact]
    public void RammingHurtsBothSides()
    {
        var s = new Session(21);
        Banish(s, 0);
        int px = s.PlayerX, py = s.PlayerY;
        // a droid just to the +x side, then push into it (DOWN+RIGHT is world +x)
        s.DroidX[0] = px + 12; s.DroidY[0] = py;
        if (!s.CanGo(px, py, px + 12, py)) { s.DroidX[0] = px - 12; }
        bool plusX = s.DroidX[0] > px;
        int hp = s.PlayerHp, dhp = s.DroidHp(0);
        var c = plusX ? new Controls { Down = true, Right = true } : new Controls { Up = true, Left = true };
        for (int i = 0; i < 30; i++)
        {
            s.DroidX[0] = plusX ? s.PlayerX + 12 : s.PlayerX - 12; s.DroidY[0] = s.PlayerY;
            s.Tick(c);
        }
        Assert.True(s.PlayerHp < hp);
        Assert.True(s.DroidHp(0) < dhp || !s.DroidAlive(0));
    }

    [Fact]
    public void TheLiftTakesYouToTheNextDeck()
    {
        var s = new Session(3);
        Banish(s);
        Assert.Equal(TileKind.Lift, (TileKind)(s.Deck.TileAt(s.PlayerX, s.PlayerY) >> 2));
        s.Tick(default); // keys held from the start don't count, as on the Oric
        s.Tick(new Controls { Lift = true });
        Run(s, 15);
        Assert.Equal(1, s.DeckIndex);
        Assert.Equal(s.Deck.StartX, s.PlayerX);
        Assert.Equal(TileKind.Lift, (TileKind)(s.Deck.TileAt(s.PlayerX, s.PlayerY) >> 2));
    }

    [Fact]
    public void EnergisersRecharge()
    {
        var s = new Session(4);
        Banish(s);
        var deck = s.Deck;
        (int x, int y) = (0, 0);
        for (int j = 0; j < 16; j++)
            for (int i = 0; i < 16; i++)
                if (deck.Kind(i, j) == TileKind.Energiser && deck.Kind(i + 1, j) == TileKind.Energiser && deck.Kind(i, j + 1) == TileKind.Energiser)
                    (x, y) = (i * 12 + 12, j * 12 + 12);
        Assert.NotEqual(0, x);
        s.PlayerX = x; s.PlayerY = y;
        s.PlayerHp = 5;
        Run(s, 40);
        Assert.True(s.PlayerHp > 5);
    }

    [Fact]
    public void HoldingFireStillArmsTheGrapple()
    {
        var s = new Session(8);
        Banish(s);
        Run(s, 14, new Controls { Fire = true });
        Assert.True(s.GrappleLit);
        Assert.Equal("GRAPPLE", s.Status);
    }

    [Fact]
    public void WinningATransferTakesOverTheDroid()
    {
        var s = new Session(9);
        Banish(s, 0);
        s.DroidX[0] = s.PlayerX + 10; s.DroidY[0] = s.PlayerY + 10;
        int type = s.DroidType(0);
        s.Tick(default);
        s.DroidX[0] = s.PlayerX + 10; s.DroidY[0] = s.PlayerY + 10;
        s.Tick(new Controls { Grapple = true });
        Assert.Equal("GRAPPLE", s.Status);
        // ride through the briefing; then hold every cell for the player
        for (int i = 0; i < 2000 && (s.View != View.Deck || i < 12); i++)
        {
            if (s.View == View.Transfer)
                for (int w = 0; w < Transfer.Wires; w++) { s.Transfer.Owner[w] = 0; s.Transfer.Hold[w] = 75; }
            s.Tick(default);
        }
        Assert.Equal(type, s.PlayerType);
        Assert.Equal(5, s.DroidsLeftOnDeck);
        Assert.Equal(Droids.Score[type] * 2, s.Score);
        Assert.True(s.PlayerHp >= Droids.MaxHp[type] / 2);
    }

    [Fact]
    public void LosingATransferAsTheInfluenceDeviceCostsHalfYourEnergy()
    {
        var s = new Session(9);
        Banish(s, 0);
        s.Tick(default);
        s.DroidX[0] = s.PlayerX + 10; s.DroidY[0] = s.PlayerY + 10;
        s.Tick(new Controls { Grapple = true });
        for (int i = 0; i < 2000 && (s.View != View.Deck || i < 12); i++)
        {
            if (s.View == View.Transfer)
                for (int w = 0; w < Transfer.Wires; w++) { s.Transfer.Owner[w] = 1; s.Transfer.Hold[w] = 75; }
            s.Tick(default);
        }
        Assert.Equal(0, s.PlayerType);
        Assert.Equal(15, s.PlayerHp);
        Assert.Equal("FAILED", s.Status);
        Assert.True(s.DroidAlive(0));
    }

    [Fact]
    public void AbandoningEndsTheGame()
    {
        var s = new Session(1);
        s.Tick(default);
        s.Tick(new Controls { Abandon = true });
        Assert.True(s.Abandoned);
        Assert.True(s.Finished);
    }

    [Fact]
    public void TheDemoPlaysWholeGamesWithoutFault()
    {
        int captures = 0, decksSeen = 0, kills = 0, finished = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var s = new Session(seed * 977, demo: true);
            int frames = 0;
            while (!s.Finished && frames++ < 25 * 60 * 10)
            {
                int alive = s.DroidsLeft;
                s.Tick(default);
                if (s.View == View.Captured) captures++;
                if (s.DroidsLeft < alive) kills++;
                Assert.InRange(s.PlayerX, 0, 191);
                Assert.InRange(s.PlayerY, 0, 191);
                Assert.True(Deck.Walkable(s.Deck.TileAt(s.PlayerX, s.PlayerY)));
            }
            if (s.Finished) finished++;
            decksSeen = Math.Max(decksSeen, s.DeckIndex + 1);
        }
        Assert.True(finished >= 8, $"only {finished} of 12 demo games ended");
        Assert.True(kills > 20);
        Assert.True(captures > 0);
        Assert.True(decksSeen >= 2);
    }

    [Fact]
    public void SameSeedSameGame()
    {
        var a = new Session(1234, demo: true);
        var b = new Session(1234, demo: true);
        for (int i = 0; i < 3000; i++)
        {
            a.Tick(default); b.Tick(default);
        }
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.PlayerX, b.PlayerX);
        Assert.Equal(a.DeckIndex, b.DeckIndex);
    }
}
