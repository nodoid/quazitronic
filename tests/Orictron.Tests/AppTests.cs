using Microsoft.Xna.Framework;
using Orictron.Audio;
using Orictron.Input;
using Orictron.Persistence;
using Orictron.Screens;
using Xunit;

namespace Orictron.Tests;

public sealed class TiltTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void LevelIsWhereverYouHoldIt(float side)
    {
        var t = new TiltController();
        var held = new Vector3(0.7f * side, 0, -0.7f);
        t.Calibrate(held);
        Assert.Equal(Vector2.Zero, t.Update(held));
        Assert.Equal(Vector2.Zero, t.Update(held + new Vector3(0.02f * side, 0.02f, 0)));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void TippingAwayIsUpTheScreen(float side)
    {
        var t = new TiltController();
        t.Calibrate(new Vector3(0.7f * side, 0, -0.7f));
        Assert.True(t.Update(Vector3.Normalize(new Vector3(0.45f * side, 0, -0.9f))).Y > 0.5f);
        Assert.True(t.Update(Vector3.Normalize(new Vector3(0.9f * side, 0, -0.45f))).Y < -0.5f);
    }

    [Fact]
    public void DippingTheRightSideSteersRight()
    {
        var t = new TiltController();
        t.Calibrate(new Vector3(0.7f, 0, -0.7f));
        Assert.True(t.Update(Vector3.Normalize(new Vector3(0.7f, 0.3f, -0.65f))).X > 0.5f);
        Assert.True(t.Update(Vector3.Normalize(new Vector3(0.7f, -0.3f, -0.65f))).X < -0.5f);
    }

    [Fact]
    public void TiltPressesTheSameButtonsAsTheKeysIncludingDiagonals()
    {
        var input = new InputState();
        input.SetTilt(new Vector2(-0.8f, 0.8f));
        Assert.True(input.Held(Pad.Left));
        Assert.True(input.Held(Pad.Up));
        Assert.True(input.Pressed(Pad.Left));
        input.SetTilt(new Vector2(-0.4f, 0));
        Assert.True(input.Held(Pad.Left));
        Assert.False(input.Pressed(Pad.Left));
        Assert.False(input.Held(Pad.Up));
        input.SetTilt(new Vector2(-0.1f, 0));
        Assert.False(input.Held(Pad.Left));
    }
}

public sealed class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orictron-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SettingsAndScoresSurviveRelaunch()
    {
        var store = new SaveStore(_dir);
        var data = store.Load();
        data.Graphics = "Original";
        data.Music = false;
        data.TiltControls = false;
        data.Insert(new ScoreEntry { Score = 4500, Deck = 3 });
        Assert.True(store.Save(data));
        var again = new SaveStore(_dir).Load();
        Assert.Equal("Original", again.Graphics);
        Assert.False(again.Music);
        Assert.False(again.TiltControls);
        Assert.Equal(4500, again.Best);
    }

    [Fact]
    public void ACorruptSaveFallsBackToTheBackup()
    {
        var store = new SaveStore(_dir);
        var data = new SaveData();
        data.Insert(new ScoreEntry { Score = 100, Deck = 1 });
        store.Save(data);
        data.Insert(new ScoreEntry { Score = 200, Deck = 1 });
        store.Save(data);                       // the first save becomes the backup
        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Equal(100, new SaveStore(_dir).Load().Best);
    }

    [Fact]
    public void BestScoresKeepTheTopFiveInOrder()
    {
        var d = new SaveData();
        foreach (int s in new[] { 50, 500, 5, 300, 900, 10, 700 }) d.Insert(new ScoreEntry { Score = s, Deck = 1 });
        Assert.Equal(new[] { 900, 700, 500, 300, 50 }, d.Scores.Select(e => e.Score));
        Assert.Equal(-1, d.RankFor(10));
        Assert.Equal(0, d.RankFor(1000));
        Assert.Equal(-1, d.RankFor(0));
    }

    [Fact]
    public void AGameBankedMidwayIsUpdatedNotDuplicated()
    {
        var d = new SaveData();
        ScoreEntry? entry = null;
        d.Record(ref entry, new ScoreEntry { Score = 300, Deck = 1 });   // app backgrounded mid-game
        d.Record(ref entry, new ScoreEntry { Score = 900, Deck = 2 });   // carried on, then finished
        Assert.Single(d.Scores);
        Assert.Equal(900, d.Best);
        Assert.Equal(2, d.Scores[0].Deck);
    }

    [Fact]
    public void ScoresCarryOverToTheNextSession()
    {
        var first = new SaveStore(_dir);
        var data = first.Load();
        ScoreEntry? entry = null;
        data.Record(ref entry, new ScoreEntry { Score = 2500, Deck = 3 });
        first.Save(data);
        var next = new SaveStore(_dir).Load();         // a later launch
        Assert.Equal(2500, next.Best);
        ScoreEntry? e2 = null;
        next.Record(ref e2, new ScoreEntry { Score = 1200, Deck = 2 });
        new SaveStore(_dir).Save(next);
        var third = new SaveStore(_dir).Load();
        Assert.Equal(new[] { 2500, 1200 }, third.Scores.Select(e => e.Score));
    }

    [Fact]
    public void NormalizeRepairsBadValues()
    {
        var d = new SaveData { Graphics = "Weird" };
        d.Scores.Add(new ScoreEntry { Score = 10, Deck = 99 });
        d.Scores.Add(new ScoreEntry { Score = 30, Deck = 0 });
        d.Scores.Add(new ScoreEntry { Score = -5, Deck = 1 });
        d.Normalize();
        Assert.Equal("Enhanced", d.Graphics);
        Assert.Equal(new[] { 30, 10 }, d.Scores.Select(e => e.Score));
        Assert.All(d.Scores, e => Assert.InRange(e.Deck, 1, 6));
    }
}

