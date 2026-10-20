using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of placed items, one per tile. Placement is instant and free (DESIGN §A.4):
/// a target must be Floor, unoccupied, and reachable by the imp. TileState is never changed.
/// Placing creates a construction site (#91) that occupies its tile at once but does nothing until built;
/// consumers read the built-only queries (GetBuilt…, CountBuilt, TotalFood).
/// Orders cost materials (#104): each site records its cost row (#109: the type in the material the player chose,
/// TryGetCost) and waits unfunded until the funding pass pays for it from the Stockpile, deducted at funding. Each
/// material has its own line (#120): strictly in placement order within a material (a later, cheaper order never jumps
/// an earlier one of the same material), while a waiting order never holds up orders in other materials. The pass runs at placement and every frame while anything waits, so an affordable order funds at once
/// and a waiting one funds the frame the stockpile covers it. Only funded sites are build jobs. With no Stockpile
/// wired, every order funds at once (an unwired system degrades, never blocks). Placing is never blocked by cost.
/// Power (#142): the power table gives each type a role (none, producer, consumer), its watts and per-type params; a type
/// with a role gets a PowerNode at placement, which joins the PowerGrid once built.
/// #143: a power row with a fuel id makes its type a fueled producer (a FurnaceGenerator burning that fuel); the Grow-Lamp
/// speeds up plots near it while powered. This is also the fuel store: "mushrooms" are the food stored in built plots (the
/// top bar's Food), and a fuel id naming a material is that material's Stockpile count — global stock, never hauled.
/// </summary>
[DisallowMultipleComponent]
public class PlacementManager : MonoBehaviour, IFuelStore
{
    /// <summary>The fuel id for food (#143): the mushrooms stored in built plots.</summary>
    public const string FoodFuelId = "mushrooms";

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Stockpile stockpile; // Pays for orders (#104); null = orders fund at once
    [SerializeField] private PowerGrid powerGrid; // #142: where built power nodes register; null = nodes never connect
    [SerializeField] private ManaFlow manaFlow;   // #145: where built mana stores register; null = they never join a network

    [Header("Placeable Visuals (Placeholder Art)")]
    [SerializeField] private Color lairCotColor = new Color(0.40f, 0.60f, 0.90f, 1f);      // Blue
    [SerializeField] private Color mushroomPlotColor = new Color(0.35f, 0.75f, 0.35f, 1f); // Green
    [SerializeField] private Color spikeTrapColor = new Color(0.55f, 0.55f, 0.60f, 1f);    // Steel gray
    [SerializeField] private Color impWheelColor = new Color(0.62f, 0.44f, 0.26f, 1f);     // #142: worn wood
    [SerializeField] private Color furnaceColor = new Color(0.40f, 0.36f, 0.34f, 1f);      // #143: sooty stone body
    [SerializeField] private Color growLampColor = new Color(1.00f, 0.86f, 0.45f, 1f);     // #143: warm lamp glass
    [SerializeField] private Color accumulatorColor = new Color(0.66f, 0.48f, 1.00f, 1f);  // #145: the mana crystal
    [SerializeField] private Color manaContainerColor = new Color(0.34f, 0.30f, 0.44f, 1f); // #145: the tank's dark body
    [SerializeField] private float placeableSize = 0.8f;                                    // Fraction of a tile

    // Placeables are runtime-created (no Inspector presence), so their behavior config lives here.
    [Header("Mushroom Plot")]
    [SerializeField] private float mushroomGrowthSecondsPerFood = 30f;
    [SerializeField] private int mushroomCapacity = 5;

    [Header("Spike Trap")]
    [SerializeField] private int spikeDamage = 1; // Provisional: hero HP lands with Epic #5

    [Header("Furnace Generator (#143)")]
    [SerializeField] private float furnaceRetrySeconds = 1f; // Out of fuel: how often it tries to buy the next unit

