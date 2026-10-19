using UnityEngine;

/// <summary>
/// A placeable's place on the power grid (#142). PlacementManager attaches it at placement to every type whose power row
/// has a role (the MushroomPlot pattern); it joins the PowerGrid when construction completes (a site is never a node) and
/// leaves it when destroyed. It reports its role, watts and tile, and whether it's active right now: a producer that needs
/// an operator (the Imp Wheel) is active only while an imp tends it; any other built node is always active.
/// The unpowered marker: a built consumer that's connected to a grid that can't cover it shows a small lightning badge
/// above it. Producers, sites and unconnected consumers (a trap with no conduit, as before #142) never show it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class PowerNode : MonoBehaviour
{
    [Header("Unpowered Marker")]
    [SerializeField] private Color markerColor = new Color(1f, 0.82f, 0.18f, 1f); // Bright yellow: wants power
    [SerializeField] private float markerHeight = 0.75f;                          // Badge centre above the ground, tiles
    [SerializeField] private float markerSize = 0.28f;                            // Badge height, tiles
    [SerializeField] private float markerSmoothness = 0.3f;

    public PowerRole Role { get; private set; }
    public int Watts { get; private set; }

    /// <summary>True for a producer that makes watts only while an imp operates it (the Imp Wheel).</summary>
    public bool NeedsOperator { get; private set; }

    public PowerGrid Grid { get; private set; }

    public Vector2Int Tile => GetComponent<Placeable>().Tile;
    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;

    /// <summary>The imp operating this node now, or null. Set by ImpDigger when it starts operating, cleared when it leaves.</summary>
    public ImpDigger Operator { get; private set; }

    /// <summary>An operator is at work here: it still holds this node and is operating, not walking to it.</summary>
    public bool IsTended => Operator != null && Operator.CurrentWheel == this && Operator.IsOperating;

    /// <summary>Active now: a built node, and for an operated producer, tended. The grid's live supply reads this.</summary>
    public bool IsActive => IsBuilt && (!NeedsOperator || IsTended);

    /// <summary>This node as the grid math sees it.</summary>
    public PowerGrid.NodeState State => new PowerGrid.NodeState(Tile, Role, Watts, IsActive);

    /// <summary>The marker badge's root (null until Initialize); active exactly while ShowsMarker.</summary>
    public GameObject Marker { get; private set; }

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(PowerGrid grid, PowerRole role, int watts, bool needsOperator)
    {
        Grid = grid;
        Role = role;
        Watts = Mathf.Max(0, watts);
        NeedsOperator = needsOperator;
        if (Marker == null) Marker = CreateMarker();
        RefreshMarker();
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished node joins its grid.</summary>
    public void OnConstructed()
    {
        if (Grid != null) Grid.Register(this);
        RefreshMarker();
    }

    private void OnDestroy()
    {
        if (Grid != null) Grid.Unregister(this);
    }

    /// <summary>Claims this node for an operator (the imp arriving at its wheel). Another operator's claim is replaced.</summary>
    public void SetOperator(ImpDigger digger) => Operator = digger;

    /// <summary>Releases the claim, if it's this operator's.</summary>
    public void ClearOperator(ImpDigger digger)
    {
        if (Operator == digger) Operator = null;
    }

    /// <summary>The grid totals for this node: connected, supply, demand. Unconnected when there's no grid.</summary>
    public PowerGrid.GridTotals Totals => Grid != null ? Grid.TotalsFor(this) : new PowerGrid.GridTotals(false, 0, 0);

    public bool IsConnected => Totals.Connected;

    /// <summary>Connected and the grid covers its demand (PowerGrid.IsPowered). False with no grid.</summary>
    public bool IsPowered
    {
        get
        {
            PowerGrid.GridTotals totals = Totals;
            return PowerGrid.IsPowered(totals.Connected, totals.Supply, totals.Demand);
        }
    }

    /// <summary>
    /// The marker rule: a built consumer, connected to a grid, and not powered. An unconnected consumer isn't on any grid
    /// (it's the pre-#142 trap and works as it always did), so it doesn't ask for power.
    /// </summary>
    public static bool ShowsUnpoweredMarker(bool built, PowerRole role, bool connected, bool powered) =>
        built && role == PowerRole.Consumer && connected && !powered;

    /// <summary>Whether this node's marker shows right now.</summary>
    public bool ShowsMarker
    {
        get
        {
            if (!IsBuilt || Role != PowerRole.Consumer) return false;
            PowerGrid.GridTotals totals = Totals;
            return ShowsUnpoweredMarker(true, Role, totals.Connected, PowerGrid.IsPowered(totals.Connected, totals.Supply, totals.Demand));
        }
    }

    private void Update()
    {
        RefreshMarker();
    }

    /// <summary>Shows or hides the badge from ShowsMarker. Update calls it; public so tests can step it.</summary>
    public void RefreshMarker()
    {
        if (Marker != null) Marker.SetActive(ShowsMarker);
    }

    // A lightning bolt from three flat boxes, standing upright and facing the camera (−Z), above the placeable.
    private GameObject CreateMarker()
    {
        float h = markerSize;
        var parts = new[]
        {
            new CreatureModel.Part("BoltTop", CreatureModel.Shape.Box, new Vector3(0.06f * h, 0.26f * h, 0f),
                new Vector3(0.16f * h, 0.55f * h, 0.04f), markerColor, new Vector3(0f, 0f, -25f)),
            new CreatureModel.Part("BoltMid", CreatureModel.Shape.Box, new Vector3(0f, 0f, 0f),
                new Vector3(0.42f * h, 0.12f * h, 0.04f), markerColor),
            new CreatureModel.Part("BoltBottom", CreatureModel.Shape.Box, new Vector3(-0.06f * h, -0.26f * h, 0f),
                new Vector3(0.16f * h, 0.55f * h, 0.04f), markerColor, new Vector3(0f, 0f, -25f)),
        };
        GameObject marker = CreatureModel.Build(transform, "UnpoweredMarker", parts, markerSmoothness);
        marker.transform.localPosition = new Vector3(0f, markerHeight, 0f);
        marker.SetActive(false);
        return marker;
    }
}
