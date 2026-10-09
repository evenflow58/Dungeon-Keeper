using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of placed items, one per tile. Placement is instant and free (DESIGN §A.4):
/// a target must be Floor, unoccupied, and reachable by the imp. TileState is never changed.
/// Placing creates a construction site (#91) that occupies its tile at once but does nothing until built;
/// consumers read the built-only queries (GetBuilt…, CountBuilt, TotalFood).
/// Orders cost materials (#104): each site records its cost row (#109: the type in the material the player chose,
/// TryGetCost) and waits unfunded until the funding pass pays for it from the Stockpile — strictly in placement order (a later, cheaper order never jumps an earlier one), deducted
/// at funding. The pass runs at placement and every frame while anything waits, so an affordable order funds at once
/// and a waiting one funds the frame the stockpile covers it. Only funded sites are build jobs. With no Stockpile
/// wired, every order funds at once (an unwired system degrades, never blocks). Placing is never blocked by cost.
/// </summary>
[DisallowMultipleComponent]
public class PlacementManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Stockpile stockpile; // Pays for orders (#104); null = orders fund at once

    [Header("Placeable Visuals (Placeholder Art)")]
    [SerializeField] private Color lairCotColor = new Color(0.40f, 0.60f, 0.90f, 1f);      // Blue
    [SerializeField] private Color mushroomPlotColor = new Color(0.35f, 0.75f, 0.35f, 1f); // Green
    [SerializeField] private Color spikeTrapColor = new Color(0.55f, 0.55f, 0.60f, 1f);    // Steel gray
    [SerializeField] private float placeableSize = 0.8f;                                    // Fraction of a tile

    // Placeables are runtime-created (no Inspector presence), so their behavior config lives here.
    [Header("Mushroom Plot")]
    [SerializeField] private float mushroomGrowthSecondsPerFood = 30f;
    [SerializeField] private int mushroomCapacity = 5;

    [Header("Spike Trap")]
    [SerializeField] private int spikeDamage = 1; // Provisional: hero HP lands with Epic #5

    // How long the imp works at a construction site to finish it (#92), in game seconds.
    [Header("Build Times")]
    [SerializeField] private float lairCotBuildSeconds = 4f;
    [SerializeField] private float mushroomPlotBuildSeconds = 6f;
    [SerializeField] private float spikeTrapBuildSeconds = 5f;

    // What an order costs (#104), per type AND material (#109): a type is orderable in a material exactly when a row
    // exists. Ships with every type in Stone; later materials add rows, not fields.
    [Header("Build Costs")]
    [SerializeField] private List<PlaceableCost> costs = new List<PlaceableCost>
    {
        new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 3),
        new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Stone, 4),
        new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Stone, 2),
    };

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public float MushroomGrowthSecondsPerFood { get => mushroomGrowthSecondsPerFood; set => mushroomGrowthSecondsPerFood = value; }
    public int MushroomCapacity { get => mushroomCapacity; set => mushroomCapacity = value; }
    public int SpikeDamage { get => spikeDamage; set => spikeDamage = value; }
    public float LairCotBuildSeconds { get => lairCotBuildSeconds; set => lairCotBuildSeconds = value; }
    public float MushroomPlotBuildSeconds { get => mushroomPlotBuildSeconds; set => mushroomPlotBuildSeconds = value; }
    public float SpikeTrapBuildSeconds { get => spikeTrapBuildSeconds; set => spikeTrapBuildSeconds = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    /// <summary>The cost rows, in table order (replaceable in code, e.g. by tests).</summary>
    public List<PlaceableCost> Costs { get => costs; set => costs = value ?? new List<PlaceableCost>(); }

    /// <summary>
    /// What an order of this type in this material costs (#109): the row's (material, amount). False when no row
    /// exists — the type isn't orderable in that material. Never throws.
    /// </summary>
    public bool TryGetCost(PlaceableType type, MaterialType material, out MaterialAmount cost)
    {
        if (costs != null)
        {
            foreach (PlaceableCost row in costs)
            {
                if (row != null && row.type == type && row.material == material)
                {
                    cost = new MaterialAmount(row.material, row.amount);
                    return true;
                }
            }
        }
        cost = null;
        return false;
    }

    /// <summary>The materials a type can be ordered in: its rows' materials, in table order, without repeats.</summary>
    public List<MaterialType> MaterialsFor(PlaceableType type)
    {
        var materials = new List<MaterialType>();
        if (costs == null) return materials;
        foreach (PlaceableCost row in costs)
            if (row != null && row.type == type && !materials.Contains(row.material)) materials.Add(row.material);
        return materials;
    }

    /// <summary>A type's default material: its first row's (Stone today). False when the type has no rows at all.</summary>
    public bool TryGetDefaultMaterial(PlaceableType type, out MaterialType material)
    {
        List<MaterialType> materials = MaterialsFor(type);
        material = materials.Count > 0 ? materials[0] : default;
        return materials.Count > 0;
    }

    /// <summary>
    /// True when an order of this type in this material placed now would fund on the spot: no stockpile (orders fund
    /// at once), or nothing already waiting (strict global FIFO across materials: a new order queues behind them) and
    /// the stock of THAT material covers the cost. False for an unorderable pair. The build bar shows the cost red when
    /// this is false; placing is never blocked by it.
    /// </summary>
    public bool WouldFundNow(PlaceableType type, MaterialType material)
    {
        if (!TryGetCost(type, material, out MaterialAmount cost)) return false;
        if (stockpile == null) return true; // Unity null check: unwired means free
        return awaitingFunding.Count == 0 && stockpile.Count(cost.type) >= cost.amount;
    }

    /// <summary>Orders waiting for materials, in placement (funding) order.</summary>
    public List<Placeable> GetAwaitingFunding()
    {
        PruneAwaiting();
        return new List<Placeable>(awaitingFunding);
    }

    /// <summary>Game seconds of imp work to finish a construction site of this type.</summary>
    public float BuildSecondsFor(PlaceableType type)
    {
        switch (type)
        {
            case PlaceableType.LairCot: return lairCotBuildSeconds;
            case PlaceableType.MushroomPlot: return mushroomPlotBuildSeconds;
            case PlaceableType.SpikeTrap: return spikeTrapBuildSeconds;
            default: return 0f;
        }
    }

    public int Count => placed.Count;

    /// <summary>Food stored across all built Mushroom Plots (the top bar's source). A site holds none.</summary>
    public int TotalFood
    {
        get
        {
            int total = 0;
            foreach (Placeable p in placed.Values)
            {
                if (p.Type != PlaceableType.MushroomPlot || !p.IsBuilt) continue;
                // TryGetComponent rather than ?. on a UnityEngine.Object (?. skips Unity's null check, which
                // matters for destroyed objects). A plot without the component contributes 0.
                if (p.TryGetComponent(out MushroomPlot plot)) total += plot.FoodCount;
            }
            return total;
        }
    }

    private readonly Dictionary<Vector2Int, Placeable> placed = new Dictionary<Vector2Int, Placeable>();
    private readonly List<Placeable> awaitingFunding = new List<Placeable>(); // Placement order: the FIFO line
    private long nextOrderIndex;
    private Transform holder;

    private void Update()
    {
        if (awaitingFunding.Count > 0) RunFundingPass(); // Every frame while anything waits: funds within a frame of affording
    }

    /// <summary>
    /// Funds waiting orders strictly in placement order: the head order is paid in full via Stockpile.TrySpend (all or
    /// nothing) and released to the build queue; the pass stops at the first order the stock can't cover, so nothing
    /// behind it jumps the line. Built or removed orders leave the line unpaid. Returns how many it funded.
    /// Runs at placement and from Update while anything waits; public so tests can drive it.
    /// </summary>
    public int RunFundingPass()
    {
        int funded = 0;
        while (awaitingFunding.Count > 0)
        {
            Placeable head = awaitingFunding[0];
            if (head == null || head.IsBuilt || head.IsFunded) // Gone, staged complete, or already funded: no charge
            {
                awaitingFunding.RemoveAt(0);
                continue;
            }
            if (stockpile != null && !stockpile.TrySpend(head.OrderedMaterial, head.RequiredAmount)) break; // Head-of-line wait
            awaitingFunding.RemoveAt(0);
            head.MarkFunded();
            funded++;
        }
        return funded;
    }

    private void PruneAwaiting() => awaitingFunding.RemoveAll(p => p == null || p.IsBuilt || p.IsFunded);

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= GetComponent<BoardRenderer>() ?? FindAnyObjectByType<BoardRenderer>();
    }

    public bool IsOccupied(Vector2Int tile) => placed.ContainsKey(tile);

    public Placeable GetAt(Vector2Int tile) => placed.TryGetValue(tile, out Placeable p) ? p : null;

    /// <summary>Placeables of a type, construction sites included.</summary>
    public int CountOfType(PlaceableType type) => CountMatching(type, builtOnly: false);

    /// <summary>Built placeables of a type: what the game consumes (sites don't count, #91).</summary>
    public int CountBuilt(PlaceableType type) => CountMatching(type, builtOnly: true);

    private int CountMatching(PlaceableType type, bool builtOnly)
    {
        int n = 0;
        foreach (Placeable p in placed.Values)
            if (p.Type == type && (!builtOnly || p.IsBuilt)) n++;
        return n;
    }

    /// <summary>
    /// All placed Spike Traps (Armed, Spent, and construction sites), sorted by tile in board scan order
    /// (x ascending, then y ascending) so work selection is deterministic regardless of the registry's order.
    /// </summary>
    public List<SpikeTrap> GetSpikeTraps() => Collect<SpikeTrap>(PlaceableType.SpikeTrap, builtOnly: false);

    /// <summary>Built Spike Traps only (Armed and Spent), in scan order: the ones that exist as traps.</summary>
    public List<SpikeTrap> GetBuiltSpikeTraps() => Collect<SpikeTrap>(PlaceableType.SpikeTrap, builtOnly: true);

    /// <summary>All placed Mushroom Plots (stocked, empty, and sites), sorted by tile in board scan order.</summary>
    public List<MushroomPlot> GetMushroomPlots() => Collect<MushroomPlot>(PlaceableType.MushroomPlot, builtOnly: false);

    /// <summary>Built Mushroom Plots only, in scan order: the ones that grow and feed.</summary>
    public List<MushroomPlot> GetBuiltMushroomPlots() => Collect<MushroomPlot>(PlaceableType.MushroomPlot, builtOnly: true);

    /// <summary>All placed Lair Cots (claimed, free, and sites), sorted by tile in board scan order.</summary>
    public List<Placeable> GetCots() => Collect<Placeable>(PlaceableType.LairCot, builtOnly: false);

    /// <summary>Built Lair Cots only, in scan order: the ones a goblin can claim.</summary>
    public List<Placeable> GetBuiltCots() => Collect<Placeable>(PlaceableType.LairCot, builtOnly: true);

    /// <summary>Build jobs (#92): every FUNDED, unbuilt site, any type, in board scan order. Orders still awaiting
    /// materials are excluded (#104): they are not work until paid for.</summary>
    public List<Placeable> GetConstructionSites()
    {
        // Funded and unbuilt only (#104): an order awaiting materials is not yet a build job.
        var sites = new List<Placeable>();
        foreach (Placeable p in placed.Values)
            if (p != null && !p.IsBuilt && p.IsFunded) sites.Add(p);
        sites.Sort((a, b) => CompareScanOrder(a.Tile, b.Tile));
        return sites;
    }

    // The type's component on each matching placeable (a placeable missing it is skipped), in scan order.
    private List<T> Collect<T>(PlaceableType type, bool builtOnly) where T : Component
    {
        var found = new List<(Vector2Int tile, T item)>();
        foreach (Placeable p in placed.Values)
        {
            if (p.Type != type || (builtOnly && !p.IsBuilt)) continue;
            if (p.TryGetComponent(out T item)) found.Add((p.Tile, item));
        }
        found.Sort((a, b) => CompareScanOrder(a.tile, b.tile));
        var result = new List<T>(found.Count);
        foreach (var f in found) result.Add(f.item);
        return result;
    }

    // Board scan order: x ascending, then y ascending.
    private static int CompareScanOrder(Vector2Int a, Vector2Int b) =>
        a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

    /// <summary>
    /// Valid target: in-bounds Floor, unoccupied, and reachable from fromTile (the imp's current tile).
    /// </summary>
    public bool CanPlace(Vector2Int tile, Vector2Int fromTile)
    {
        if (dungeonBoard == null) return false;
        if (!dungeonBoard.IsWalkable(tile.x, tile.y) || IsOccupied(tile)) return false;
        return Pathfinder.FindPath(dungeonBoard, fromTile, tile).Count > 0;
    }

    /// <summary>Places an order of this type in its default material (its first cost row: Stone today).</summary>
    public bool TryPlace(PlaceableType type, Vector2Int tile, Vector2Int fromTile, out Placeable placeable)
    {
        placeable = null;
        return TryGetDefaultMaterial(type, out MaterialType material) && TryPlace(type, material, tile, fromTile, out placeable);
    }

    /// <summary>
    /// Places an order of this type in the chosen material (#109). False when the tile isn't placeable, or when the type
    /// isn't orderable in that material (no cost row) — cost itself never blocks: an unaffordable order waits.
    /// </summary>
    public bool TryPlace(PlaceableType type, MaterialType material, Vector2Int tile, Vector2Int fromTile, out Placeable placeable)
    {
        placeable = null;
        if (!TryGetCost(type, material, out MaterialAmount cost)) return false;
        if (!CanPlace(tile, fromTile)) return false;

        var go = new GameObject($"{type} ({tile.x}, {tile.y})");
        go.transform.SetParent(GetHolder(), false);
        go.transform.position = TileCenter(tile);

        placeable = go.AddComponent<Placeable>();
        placeable.Initialize(type, tile, ColorFor(type), placeableSize);
        if (type == PlaceableType.MushroomPlot)
            go.AddComponent<MushroomPlot>().Initialize(mushroomGrowthSecondsPerFood, mushroomCapacity);
        else if (type == PlaceableType.SpikeTrap)
            go.AddComponent<SpikeTrap>().Initialize(spikeDamage);
        placed[tile] = placeable;

        // The order (#104): stamp its place in line and cost, then fund it now if it can be (it waits otherwise).
        placeable.SetOrder(nextOrderIndex++, cost.type, cost.amount);
        placeable.TintOrderMarker(Placeable.MarkerTintFor(stockpile, cost.type, placeable.OrderMarkerFallbackTint)); // #109
        if (stockpile == null) placeable.MarkFunded(); // Unwired: no waiting, exactly as before #104
        else
        {
            awaitingFunding.Add(placeable);
            RunFundingPass();
        }
        return true;
    }

    /// <summary>
    /// Places on every valid tile in the rect (invalid tiles are skipped). Returns how many were placed.
    /// </summary>
    public int ApplyRect(PlaceableType type, RectInt rect, Vector2Int fromTile) =>
        TryGetDefaultMaterial(type, out MaterialType material) ? ApplyRect(type, material, rect, fromTile) : 0;

    /// <summary>Places orders in the chosen material on every valid tile in the rect (#109). Returns how many were placed.</summary>
    public int ApplyRect(PlaceableType type, MaterialType material, RectInt rect, Vector2Int fromTile)
    {
        int count = 0;
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                if (TryPlace(type, material, new Vector2Int(x, y), fromTile, out _)) count++;
            }
        }
        return count;
    }

    /// <summary>True if at least one tile in the rect is a valid target (drives the drag preview tint).</summary>
    public bool AnyPlaceable(RectInt rect, Vector2Int fromTile)
    {
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                if (CanPlace(new Vector2Int(x, y), fromTile)) return true;
            }
        }
        return false;
    }

    public Color ColorFor(PlaceableType type)
    {
        switch (type)
        {
            case PlaceableType.LairCot: return lairCotColor;
            case PlaceableType.MushroomPlot: return mushroomPlotColor;
            case PlaceableType.SpikeTrap: return spikeTrapColor;
            default: return Color.magenta;
        }
    }

    private Transform GetHolder()
    {
        if (holder == null)
        {
            holder = new GameObject("Placeables").transform;
            holder.SetParent(transform, false);
        }
        return holder;
    }

    private Vector3 TileCenter(Vector2Int tile)
    {
        if (boardRenderer != null) return boardRenderer.GetTileCenterWorldPosition(tile.x, tile.y);
        return BoardRenderer.UnanchoredTileCenter(tile);
    }
}

/// <summary>One build-cost row (#109): an order of this type in this material costs this much of it.</summary>
[System.Serializable]
public class PlaceableCost
{
    public PlaceableType type;
    public MaterialType material;
    public int amount;

    public PlaceableCost(PlaceableType type, MaterialType material, int amount)
    {
        this.type = type;
        this.material = material;
        this.amount = amount;
    }
}
