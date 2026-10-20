using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Accumulator (#145): a Magic placeable that draws mana units off the tiles beside it into its buffer (a ManaStore,
/// so it adopts the first type it takes and only that type until emptied). Every collectSeconds it takes up to
/// collectPerCycle units from its own tile, then its 4-neighbors in board scan order (its pull draws mana right under it), leaving units of another type in place; a full buffer
/// stops collecting. While it has room it also pulls: mana within PullRadius tiles feels a suction toward it (ManaFlow's
/// pressure), so nearby mana drifts in to be collected; farther mana is drawn in by the emptier patch it leaves, not by
/// the accumulator itself. Its pipe port is output-only: on a pipe network it overflows what's above the reserve into
/// containers (ManaFlow's transfer pass). The ManaFlow clock drives collection; a construction site does nothing.
/// No power behavior in this story.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class Accumulator : MonoBehaviour, IManaPort
{
    [Header("Card (feel)")]
    [SerializeField] private float recentWindowSeconds = 10f; // The card's "collected in the last N s"

    public ManaStore Buffer { get; private set; } = new ManaStore(0);
    public ManaStore Store => Buffer;
    public ManaPortRole Role => ManaPortRole.Output;
    public Vector2Int Tile => GetComponent<Placeable>().Tile;
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;
    public ManaFlow Flow { get; private set; }
    public float CollectSeconds { get; private set; } = 1f;
    public int CollectPerCycle { get; private set; } = 1;
    public float RecentWindowSeconds => recentWindowSeconds;

    /// <summary>How far its pull reaches, in tiles (Manhattan).</summary>
    public int PullRadius { get; private set; }

    /// <summary>The pull's strength at the accumulator itself (density runs 0–1), fading to 0 past PullRadius.</summary>
    public float Suction { get; private set; }

    /// <summary>Pulling now: built, with room in its buffer. A site, or a full buffer, pulls nothing.</summary>
    public bool IsPulling => IsBuilt && !Buffer.IsFull && Suction > 0f;

    private float collectTimer;
    private readonly List<float> collectedAt = new List<float>(); // Sim times of recent units, for the card

    /// <summary>
    /// Called once by PlacementManager at placement: the service, the buffer's capacity, the collection cycle, and the pull's
    /// reach and strength.
    /// </summary>
    public void Initialize(ManaFlow flow, int capacity, float collectSeconds, int collectPerCycle, int pullRadius = 0, float suction = 0f)
    {
        Flow = flow;
        Buffer = new ManaStore(capacity);
        CollectSeconds = collectSeconds;
        CollectPerCycle = Mathf.Max(0, collectPerCycle);
        PullRadius = Mathf.Max(0, pullRadius);
        Suction = Mathf.Max(0f, suction);
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished accumulator joins the service.</summary>
    public void OnConstructed()
    {
        if (Flow != null) Flow.Register(this);
    }

    private void OnDestroy()
    {
        if (Flow != null) Flow.Unregister(this);
    }

    /// <summary>Advances the collection cycle (the ManaFlow clock calls it). A site collects nothing.</summary>
    public void TickCollect(float deltaTime, ManaFlow flow)
    {
        if (!IsBuilt || flow == null) return;
        collectTimer += deltaTime;
        if (CollectSeconds <= 0f) return;
        while (collectTimer >= CollectSeconds)
        {
            collectTimer -= CollectSeconds;
            int taken = Collect(flow, Tile, Buffer, CollectPerCycle);
            for (int i = 0; i < taken; i++) collectedAt.Add(flow.ElapsedSeconds);
        }
    }

    /// <summary>
    /// One collection (a static seam): takes up to max units from tile itself, then its 4-neighbors in board scan order, that the
    /// buffer can accept (empty adopts, mismatched units stay put). Returns how many it took.
    /// </summary>
    public static int Collect(ManaFlow flow, Vector2Int tile, ManaStore buffer, int max)
    {
        int taken = 0;
        if (flow == null || buffer == null) return 0;
        Vector2Int[] scan = { tile, tile + Vector2Int.left, tile + Vector2Int.down, tile + Vector2Int.up, tile + Vector2Int.right };
        foreach (Vector2Int neighbor in scan)
        {
            while (taken < max)
            {
                string type = flow.TypeAt(neighbor);
                if (type == null || !buffer.CanAccept(type)) break;
                if (!flow.TryTakeUnit(neighbor, out string took)) break;
                buffer.Deposit(took, 1);
                taken++;
            }
            if (taken >= max) break;
        }
        return taken;
    }

    /// <summary>Units collected in the last recentWindowSeconds of simulation time (the card's recent rate).</summary>
    public int RecentCollected(float now)
    {
        collectedAt.RemoveAll(t => t < now - recentWindowSeconds);
        return collectedAt.Count;
    }

    /// <summary>Whether a unit it could take sits beside it now (the card tells a starved accumulator from a full one).</summary>
    public bool HasCollectableNeighbor(ManaFlow flow)
    {
        if (flow == null) return false;
        foreach (Vector2Int neighbor in new[] { Tile, Tile + Vector2Int.left, Tile + Vector2Int.down, Tile + Vector2Int.up, Tile + Vector2Int.right })
        {
            string type = flow.TypeAt(neighbor);
            if (type != null && Buffer.CanAccept(type)) return true;
        }
        return false;
    }
}
