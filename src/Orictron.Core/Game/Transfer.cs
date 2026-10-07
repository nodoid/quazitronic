namespace Orictron.Game;

/// <summary>
/// The transfer battle. Two banks of wires feed a column of cells: the player's side is yellow
/// (left), the droid's blue (right). A pulse fired down a wire turns its cell to the firer's colour
/// for a while (75 frames). Whoever holds more cells when the timer runs out wins.
/// Wire kinds: 0 normal, 1 dead end, 2 joins the wire below at its middle, 3 fed by the wire above
/// (no wire of its own before the join).
/// </summary>
public sealed class Transfer
{
    public const int Wires = 13;
    /// <summary>Wire length in steps (columns on the Oric screen).</summary>
    public const int Length = 11;
    public const int DeadEndLength = 4;
    public const int HoldFrames = 75;

    public readonly int[] Base = new int[Wires], Owner = new int[Wires], Hold = new int[Wires];
    public readonly int[] LeftKind = new int[Wires], RightKind = new int[Wires];
    public readonly int[] LeftPulse = new int[Wires], RightPulse = new int[Wires];
    /// <summary>Frames since each side's wire was last used (for the powered-wire glow).</summary>
    public readonly int[] LeftLit = new int[Wires], RightLit = new int[Wires];

    public int PlayerPulses, EnemyPulses, Cursor, Seconds;
    public int PlayerType, EnemyType;
    /// <summary>Result animation: cells flipped so far, and which flash of the border (-1 = none).</summary>
    public int ResultStep = -1, FlashPhase = -1;
    public bool ResultWin;

    public void Setup(Session s, int enemyType)
    {
        PlayerType = s.PlayerType;
        EnemyType = enemyType;
        PlayerPulses = Droids.Pulses[s.PlayerType];
        EnemyPulses = Droids.Pulses[enemyType];
        for (int w = 0; w < Wires; w++)
        {
            Base[w] = Owner[w] = w & 1;
            Hold[w] = LeftPulse[w] = RightPulse[w] = 0;
            LeftKind[w] = RightKind[w] = 0;
            LeftLit[w] = RightLit[w] = 0;
        }
        for (int i = 0; i < 16; i++)
        {
            int w = s.Rnd() % Wires;
            int c = s.Rnd() % Wires;
            (Base[w], Base[c]) = (Base[c], Base[w]);
        }
        // dead ends and joins
        for (int i = 0; i < 3; i++)
        {
            int w = s.Rnd() % Wires;
            if (LeftKind[w] == 0) LeftKind[w] = 1;
            w = s.Rnd() % Wires;
            if (RightKind[w] == 0) RightKind[w] = 1;
        }
        for (int i = 0; i < 2; i++)
        {
            int w = s.Rnd() % (Wires - 1);
            if (LeftKind[w] == 0 && LeftKind[w + 1] == 0) { LeftKind[w] = 2; LeftKind[w + 1] = 3; }
            w = s.Rnd() % (Wires - 1);
            if (RightKind[w] == 0 && RightKind[w + 1] == 0) { RightKind[w] = 2; RightKind[w + 1] = 3; }
        }
        for (int w = 0; w < Wires; w++) Owner[w] = Base[w];
        Cursor = 0;
        ResultStep = -1;
        FlashPhase = -1;
    }

    /// <summary>How far a wire's pulse can travel before it reaches the cell (or dies, for a dead end).</summary>
    public static int End(int kind) => kind == 1 ? DeadEndLength : Length;

    /// <summary>Cells each side holds right now.</summary>
    public int Count(int side)
    {
        int n = 0;
        for (int w = 0; w < Wires; w++) if (Owner[w] == side) n++;
        return n;
    }

    private void Take(int w, int side)
    {
        Owner[w] = side;
        Hold[w] = HoldFrames;
    }

    /// <summary>Advances the pulse on wire w; side 0 = player (left), 1 = droid (right).</summary>
    internal void AdvancePulse(Session s, int side, int w)
    {
        int[] pp = side != 0 ? RightPulse : LeftPulse;
        int kind = side != 0 ? RightKind[w] : LeftKind[w];
        int prog = pp[w], end = End(kind);
        (side != 0 ? RightLit : LeftLit)[w] = 1;
        if (kind == 2 && prog > 6) (side != 0 ? RightLit : LeftLit)[w + 1] = 1;
        if (prog <= end)
        {
            pp[w]++;
        }
        else
        {
            if (kind != 1)
            {
                Take(w, side);
                if (kind == 2) Take(w + 1, side);
                s.CueFromTransfer(side != 0 ? Cue.EnemyCellTaken : Cue.CellTaken, side != 0 ? 120 : 30);
            }
            pp[w] = 0;
        }
    }
}
