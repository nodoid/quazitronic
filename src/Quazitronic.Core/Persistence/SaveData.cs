using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Quazitronic.Persistence;

public sealed class ScoreEntry
{
    /// <summary>Score as shown on screen (the original's internal score × 10).</summary>
    public int Score { get; set; }
    /// <summary>Deepest deck reached (1-6).</summary>
    public int Deck { get; set; }
    public bool Secured { get; set; }
    /// <summary>ISO date (yyyy-MM-dd).</summary>
    public string Date { get; set; } = "";
}

/// <summary>Everything that survives between launches: preferences and the enhanced game's best scores.</summary>
public sealed class SaveData
{
    public const int CurrentVersion = 1;
    public const int TableSize = 5;

    public int Version { get; set; } = CurrentVersion;
    /// <summary>"Enhanced" (new graphics and sound) or "Original" (the Oric version's look and sound).</summary>
    public string Graphics { get; set; } = "Enhanced";
    public bool Sound { get; set; } = true;
    public bool Music { get; set; } = true;
    /// <summary>Phones and tablets: steer by tilting (true) or with the on-screen D-pad.</summary>
    public bool TiltControls { get; set; } = true;
    public List<ScoreEntry> Scores { get; set; } = new();
    public int GamesPlayed { get; set; }

    [JsonIgnore]
    public int Best => Scores.Count > 0 ? Scores[0].Score : 0;

    /// <summary>Where a score would land in the table (0-based), or -1 if it doesn't qualify.</summary>
    public int RankFor(int score)
    {
        if (score <= 0) return -1;
        for (int i = 0; i < Scores.Count; i++)
            if (score > Scores[i].Score) return i;
        return Scores.Count < TableSize ? Scores.Count : -1;
    }

    public int Insert(ScoreEntry entry)
    {
        int rank = RankFor(entry.Score);
        if (rank < 0) return -1;
        Scores.Insert(rank, entry);
        if (Scores.Count > TableSize) Scores.RemoveRange(TableSize, Scores.Count - TableSize);
        return rank;
    }

    /// <summary>
    /// Records a game's score, or updates the entry an unfinished game already has in the table (a
    /// game banked when the app went to the background and then carried on). Returns the rank, or -1.
    /// </summary>
    public int Record(ref ScoreEntry? entry, ScoreEntry latest)
    {
        if (entry != null && Scores.Contains(entry))
        {
            if (latest.Score <= entry.Score && latest.Deck <= entry.Deck && latest.Secured == entry.Secured) return Scores.IndexOf(entry);
            Scores.Remove(entry);
        }
        int rank = Insert(latest);
        entry = rank >= 0 ? latest : null;
        return rank;
    }

    /// <summary>Repairs anything a hand-edited or older save might have wrong.</summary>
    public void Normalize()
    {
        if (Graphics != "Original") Graphics = "Enhanced";
        Scores ??= new List<ScoreEntry>();
        Scores.RemoveAll(s => s == null || s.Score <= 0);
        Scores.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (Scores.Count > TableSize) Scores.RemoveRange(TableSize, Scores.Count - TableSize);
        foreach (var s in Scores) s.Deck = Math.Clamp(s.Deck, 1, 6);
        Version = CurrentVersion;
    }
}

[JsonSerializable(typeof(SaveData))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class SaveJsonContext : JsonSerializerContext
{
}