    [Header("Accumulator (#145)")]
    [SerializeField] private int accumulatorCapacity = 20;
    [SerializeField] private float accumulatorCollectSeconds = 1f; // One collection cycle
    [SerializeField] private int accumulatorCollectPerCycle = 1;   // Units taken from beside it per cycle
    [SerializeField] private int accumulatorPullRadius = 4;        // Its suction reaches this many tiles (Manhattan), no farther
    [SerializeField] private float accumulatorSuction = 1f;        // The pull at the accumulator (density runs 0–1), fading out by the radius

    [Header("Mana Container (#145)")]
    [SerializeField] private int manaContainerCapacity = 100;

    [Header("Grow-Lamp (#143)")]
    [SerializeField] private int lampRadius = 2;              // Chebyshev: plots within this many tiles, diagonals in
    [SerializeField] private float lampGrowthMultiplier = 2f; // Covered plots grow this much faster while it's lit

    // How long the imp works at a construction site to finish it (#92), in game seconds.
    [Header("Build Times")]
    [SerializeField] private float lairCotBuildSeconds = 4f;
    [SerializeField] private float mushroomPlotBuildSeconds = 6f;
    [SerializeField] private float spikeTrapBuildSeconds = 5f;
    [SerializeField] private float impWheelBuildSeconds = 6f; // #142
    [SerializeField] private float furnaceBuildSeconds = 8f;  // #143
    [SerializeField] private float growLampBuildSeconds = 5f; // #143
    [SerializeField] private float accumulatorBuildSeconds = 6f;   // #145
    [SerializeField] private float manaContainerBuildSeconds = 6f; // #145

