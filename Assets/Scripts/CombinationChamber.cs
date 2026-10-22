using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Combination Chamber (#146): a Magic placeable that hosts one process from ManaFlow's process table (its host rows).
/// It holds no general store: it stages exactly one cycle of its row's inputs, drawn through its own tile's pipe by the
/// transfer pass, runs the cycle, and holds the product until the transfer pass deposits it into connected stores. It
/// runs unattended and unpowered. A new chamber runs the table's first chamber row; the row is switchable (its card)
/// only while it's idle and empty. The ManaFlow clock drives the cycle; a construction site does nothing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class CombinationChamber : MonoBehaviour, IManaProcessorHost
{
    public ManaProcessor Processor { get; private set; } = new ManaProcessor(null);
    public Vector2Int Tile => GetComponent<Placeable>().Tile;
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;
    public ManaFlow Flow { get; private set; }

    /// <summary>Called once by PlacementManager at placement: the service, running its first chamber row.</summary>
    public void Initialize(ManaFlow flow)
    {
        Flow = flow;
        List<ManaProcessRow> rows = Rows;
        Processor = new ManaProcessor(rows.Count > 0 ? rows[0] : null);
    }

    /// <summary>The rows this chamber can run: the table's chamber rows, in table order.</summary>
    public List<ManaProcessRow> Rows => Flow != null ? Flow.RowsFor(PlaceableType.CombinationChamber) : new List<ManaProcessRow>();

    /// <summary>The running row's index among Rows (-1 for none).</summary>
    public int SelectedIndex => Rows.IndexOf(Processor.Row);

    /// <summary>Switches to Rows[index]; false (nothing changes) unless idle and empty, or for a bad index.</summary>
    public bool SelectProcess(int index)
    {
        List<ManaProcessRow> rows = Rows;
        return index >= 0 && index < rows.Count && Processor.TrySetRow(rows[index]);
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished chamber joins the service.</summary>
    public void OnConstructed()
    {
        if (Flow != null) Flow.RegisterProcessor(this);
    }

    private void OnDestroy()
    {
        if (Flow != null) Flow.UnregisterProcessor(this);
    }
}
