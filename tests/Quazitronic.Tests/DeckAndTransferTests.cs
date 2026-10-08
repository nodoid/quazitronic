using Quazitronic.Game;
using Xunit;

namespace Quazitronic.Tests;

public sealed class DeckAndTransferTests
{
    public static IEnumerable<object[]> AllDecks() => Enumerable.Range(0, DeckData.Count).Select(d => new object[] { d });

    [Theory]
    [MemberData(nameof(AllDecks))]
    public void EveryWalkableTileCanBeReachedFromTheLift(int d)
    {
        var deck = DeckData.Get(d);
        Assert.Equal(TileKind.Lift, deck.Kind(deck.StartI, deck.StartJ));
        var seen = new HashSet<(int, int)> { (deck.StartI, deck.StartJ) };
        var queue = new Queue<(int, int)>(seen);
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int ni = i + di, nj = j + dj;
                if (seen.Contains((ni, nj)) || !Deck.Walkable(deck.Kind(ni, nj))) continue;
                int dl = deck.Level(ni, nj) - deck.Level(i, j);
                bool pad = deck.Kind(ni, nj) == TileKind.Pad || deck.Kind(i, j) == TileKind.Pad;
                if (dl != 0 && !(Math.Abs(dl) == 1 && pad)) continue;
                seen.Add((ni, nj));
                queue.Enqueue((ni, nj));
            }
        }
        for (int j = 0; j < Deck.Size; j++)
            for (int i = 0; i < Deck.Size; i++)
                if (Deck.Walkable(deck.Kind(i, j))) Assert.Contains((i, j), seen);
    }

    [Theory]
    [MemberData(nameof(AllDecks))]
    public void DecksHaveEnergisersAndAnEdgeOfVoid(int d)
    {
        var deck = DeckData.Get(d);
        int energisers = 0;
        for (int j = 0; j < Deck.Size; j++)
            for (int i = 0; i < Deck.Size; i++)
            {
                if (deck.Kind(i, j) == TileKind.Energiser) energisers++;
                Assert.InRange(deck.Level(i, j), 0, 3);
            }
        Assert.True(energisers >= 4);
        Assert.Equal(TileKind.Void, deck.Kind(0, 0));
        Assert.Equal(0, deck.TileAt(300, 5)); // off the map is void
    }

    [Fact]
    public void TheSixDecksHaveTheOriginalColours()
    {
        Assert.Equal(new[] { 7, 6, 4, 2, 5, 3 }, Enumerable.Range(0, 6).Select(d => DeckData.Get(d).OricInk));
    }

    [Fact]
    public void TransferSetupMakesValidWires()
    {
        for (int seed = 1; seed < 40; seed++)
        {
            var s = new Session(seed);
            var tr = new Transfer();
            tr.Setup(s, 5);
            Assert.Equal(Droids.Pulses[0], tr.PlayerPulses);
            Assert.Equal(Droids.Pulses[5], tr.EnemyPulses);
            Assert.Equal(13, tr.Owner.Length);
            int ones = tr.Base.Count(b => b == 1);
            Assert.Equal(6, ones); // the cells start shared out, 7 to 6
            for (int w = 0; w < Transfer.Wires; w++)
            {
                if (tr.LeftKind[w] == 2) Assert.Equal(3, tr.LeftKind[w + 1]);
                if (tr.LeftKind[w] == 3) Assert.Equal(2, tr.LeftKind[w - 1]);
                if (tr.RightKind[w] == 2) Assert.Equal(3, tr.RightKind[w + 1]);
            }
        }
    }

    [Fact]
    public void APulseTakesItsCellAndADeadEndDoesNot()
    {
        var s = new Session(2);
        var tr = new Transfer();
        tr.Setup(s, 3);
        int normal = Array.FindIndex(tr.LeftKind, k => k == 0);
        tr.Owner[normal] = 1;
        tr.LeftPulse[normal] = 1;
        for (int i = 0; i < 20 && tr.LeftPulse[normal] != 0; i++) tr.AdvancePulse(s, 0, normal);
        Assert.Equal(0, tr.Owner[normal]);
        Assert.Equal(Transfer.HoldFrames, tr.Hold[normal]);

        tr.LeftKind[0] = 1;
        tr.Owner[0] = 1;
        tr.Hold[0] = 0;
        tr.LeftPulse[0] = 1;
        for (int i = 0; i < 20 && tr.LeftPulse[0] != 0; i++) tr.AdvancePulse(s, 0, 0);
        Assert.Equal(1, tr.Owner[0]);
    }

    [Fact]
    public void ASplitWireTakesTwoCells()
    {
        var s = new Session(2);
        var tr = new Transfer();
        tr.Setup(s, 3);
        Array.Fill(tr.LeftKind, 0);
        tr.LeftKind[4] = 2; tr.LeftKind[5] = 3;
        tr.Owner[4] = tr.Owner[5] = 1;
        tr.LeftPulse[4] = 1;
        for (int i = 0; i < 20 && tr.LeftPulse[4] != 0; i++) tr.AdvancePulse(s, 0, 4);
        Assert.Equal(0, tr.Owner[4]);
        Assert.Equal(0, tr.Owner[5]);
    }

    [Fact]
    public void DroidClassesClimbInStrength()
    {
        for (int t = 2; t < Droids.Types; t++)
            Assert.True(Droids.MaxHp[t] >= Droids.MaxHp[1]);
        Assert.Equal(64, Droids.MaxHp[8]);
        Assert.Equal("X9 COMMAND UNIT", Droids.Describe(8));
        Assert.Equal("INFLUENCE DEVICE", Droids.Describe(0));
    }
}
