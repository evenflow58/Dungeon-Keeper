using UnityEngine;

/// <summary>
/// The Mana Container (#145): a Magic placeable that bulk-stores one type of mana (a ManaStore: adopts the first type
/// deposited, forgets it when emptied). Passive — it fills and drains only through pipes, via ManaFlow's transfer pass;
/// its port is input + output. On no pipe network it simply holds what it has.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class ManaContainer : MonoBehaviour, IManaPort
{
    public ManaStore Store { get; private set; } = new ManaStore(0);
    public ManaPortRole Role => ManaPortRole.InputOutput;
    public Vector2Int Tile => GetComponent<Placeable>().Tile;
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;
    public ManaFlow Flow { get; private set; }

    /// <summary>Called once by PlacementManager at placement: the service and the store's capacity.</summary>
    public void Initialize(ManaFlow flow, int capacity)
    {
        Flow = flow;
        Store = new ManaStore(capacity);
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished container joins the service.</summary>
    public void OnConstructed()
    {
        if (Flow != null) Flow.Register(this);
    }

    private void OnDestroy()
    {
        if (Flow != null) Flow.Unregister(this);
    }
}