public sealed class AudioTests
{
    [Fact]
    public void EverySoundIsSynthesisedCleanly()
    {
        var fx = Sounds.Synthesize();
        foreach (Sfx s in Enum.GetValues<Sfx>())
        {
            Assert.True(fx.ContainsKey(s), s.ToString());
            var b = fx[s];
            Assert.True(b.Length > 100, s.ToString());
            Assert.All(b, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1.0001f));
            Assert.True(b.Max(Math.Abs) > 0.02f, s + " is silent");
        }
    }

    [Theory]
    [InlineData(Song.Title)]
    [InlineData(Song.Deck)]
    public void MusicLoopsAreSeamlessAndBounded(Song song)
    {
        var m = Music.Render(song);
        Assert.True(m.Length > Synth.Rate * 10);
        Assert.All(m, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
        // the end flows into the start: no big jump at the loop point
        Assert.True(Math.Abs(m[^1] - m[0]) < 0.25f);
    }

    [Fact]
    public void TheMixerRendersOfflineSampleExact()
    {
        var engine = new AudioEngine();
        int got = 0;
        engine.OfflineSink = s => got += s.Length;
        engine.Play(new float[1000]);
        for (int i = 0; i < 30; i++) engine.Update(1 / 30f);
        Assert.InRange(got, AudioEngine.Rate - 2, AudioEngine.Rate);
    }

    [Fact]
    public void ChipVoicesFollowTheOriginalSoundCode()
    {
        var chip = new ChipSound();
        chip.Shot();
        Assert.Equal((40, 12, 0, 0, 0), chip.State);
        chip.Frame();                          // snd_update: the laser falls in pitch and fades
        Assert.Equal((52, 11, 0, 0, 0), chip.State);
        chip.Boom();
        chip.Blip(60);
        Assert.Equal(15, chip.State.BVolume);
        Assert.Equal((60, 10), (chip.State.CPeriod, chip.State.CVolume));
        for (int i = 0; i < 20; i++) chip.Frame();
        Assert.Equal((0, 0, 0), (chip.State.AVolume, chip.State.BVolume, chip.State.CVolume));
    }

    [Fact]
    public void ChipVoicesAreAudibleAndSilentWhenOff()
    {
        var chip = new ChipSound();
        var engine = new AudioEngine { Chip = chip };
        float peak = 0;
        engine.OfflineSink = s => { foreach (var v in s) peak = Math.Max(peak, Math.Abs(v)); };
        chip.Boom();
        engine.Update(0.1f);
        Assert.True(peak > 0.05f);
        chip.Off();
        engine.Update(0.5f);                   // let the DC filter settle
        peak = 0;
        engine.Update(0.1f);
        Assert.True(peak < 0.01f);
    }
}

public sealed class UiTests
{
    [Fact]
    public void TheCreditsLineReadsAsRequested()
    {
        Assert.Equal("Written by PFJ, Based on the Oric port of the ZX Spectrum game by Hewson", CreditsScroller.Text);
    }

    [Fact]
    public void TheCreditsScrollLeftToRightAndWrap()
    {
        var c = new CreditsScroller();
        c.Update(0, 320);
        float x0 = c.X;
        c.Update(0.5f, 320);
        Assert.True(c.X > x0);                         // moving right
        for (int i = 0; i < 2000 && c.X >= x0; i++) c.Update(0.1f, 320);
        Assert.True(c.X < 0);                          // came back in from the left
        Assert.True(c.X + Orictron.Graphics.Gfx.TextWidth(CreditsScroller.Text) <= 0);
    }

    [Fact]
    public void InstructionTextWraps()
    {
        var lines = InstructionsScreen.Wrap("one two three four five six seven", 10);
        Assert.All(lines, l => Assert.True(l.Length <= 10));
        Assert.Equal("one two three four five six seven", string.Join(" ", lines));
        Assert.Single(InstructionsScreen.Wrap("a  table   row that is long", 5));
    }
}