    // What an order costs (#104), per type AND material (#109): a type is orderable in a material exactly when a row
    // exists. Ships with every type in Stone, Glimmerstone (#114) and Deepstone (#111); later materials add rows, not fields.
    [Header("Build Costs")]
    [SerializeField] private List<PlaceableCost> costs = new List<PlaceableCost>
    {
        new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 3),
        new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Stone, 4),
        new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Stone, 2),
        new PlaceableCost(PlaceableType.LairCot, MaterialType.Glimmerstone, 3),      // #114: at Stone's amounts
        new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Glimmerstone, 4),
        new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, 2),
        new PlaceableCost(PlaceableType.LairCot, MaterialType.Deepstone, 3),         // #111: at Stone's amounts
        new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Deepstone, 4),
        new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Deepstone, 2),
        new PlaceableCost(PlaceableType.ImpWheel, MaterialType.Stone, 4),            // #142
        new PlaceableCost(PlaceableType.ImpWheel, MaterialType.Glimmerstone, 4),
        new PlaceableCost(PlaceableType.ImpWheel, MaterialType.Deepstone, 4),
        new PlaceableCost(PlaceableType.FurnaceGenerator, MaterialType.Stone, 6),    // #143
        new PlaceableCost(PlaceableType.FurnaceGenerator, MaterialType.Glimmerstone, 6),
        new PlaceableCost(PlaceableType.FurnaceGenerator, MaterialType.Deepstone, 6),
        new PlaceableCost(PlaceableType.GrowLamp, MaterialType.Stone, 4),            // #143
        new PlaceableCost(PlaceableType.GrowLamp, MaterialType.Glimmerstone, 4),
        new PlaceableCost(PlaceableType.GrowLamp, MaterialType.Deepstone, 4),
        new PlaceableCost(PlaceableType.Accumulator, MaterialType.Stone, 5),          // #145
        new PlaceableCost(PlaceableType.Accumulator, MaterialType.Glimmerstone, 5),
        new PlaceableCost(PlaceableType.Accumulator, MaterialType.Deepstone, 5),
        new PlaceableCost(PlaceableType.ManaContainer, MaterialType.Stone, 6),        // #145
        new PlaceableCost(PlaceableType.ManaContainer, MaterialType.Glimmerstone, 6),
        new PlaceableCost(PlaceableType.ManaContainer, MaterialType.Deepstone, 6),
    };

    // What each type does on a power grid (#142), the same row-table pattern as the costs: a type with no row has no role.
    [Header("Power")]
    [SerializeField] private List<PlaceablePower> power = new List<PlaceablePower>
    {
        new PlaceablePower(PlaceableType.ImpWheel, PowerRole.Producer, 40, needsOperator: true),
        new PlaceablePower(PlaceableType.SpikeTrap, PowerRole.Consumer, 10, poweredRearmSeconds: 4f),
        new PlaceablePower(PlaceableType.FurnaceGenerator, PowerRole.Producer, 100, fuelId: FoodFuelId, fuelSecondsPerUnit: 12f), // #143
        new PlaceablePower(PlaceableType.GrowLamp, PowerRole.Consumer, 20),                                                       // #143
    };

    // What a material makes a building DO (#110), per type AND material: numeric modifiers plus capability flags.
    // A missing row is never an error — consumers read it as all-defaults. Rows ship only where an axis is real
    // (cots rest); later materials and axes add rows, not fields per combination.
    [Header("Material Properties")]
    [SerializeField] private List<PlaceableProperty> properties = new List<PlaceableProperty>
    {
        new PlaceableProperty(PlaceableType.LairCot, MaterialType.Stone, 1f, 1f, false),          // The explicit baseline
        new PlaceableProperty(PlaceableType.LairCot, MaterialType.Glimmerstone, 1.25f, 1f, true), // Faint magic: the rest material
        new PlaceableProperty(PlaceableType.LairCot, MaterialType.Deepstone, 1.5f, 1f, false),    // #111: the best rest in the game
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
    public PowerGrid PowerGrid { get => powerGrid; set => powerGrid = value; }
    public ManaFlow ManaFlow { get => manaFlow; set => manaFlow = value; }
    public int AccumulatorCapacity { get => accumulatorCapacity; set => accumulatorCapacity = value; }
    public float AccumulatorCollectSeconds { get => accumulatorCollectSeconds; set => accumulatorCollectSeconds = value; }
    public int AccumulatorCollectPerCycle { get => accumulatorCollectPerCycle; set => accumulatorCollectPerCycle = value; }
    public int ManaContainerCapacity { get => manaContainerCapacity; set => manaContainerCapacity = value; }
    public int AccumulatorPullRadius { get => accumulatorPullRadius; set => accumulatorPullRadius = value; }
    public float AccumulatorSuction { get => accumulatorSuction; set => accumulatorSuction = value; }
    public float AccumulatorBuildSeconds { get => accumulatorBuildSeconds; set => accumulatorBuildSeconds = value; }
    public float ManaContainerBuildSeconds { get => manaContainerBuildSeconds; set => manaContainerBuildSeconds = value; }
    public float ImpWheelBuildSeconds { get => impWheelBuildSeconds; set => impWheelBuildSeconds = value; }
    public float FurnaceBuildSeconds { get => furnaceBuildSeconds; set => furnaceBuildSeconds = value; }
    public float GrowLampBuildSeconds { get => growLampBuildSeconds; set => growLampBuildSeconds = value; }
    public float FurnaceRetrySeconds { get => furnaceRetrySeconds; set => furnaceRetrySeconds = value; }
    public int LampRadius { get => lampRadius; set => lampRadius = value; }
    public float LampGrowthMultiplier { get => lampGrowthMultiplier; set => lampGrowthMultiplier = value; }
    /// <summary>The power rows, in table order (replaceable in code, e.g. by tests).</summary>
    public List<PlaceablePower> Power { get => power; set => power = value ?? new List<PlaceablePower>(); }
    /// <summary>The cost rows, in table order (replaceable in code, e.g. by tests).</summary>
    public List<PlaceableCost> Costs { get => costs; set => costs = value ?? new List<PlaceableCost>(); }
    /// <summary>The material-property rows, in table order (replaceable in code, e.g. by tests).</summary>
    public List<PlaceableProperty> Properties { get => properties; set => properties = value ?? new List<PlaceableProperty>(); }

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

    /// <summary>
    /// What this type does when made of this material (#110): the row's modifiers and capability flags. False + null
    /// when no row exists — never an error: consumers treat a miss as all-defaults (e.g. rest rate 1.0). Never throws.
    /// </summary>
    public bool TryGetProperties(PlaceableType type, MaterialType material, out PlaceableProperty property)
    {
        if (properties != null)
        {
            foreach (PlaceableProperty row in properties)
            {
                if (row != null && row.type == type && row.material == material)
                {
                    property = row;
                    return true;
                }
            }
        }
        property = null;
        return false;
    }

    /// <summary>A type's power row (#142): its role, watts and params. False + null with no row (no role). Never throws.</summary>
    public bool TryGetPower(PlaceableType type, out PlaceablePower row)
    {
        if (power != null)
        {
            foreach (PlaceablePower candidate in power)
            {
                if (candidate != null && candidate.type == type)
                {
                    row = candidate;
                    return true;
                }
            }
        }
        row = null;
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
    /// at once), or nothing already waiting for THAT material (#120: a new order queues behind its own material's
    /// line only) and the stock of that material covers the cost. False for an unorderable pair. The build rail's
    /// tiles and the detail card's chips show the cost red when this is false; placing is never blocked by it.
    /// </summary>
    public bool WouldFundNow(PlaceableType type, MaterialType material)
    {
        if (!TryGetCost(type, material, out MaterialAmount cost)) return false;
        if (stockpile == null) return true; // Unity null check: unwired means free
        return !AnyAwaiting(cost.type) && stockpile.Count(cost.type) >= cost.amount;
    }

    // True while an order in this material waits for funding: its line is non-empty.
    private bool AnyAwaiting(MaterialType material)
    {
        foreach (Placeable p in awaitingFunding)
            if (p != null && !p.IsBuilt && !p.IsFunded && p.OrderedMaterial == material) return true;
        return false;
    }

    /// <summary>
    /// How many orders wait in this material's funding line (#126): placed, not yet funded. Funded, built and other
    /// materials' orders don't count.
    /// </summary>
    public int WaitingOrders(MaterialType material)
    {
        int n = 0;
        foreach (Placeable p in awaitingFunding)
            if (p != null && !p.IsBuilt && !p.IsFunded && p.OrderedMaterial == material) n++;
        return n;
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
            case PlaceableType.ImpWheel: return impWheelBuildSeconds;
            case PlaceableType.FurnaceGenerator: return furnaceBuildSeconds;
            case PlaceableType.GrowLamp: return growLampBuildSeconds;
            case PlaceableType.Accumulator: return accumulatorBuildSeconds;
            case PlaceableType.ManaContainer: return manaContainerBuildSeconds;
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

    // ---- the fuel store (#143) ----

    /// <summary>
    /// Units of a fuel on hand: "mushrooms" is the food in built plots (TotalFood); a material's name is its Stockpile
    /// count. 0 for an unknown id, or a material with no stockpile wired.
    /// </summary>
    public int FuelCount(string fuelId)
    {
        if (fuelId == FoodFuelId) return TotalFood;
        if (TryParseMaterialFuel(fuelId, out MaterialType material)) return stockpile != null ? stockpile.Count(material) : 0;
        return 0;
    }

    /// <summary>
    /// Spends one unit of a fuel. Mushrooms come from the fullest built plot (ties in board scan order), taken the way a
    /// goblin eats; a material comes out of the Stockpile. False when there's none.
    /// </summary>
    public bool TrySpendFuel(string fuelId)
    {
        if (fuelId == FoodFuelId)
        {
            MushroomPlot fullest = null;
            foreach (MushroomPlot plot in GetBuiltMushroomPlots())
                if (plot.FoodCount > 0 && (fullest == null || plot.FoodCount > fullest.FoodCount)) fullest = plot;
            return fullest != null && fullest.TryTakeFood();
        }
        return TryParseMaterialFuel(fuelId, out MaterialType material) && stockpile != null && stockpile.TrySpend(material, 1);
    }

    /// <summary>How a fuel reads on the furnace's card: "mushrooms", or a material's lower-case name.</summary>
    public static string FuelDisplayName(string fuelId) => string.IsNullOrEmpty(fuelId) ? "fuel" : fuelId.ToLowerInvariant();

    private static bool TryParseMaterialFuel(string fuelId, out MaterialType material) =>
        Enum.TryParse(fuelId, true, out material) && Enum.IsDefined(typeof(MaterialType), material);

    // ---- grow-lamp coverage (#143) ----

    /// <summary>Built Grow-Lamps, in scan order.</summary>
    public List<GrowLamp> GetBuiltLamps() => Collect<GrowLamp>(PlaceableType.GrowLamp, builtOnly: true);

    /// <summary>Built Furnace Generators, in scan order.</summary>
    public List<FurnaceGenerator> GetBuiltFurnaces() => Collect<FurnaceGenerator>(PlaceableType.FurnaceGenerator, builtOnly: true);

    /// <summary>
    /// The growth multiplier at a tile: the best single multiplier of the lit lamps within lampRadius (GrowLamp's rule: no
    /// stacking), else 1. MushroomPlot reads this each growth tick.
    /// </summary>
    public float GrowthMultiplierAt(Vector2Int tile)
    {
        var lamps = new List<GrowLamp.LampState>();
        foreach (GrowLamp lamp in GetBuiltLamps()) lamps.Add(lamp.State);
        return GrowLamp.MultiplierAt(lamps, tile, lampRadius, lampGrowthMultiplier);
    }

    /// <summary>How many plots (sites included) stand within lampRadius of a tile: the lamp card's "Covers n plots".</summary>
    public int PlotsCoveredFrom(Vector2Int lampTile)
    {
        var tiles = new List<Vector2Int>();
        foreach (MushroomPlot plot in GetMushroomPlots()) tiles.Add(plot.Tile);
        return GrowLamp.CountCovered(lampTile, tiles, lampRadius);
    }

    private readonly Dictionary<Vector2Int, Placeable> placed = new Dictionary<Vector2Int, Placeable>();
    private readonly List<Placeable> awaitingFunding = new List<Placeable>(); // Placement order; each material's orders are its line (#120)
    private readonly HashSet<MaterialType> blockedMaterials = new HashSet<MaterialType>(); // Scratch for the funding pass
    private long nextOrderIndex;
    private Transform holder;

    private void Update()
    {
        if (awaitingFunding.Count > 0) RunFundingPass(); // Every frame while anything waits: funds within a frame of affording
    }

    // #144: follow the board's view mode. Subscribed in OnEnable (a mid-Play reload re-runs OnEnable, not Awake), and
    // unsubscribed first so a repeat never doubles the handlers.
    private void OnEnable()
    {
        boardRenderer ??= GetComponent<BoardRenderer>() ?? FindAnyObjectByType<BoardRenderer>();
        if (boardRenderer == null) return;
        boardRenderer.ViewModeChanged -= HandleViewModeChanged;
        boardRenderer.ViewModeChanged += HandleViewModeChanged;
        boardRenderer.OverlayRepainted -= RefreshSeeThrough;
        boardRenderer.OverlayRepainted += RefreshSeeThrough;
    }

    private void OnDisable()
    {
        if (boardRenderer == null) return;
        boardRenderer.ViewModeChanged -= HandleViewModeChanged;
        boardRenderer.OverlayRepainted -= RefreshSeeThrough;
    }

    private void HandleViewModeChanged(ViewMode mode) => RefreshSeeThrough();

    /// <summary>
    /// Applies the view mode's model look to every placeable (#144): in the power overlay non-power placeables go
    /// see-through (PowerOverlay.SeesThrough) so conduit under them shows; otherwise every model is restored. Runs on a
    /// mode change, on each overlay repaint (catching parts created since, like a funded order's pile) and on placement.
    /// </summary>
    public void RefreshSeeThrough()
    {
        ViewMode mode = boardRenderer != null ? boardRenderer.Mode : ViewMode.Normal;
        float alpha = boardRenderer != null ? boardRenderer.OverlayPropAlpha : 1f;
        foreach (Placeable p in placed.Values)
        {
            if (p == null) continue;
            p.SetSeeThrough(PowerOverlay.SeesThrough(mode, p.TryGetComponent(out PowerNode _), p.TryGetComponent(out IManaPort _)), alpha);
        }
    }

    /// <summary>
    /// Funds waiting orders, one line per material (#120): walking the orders in placement order, each is paid in full
    /// via Stockpile.TrySpend (all or nothing) and released to the build queue, until its material's stock can't cover
    /// one — that order then blocks every later order in the SAME material (nothing behind it jumps the line), and
    /// only those: other materials' lines carry on. Built or removed orders leave the line unpaid. Returns how many it
    /// funded. Runs at placement and from Update while anything waits; public so tests can drive it.
    /// </summary>
    public int RunFundingPass()
    {
        int funded = 0;
        blockedMaterials.Clear();
        for (int i = 0; i < awaitingFunding.Count;)
        {
            Placeable order = awaitingFunding[i];
            if (order == null || order.IsBuilt || order.IsFunded) // Gone, staged complete, or already funded: no charge
            {
                awaitingFunding.RemoveAt(i);
                continue;
            }
            if (blockedMaterials.Contains(order.OrderedMaterial)) // Behind its material's waiting head
            {
                i++;
                continue;
            }
            if (stockpile != null && !stockpile.TrySpend(order.OrderedMaterial, order.RequiredAmount))
            {
                blockedMaterials.Add(order.OrderedMaterial); // Head-of-line wait, for this material only
                i++;
                continue;
            }
            awaitingFunding.RemoveAt(i);
            order.MarkFunded();
            funded++;
        }
        return funded;
    }

    private void PruneAwaiting() => awaitingFunding.RemoveAll(p => p == null || p.IsBuilt || p.IsFunded);

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= GetComponent<BoardRenderer>() ?? FindAnyObjectByType<BoardRenderer>();
        powerGrid ??= GetComponent<PowerGrid>();
        manaFlow ??= GetComponent<ManaFlow>();
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

    /// <summary>All placed Accumulators (#145), sites included, in scan order.</summary>
    public List<Accumulator> GetAccumulators() => Collect<Accumulator>(PlaceableType.Accumulator, builtOnly: false);

    /// <summary>All placed Mana Containers (#145), sites included, in scan order.</summary>
    public List<ManaContainer> GetManaContainers() => Collect<ManaContainer>(PlaceableType.ManaContainer, builtOnly: false);

    /// <summary>Built Imp Wheels' power nodes (#142), in scan order: the wheels an imp can operate.</summary>
    public List<PowerNode> GetBuiltWheels() => Collect<PowerNode>(PlaceableType.ImpWheel, builtOnly: true);

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
            go.AddComponent<MushroomPlot>().Initialize(mushroomGrowthSecondsPerFood, mushroomCapacity, this);
        else if (type == PlaceableType.SpikeTrap)
            go.AddComponent<SpikeTrap>().Initialize(spikeDamage);
        else if (type == PlaceableType.GrowLamp)
            go.AddComponent<GrowLamp>();
        else if (type == PlaceableType.Accumulator) // #145
            go.AddComponent<Accumulator>().Initialize(manaFlow, accumulatorCapacity, accumulatorCollectSeconds, accumulatorCollectPerCycle,
                accumulatorPullRadius, accumulatorSuction);
        else if (type == PlaceableType.ManaContainer)
            go.AddComponent<ManaContainer>().Initialize(manaFlow, manaContainerCapacity);
        if (TryGetPower(type, out PlaceablePower powerRow) && powerRow.role != PowerRole.None)
        {
            var node = go.AddComponent<PowerNode>();
            node.Initialize(powerGrid, powerRow.role, powerRow.watts, powerRow.needsOperator);
            if (go.TryGetComponent(out SpikeTrap trap)) trap.PoweredRearmSeconds = powerRow.poweredRearmSeconds;
            if (powerRow.role == PowerRole.Producer && !string.IsNullOrEmpty(powerRow.fuelId)) // #143: a fueled producer
            {
                var furnace = go.AddComponent<FurnaceGenerator>();
                furnace.Initialize(this, powerRow.fuelId, powerRow.fuelSecondsPerUnit, furnaceRetrySeconds);
                node.SetSupplyGate(furnace);
            }
        }
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
        if (boardRenderer != null && boardRenderer.Mode != ViewMode.Normal) RefreshSeeThrough(); // Placed mid-overlay
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
            case PlaceableType.ImpWheel: return impWheelColor;
            case PlaceableType.FurnaceGenerator: return furnaceColor;
            case PlaceableType.GrowLamp: return growLampColor;
            case PlaceableType.Accumulator: return accumulatorColor;
            case PlaceableType.ManaContainer: return manaContainerColor;
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

/// <summary>
/// One material-property row (#110): what this type does when made of this material — numeric modifiers plus
/// capability flags. Read through PlacementManager.TryGetProperties; a missing row means all-defaults.
/// </summary>
[System.Serializable]
public class PlaceableProperty
{
    public PlaceableType type;
    public MaterialType material;
    public float restRateMultiplier = 1f; // Energy restored per second while sleeping, × this (GoblinAI's sleep)
    public float restQuality = 1f;        // Data only for now: no consumer yet
    public bool restsMagicalCreatures;    // Capability flag, data only: no magical creatures exist yet

    public PlaceableProperty(PlaceableType type, MaterialType material, float restRateMultiplier = 1f, float restQuality = 1f,
        bool restsMagicalCreatures = false)
    {
        this.type = type;
        this.material = material;
        this.restRateMultiplier = restRateMultiplier;
        this.restQuality = restQuality;
        this.restsMagicalCreatures = restsMagicalCreatures;
    }
}

/// <summary>
/// One power row (#142): what a type does on a power grid — its role and watts (output for a producer, draw for a
/// consumer) — plus per-type params: needsOperator (a producer that runs only while an imp tends it, the Imp Wheel) and
/// poweredRearmSeconds (the Spike Trap's self-rearm time on a powered grid). #143: fuelId and fuelSecondsPerUnit make a
/// producer fueled — it burns one unit of that fuel (an IFuelStore key: "mushrooms", or a material's name) per interval
/// and supplies only while burning. An empty fuelId is an unfueled type. A new fuel is a new row, not new code.
/// </summary>
[System.Serializable]
public class PlaceablePower
{
    public PlaceableType type;
    public PowerRole role;
    public int watts;
    public bool needsOperator;
    public float poweredRearmSeconds;
    public string fuelId = "";
    public float fuelSecondsPerUnit;

    public PlaceablePower(PlaceableType type, PowerRole role, int watts, bool needsOperator = false, float poweredRearmSeconds = 0f,
        string fuelId = "", float fuelSecondsPerUnit = 0f)
    {
        this.type = type;
        this.role = role;
        this.watts = watts;
        this.needsOperator = needsOperator;
        this.poweredRearmSeconds = poweredRearmSeconds;
        this.fuelId = fuelId ?? "";
        this.fuelSecondsPerUnit = fuelSecondsPerUnit;
    }
}
