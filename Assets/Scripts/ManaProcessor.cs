using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>An amount of one mana type (#146): a process row's input or output. Types are data ids, never an enum.</summary>
[Serializable]
public class ManaAmount
{
    public string type;
    public int amount;

    public ManaAmount(string type, int amount)
    {
        this.type = type;
        this.amount = amount;
    }
}

/// <summary>
/// One row of the process table (#146): a host building type, the inputs one cycle consumes, the outputs it produces, and
/// the cycle's length. The Combination Chamber is the pilot host; later hosts (kilns, cooks) add rows, not machinery.
/// </summary>
[Serializable]
public class ManaProcessRow
{
    public PlaceableType host;
    public List<ManaAmount> inputs = new List<ManaAmount>();
    public List<ManaAmount> outputs = new List<ManaAmount>();
    public float cycleSeconds;

    public ManaProcessRow(PlaceableType host, ManaAmount[] inputs, ManaAmount[] outputs, float cycleSeconds)
    {
        this.host = host;
        this.inputs = new List<ManaAmount>(inputs);
        this.outputs = new List<ManaAmount>(outputs);
        this.cycleSeconds = cycleSeconds;
    }

    /// <summary>The row's name, from its data: "2 Fire + 2 Water → 3 Air".</summary>
    public string Name => $"{Join(inputs)} → {Join(outputs)}";

    /// <summary>How many of a type one cycle consumes (0 when it isn't an input).</summary>
    public int InputOf(string type)
    {
        int n = 0;
        foreach (ManaAmount a in inputs) if (a != null && a.type == type) n += a.amount;
        return n;
    }

    private static string Join(List<ManaAmount> amounts)
    {
        var sb = new StringBuilder();
        foreach (ManaAmount a in amounts)
        {
            if (a == null) continue;
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(a.amount).Append(' ').Append(a.type);
        }
        return sb.ToString();
    }
}

/// <summary>Why a processor isn't progressing (#146): noted on its card, never acted on.</summary>
public enum ManaProcessorStall
{
    None,
    MissingInput,    // Idle, inputs incomplete, and nothing on its network can supply one of them
    NoRoomForOutput, // Holding product that no connected store can take
}

/// <summary>
/// A process host's working state (#146), plain data so tests drive it directly. It runs ONE row and stages exactly one
/// cycle: while idle it demands the row's inputs minus what it has staged (the transfer pass fills that demand); the
/// moment the full set is staged, the inputs are consumed and the cycle runs for cycleSeconds; at completion the outputs
/// are held until the transfer pass deposits them, and no new cycle starts until everything is out. Staged inputs and
/// held product are real mana (they count in the global totals). The stall reasons are set by the transfer pass, which
/// is what knows whether supply or room exists. A row changes only while it's empty: a switch asked for mid-commitment
/// waits as PendingRow — the processor finishes what it has started (stages the rest of a begun set, runs, deposits),
/// starts nothing new, and switches the moment it's empty.
/// </summary>
public class ManaProcessor
{
    private readonly Dictionary<string, int> staged = new Dictionary<string, int>();
    private readonly Dictionary<string, int> held = new Dictionary<string, int>();

    public ManaProcessor(ManaProcessRow row) { Row = row; }

    public ManaProcessRow Row { get; private set; }

    /// <summary>The row it switches to once it's empty (a switch asked for mid-commitment); null when none waits.</summary>
    public ManaProcessRow PendingRow { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>Seconds into the running cycle (0 while not running).</summary>
    public float ProgressSeconds { get; private set; }

    /// <summary>The running cycle's progress, 0–1.</summary>
    public float Progress => IsRunning && Row != null && Row.cycleSeconds > 0f ? Mathf.Clamp01(ProgressSeconds / Row.cycleSeconds) : 0f;

    public int CyclesCompleted { get; private set; }

    /// <summary>The input the last transfer pass found no supply for, while idle and short of it; null otherwise.</summary>
    public string MissingType { get; private set; }

    /// <summary>True when the last transfer pass found no connected store able to take the held product.</summary>
    public bool OutputBlocked { get; private set; }

    public bool IsHolding => Total(held) > 0;
    public int StagedTotal => Total(staged);
    public int HeldTotal => Total(held);

    /// <summary>Idle and empty: nothing staged, nothing running, nothing held. The only state a row may change in.</summary>
    public bool IsEmpty => !IsRunning && StagedTotal == 0 && !IsHolding;

    public bool CanSwitch => IsEmpty;

    public ManaProcessorStall Stall =>
        IsHolding && OutputBlocked ? ManaProcessorStall.NoRoomForOutput
        : !IsRunning && !IsHolding && MissingType != null ? ManaProcessorStall.MissingInput
        : ManaProcessorStall.None;

    /// <summary>"Missing Water", "No room for output", or empty when not stalled.</summary>
    public string StallText =>
        Stall == ManaProcessorStall.MissingInput ? $"Missing {MissingType}"
        : Stall == ManaProcessorStall.NoRoomForOutput ? "No room for output"
        : "";

    public int StagedOf(string type) => type != null && staged.TryGetValue(type, out int n) ? n : 0;
    public int HeldOf(string type) => type != null && held.TryGetValue(type, out int n) ? n : 0;

    /// <summary>Every type it holds now, staged or as product (for totals and discovery).</summary>
    public IEnumerable<KeyValuePair<string, int>> Holdings()
    {
        foreach (KeyValuePair<string, int> e in staged) if (e.Value > 0) yield return e;
        foreach (KeyValuePair<string, int> e in held) if (e.Value > 0) yield return e;
    }

    /// <summary>The held product's types, in the row's output order.</summary>
    public List<string> HeldTypes()
    {
        var list = new List<string>();
        if (Row != null)
            foreach (ManaAmount a in Row.outputs)
                if (a != null && HeldOf(a.type) > 0 && !list.Contains(a.type)) list.Add(a.type);
        foreach (KeyValuePair<string, int> e in held)
            if (e.Value > 0 && !list.Contains(e.Key)) list.Add(e.Key);
        return list;
    }

    /// <summary>
    /// How many more of a type it wants staged: only while idle (not running, holding nothing), and with a switch pending
    /// only to finish a set it has begun (it starts no new one).
    /// </summary>
    public int Demand(string type) =>
        Row == null || IsRunning || IsHolding || (PendingRow != null && StagedTotal == 0) ? 0
        : Mathf.Max(0, Row.InputOf(type) - StagedOf(type));

    public bool IsFullyStaged
    {
        get
        {
            if (Row == null || Row.inputs.Count == 0) return false;
            foreach (ManaAmount a in Row.inputs)
                if (a != null && StagedOf(a.type) < Row.InputOf(a.type)) return false;
            return true;
        }
    }

    /// <summary>Stages up to amount of a type, as far as its demand goes; returns how many it took. A full set starts the cycle.</summary>
    public int Stage(string type, int amount)
    {
        int take = Mathf.Min(Mathf.Max(0, amount), Demand(type));
        if (take <= 0) return 0;
        staged[type] = StagedOf(type) + take;
        if (MissingType == type && Demand(type) == 0) MissingType = null;
        TryStart();
        return take;
    }

    /// <summary>Takes up to amount of held product of a type (the deposit); returns how many came out.</summary>
    public int TakeHeld(string type, int amount)
    {
        int take = Mathf.Min(Mathf.Max(0, amount), HeldOf(type));
        if (take <= 0) return 0;
        held[type] = HeldOf(type) - take;
        if (held[type] == 0) held.Remove(type);
        if (!IsHolding)
        {
            OutputBlocked = false;
            ApplyPendingRow(); // The product is out: a waiting switch happens now, before anything new is drawn
        }
        return take;
    }

    /// <summary>Advances the running cycle; at completion the row's outputs are held. Starts a cycle when a full set waits.</summary>
    public void Tick(float deltaTime)
    {
        ApplyPendingRow();
        TryStart();
        if (!IsRunning || deltaTime <= 0f) return;
        ProgressSeconds += deltaTime;
        if (ProgressSeconds < Row.cycleSeconds - 1e-4f) return;
        IsRunning = false;
        ProgressSeconds = 0f;
        foreach (ManaAmount a in Row.outputs)
            if (a != null && a.amount > 0) held[a.type] = HeldOf(a.type) + a.amount;
        CyclesCompleted++;
    }

    /// <summary>Switches to another row now; refused (false) unless empty — a switch mid-commitment would strand units.</summary>
    public bool TrySetRow(ManaProcessRow row)
    {
        if (!CanSwitch || row == null) return false;
        Row = row;
        PendingRow = null;
        MissingType = null;
        OutputBlocked = false;
        return true;
    }

    /// <summary>
    /// Asks for a row: switches now when empty, otherwise waits as PendingRow until the current commitment is out. Asking
    /// for the running row cancels a waiting switch. False only for no row.
    /// </summary>
    public bool RequestRow(ManaProcessRow row)
    {
        if (row == null) return false;
        if (row == Row)
        {
            PendingRow = null;
            return true;
        }
        if (TrySetRow(row)) return true;
        PendingRow = row;
        return true;
    }

    private void ApplyPendingRow()
    {
        if (PendingRow != null && IsEmpty) TrySetRow(PendingRow);
    }

    /// <summary>The transfer pass's verdicts: the input it found no supply for (or null), and whether the product is stuck.</summary>
    public void SetStallVerdicts(string missingType, bool outputBlocked)
    {
        MissingType = missingType;
        OutputBlocked = outputBlocked && IsHolding;
    }

    private void TryStart()
    {
        if (IsRunning || IsHolding || !IsFullyStaged) return;
        staged.Clear(); // Consumed: the row's inputs leave the world here and only here
        MissingType = null;
        IsRunning = true;
        ProgressSeconds = 0f;
    }

    private static int Total(Dictionary<string, int> d)
    {
        int n = 0;
        foreach (int v in d.Values) n += v;
        return n;
    }
}

/// <summary>A building that hosts a process and attaches to a pipe by its own tile (#146).</summary>
public interface IManaProcessorHost
{
    Vector2Int Tile { get; }
    ManaProcessor Processor { get; }
}
