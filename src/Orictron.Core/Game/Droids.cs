namespace Orictron.Game;

/// <summary>Droid classes and their numbers, exactly as in the Oric original.</summary>
public static class Droids
{
    public const int Types = 9;

    public static readonly string[] Code = { "", "M1", "S2", "W3", "G4", "B5", "R6", "B7", "X9" };

    public static readonly string[] Name =
    {
        "INFLUENCE DEVICE", "MESSENGER UNIT", "SENTRY DROID", "WORKER UNIT",
        "GUARD ROBOT", "BATTLE ROBOT", "REPAIR ROBOT", "BATTLE DROID", "COMMAND UNIT",
    };

    public static readonly string[] Security = { "NONE", "ALPHA", "ALPHA", "BETA", "GAMMA", "DELTA", "EPSILON", "ZETA", "OMEGA" };

    public static readonly int[] Speed = { 2, 1, 1, 1, 2, 2, 1, 2, 2 };
    public static readonly int[] MaxHp = { 30, 12, 18, 20, 26, 34, 30, 44, 64 };
    public static readonly int[] Damage = { 3, 0, 3, 0, 4, 5, 4, 6, 8 };
    public static readonly int[] FireRate = { 5, 0, 26, 0, 22, 18, 24, 14, 10 };
    public static readonly int[] Mass = { 2, 2, 3, 3, 4, 5, 4, 6, 8 };
    public static readonly int[] Pulses = { 4, 3, 4, 4, 5, 5, 5, 6, 7 };
    /// <summary>Points (shown ×10 on screen).</summary>
    public static readonly int[] Score = { 0, 5, 10, 10, 20, 30, 30, 50, 100 };

    /// <summary>The lid each class wears (the original's sprite designs).</summary>
    public static readonly LidShape[] Lid =
    {
        LidShape.Dome, LidShape.Flat, LidShape.Antenna, LidShape.Flat, LidShape.Crown,
        LidShape.Big, LidShape.Antenna, LidShape.Big, LidShape.Crown,
    };

    public static string Describe(int type) => type == 0 ? Name[0] : Code[type] + " " + Name[type];
}

public enum LidShape { Dome, Flat, Antenna, Crown, Big }
