using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The detail card (#125): a bottom-left panel shown exactly while a buildable is armed. It names the armed type and
/// its category, describes it, lists its build time and the stats BuildPresentation declares for it, and carries
/// material selection: one chip per material the type can be ordered in, with that material's cost and the amount
/// owned. It is a view, like BuildRail: the selection lives in PlacementController (SelectedMaterial, SelectMaterial)
/// and every number comes from PlacementManager and the Stockpile. A chip's cost is red when an order in that material
/// would wait (WouldFundNow). Built in code like the rest of the chrome (own overlay Canvas, no scene-authored UI).
/// Material mode (#126): while nothing is armed and the STOCK panel has a material selected (ShowMaterial), the same
/// frame describes that material instead: name and family, the amount on hand, description and source, the property
/// highlights where its rows differ from the defaults, one read-only chip per type with a cost row in it, and how many
/// orders wait for it. Arming a type always takes the card back to buildable mode.
/// Placed mode (#142): in Neutral, a built placeable the controller has selected (a board click) shows here: its name and
/// category, its description, and lines for what it's doing — a cot claimed or free, a plot's food, a trap armed or
/// spent — plus, for a power node, its role, its power state and its grid's totals. Wire mode (#142): while Lay conduit
/// is armed the card describes the verb. One thing at a time: armed, then wire, then placed, then material.
/// Toggles (#143): in placed mode a power node's card carries an Enable/Disable button (the only new raycast target; the
/// card's own panel already keeps presses off the board). A click flips the node's Enabled at once. The furnace's card
/// adds its fuel lines, the Grow-Lamp's the plots it covers.
/// </summary>
[DisallowMultipleComponent]
public class DetailCard : MonoBehaviour
{
    public const string HintText = "Drag to place · Orders that can't be paid wait · Right-click cancels";
    public const string WireHintText = "Drag over explored floor to lay conduit · Right-drag removes · Esc exits";
    public const string PipeHintText = "Drag over explored floor to lay pipe · Right-drag removes · Esc exits"; // #145
    public const string PlacedHintText = "Esc or click empty floor to deselect";

    [Header("References")]
    [SerializeField] private PlacementController placementController;
    [SerializeField] private PlacementManager placementManager;

    [Header("Layout")]
    [SerializeField] private float cardWidth = 350f;
    [SerializeField] private float leftOffset = 62f;         // Clear of the build rail (BuildRail.railWidth 54) plus a gap
    [SerializeField] private float bottomOffset = 8f;
    [SerializeField] private float padding = 8f;             // Inside the card's edges
    [SerializeField] private float sectionGap = 6f;          // Between the card's rows
    [SerializeField] private float headerHeight = 22f;
    [SerializeField] private float descriptionHeight = 32f;  // Two lines at descriptionFontSize
    [SerializeField] private float statLineHeight = 16f;
    [SerializeField] private float chipHeight = 40f;
    [SerializeField] private float chipGap = 6f;
    [SerializeField] private float chipBorder = 2f;          // The selected chip's outline
    [SerializeField] private float swatchSize = 10f;         // The material's color, on its chip
    [SerializeField] private float hintHeight = 16f;         // One line at hintFontSize
    [SerializeField] private float onHandHeight = 26f;       // Material mode's big count, one line at onHandFontSize
    [SerializeField] private float useChipHeight = 26f;      // Material mode's read-only use chips: one line
    [SerializeField] private int titleFontSize = 15;
    [SerializeField] private int onHandFontSize = 20;
    [SerializeField] private int tagFontSize = 11;
    [SerializeField] private int descriptionFontSize = 12;
    [SerializeField] private int statFontSize = 12;
    [SerializeField] private int chipNameFontSize = 12;
    [SerializeField] private int chipCostFontSize = 10;
    [SerializeField] private int hintFontSize = 10;
    [SerializeField] private float toggleWidth = 110f;       // #143: the placed card's Enable/Disable button
    [SerializeField] private float toggleHeight = 24f;
    [SerializeField] private int toggleFontSize = 12;
    [SerializeField] private float refreshSeconds = 0.25f;   // How often counts and affordability re-check (unscaled)

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);                 // Matches the build rail
    [SerializeField] private Color chipColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color chipSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f);   // Amber outline on the selected chip
    [SerializeField] private Color unaffordableCostColor = new Color(1f, 0.38f, 0.38f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color toggleOnColor = new Color(0.25f, 0.25f, 0.30f, 1f);  // #143: "Disable" (the node is on)
    [SerializeField] private Color toggleOffColor = new Color(0.55f, 0.42f, 0.12f, 1f); // #143: "Enable" (it's off: amber)

    public PlacementController PlacementController { get => placementController; set => placementController = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Color ChipColor => chipColor;
    public Color ChipSelectedColor => chipSelectedColor;
    public Color UnaffordableCostColor => unaffordableCostColor;

    /// <summary>One material chip: what an order of the armed type in this material costs, and how much is owned.</summary>
    public readonly struct Chip
    {
        public readonly MaterialType material;
        public readonly string name;
        public readonly int cost;
        public readonly int owned;
        public readonly bool selected;
        public readonly bool affordable; // An order in this material would fund now; false = it would wait

        public Chip(MaterialType material, string name, int cost, int owned, bool selected, bool affordable)
        {
            this.material = material;
            this.name = name;
            this.cost = cost;
            this.owned = owned;
            this.selected = selected;
            this.affordable = affordable;
        }
    }

    private float refreshTimer;

    // ---- chrome (code-built; a recompile can null or empty any of it, so every reader guards for null and length, #83) ----
    private Canvas canvas;
    // #137: true once Build has wired this chrome in the current domain. NonSerialized, so a domain reload resets it
    // while the canvas reference survives: that pair is how OnEnable tells a reload (chip listeners and the
    // ModeChanged subscription lost) from an ordinary re-enable.
    [System.NonSerialized] private bool chromeWired;
    private RectTransform cardRect;
    private Text titleText;
    private Text tagText;
    private Text descriptionText;
    private Text statsText;
    private RectTransform chipRow;
    private Text hintText;
    private Text onHandText;   // Material mode only
    private Text waitingText;  // Material mode only
    private ChipView[] chipViews;
    private Font font;
    private Button toggleButton; // #143: placed mode, power nodes only
    private Image toggleImage;
    private Text toggleText;

    private sealed class ChipView
    {
        public RectTransform rect;
        public Button button;
        public Image border;
        public Image swatch;
        public Text name;
        public Text cost;
        public MaterialType material;
    }

    // ---- lifecycle ----

    private void Start()
    {
        placementController ??= FindAnyObjectByType<PlacementController>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();

        PlacementController.EnsureEventSystem();
        Build();
        SubscribeToMode();
    }

    // #137: a domain reload mid-Play (Recompile And Continue Playing) re-runs OnEnable but not Start, so chrome that
    // outlived the reload is rebuilt with its wiring here.
    private void OnEnable()
    {
        if (canvas == null || chromeWired) return;
        DestroyChrome();
        Build();
        SubscribeToMode();
    }

    // Idempotent: Start and a reload both come through here, and the card must hear each mode change once.
    private void SubscribeToMode()
    {
        if (placementController == null) return;
        placementController.ModeChanged -= Repaint;
        placementController.ModeChanged += Repaint; // The card follows the mode and the material at once
    }

    private void OnDestroy()
    {
        if (placementController != null) placementController.ModeChanged -= Repaint;
        DestroyChrome();
    }

    private void Update()
    {
        refreshTimer += Time.unscaledDeltaTime; // Counts and affordability track the stockpile even while paused
        if (refreshTimer < refreshSeconds) return;
        refreshTimer = 0f;
        Repaint();
    }

    // ---- what the card reads ----

    /// <summary>The armed type; null when nothing is armed (Neutral or Dig).</summary>
    public PlaceableType? ArmedType => placementController != null ? placementController.SelectedType : null;

    /// <summary>The material the STOCK panel has selected (#126); null when it has none.</summary>
    public MaterialType? ShownMaterial { get; private set; }

    /// <summary>Wire mode (#142): Lay conduit is armed; the card describes the verb.</summary>
    public bool IsWireMode => placementController != null && placementController.IsWiring;

    /// <summary>Pipe mode (#145): Lay pipe is armed; the card describes the verb.</summary>
    public bool IsPipeMode => placementController != null && placementController.IsPiping;

    /// <summary>The verb tool armed now (#142 Lay conduit, #145 Lay pipe), or null.</summary>
    public BuildVerb? ArmedVerb => IsWireMode ? BuildVerb.LayConduit : IsPipeMode ? BuildVerb.LayPipe : (BuildVerb?)null;

    /// <summary>Verb mode: a verb tool is armed (wire or pipe); the card describes it.</summary>
    public bool IsVerbMode => ArmedVerb != null;

    /// <summary>The built placeable the controller has selected (#142); null when none.</summary>
    public Placeable ShownPlaceable => placementController != null ? placementController.SelectedPlaceable : null;

    /// <summary>Placed mode (#142): nothing armed and a built placeable selected.</summary>
    public bool IsPlacedMode => ArmedType == null && !IsVerbMode && ShownPlaceable != null;

    /// <summary>
    /// Material mode (#126): a material is shown and nothing is armed (an armed type always wins the card), and no placed
    /// building is selected (#142: the most recent selection wins, and the two never stand together).
    /// </summary>
    public bool IsMaterialMode => ArmedType == null && !IsVerbMode && ShownPlaceable == null && ShownMaterial != null;

    /// <summary>The card shows while a buildable is armed, in wire mode, in placed mode, or in material mode.</summary>
    public bool IsVisible => ArmedType != null || IsVerbMode || IsPlacedMode || ShownMaterial != null;

    /// <summary>The STOCK panel's selection (#126): the material to describe while nothing is armed, or null to stop.</summary>
    public void ShowMaterial(MaterialType? material)
    {
        ShownMaterial = material;
        Repaint();
    }

    // The type the card is about: the armed one, else the selected placed one (#142).
    private PlaceableType? CardType => ArmedType ?? (IsPlacedMode ? ShownPlaceable.Type : (PlaceableType?)null);

    /// <summary>The card's category, when it shows a type or the wire verb (#142); false in material mode or hidden.</summary>
    public bool TryGetCardCategory(out BuildCategory category)
    {
        if (CardType != null) return BuildPresentation.TryGetCategory(CardType.Value, out category);
        if (ArmedVerb != null && BuildPresentation.TryGetVerb(ArmedVerb.Value, out BuildPresentation.VerbEntry verb))
        {
            category = verb.category;
            return true;
        }
        category = default;
        return false;
    }

    /// <summary>The armed type's name, the wire verb's, the placed building's, else the shown material's; empty with none.</summary>
    public string Title =>
        ArmedType != null ? BuildPresentation.DisplayName(ArmedType.Value)
        : ArmedVerb != null ? (BuildPresentation.TryGetVerb(ArmedVerb.Value, out BuildPresentation.VerbEntry verb) ? verb.displayName : "")
        : IsPlacedMode ? BuildPresentation.DisplayName(ShownPlaceable.Type)
        : ShownMaterial != null ? MaterialName(ShownMaterial.Value)
        : "";

    /// <summary>The card's category ("Living", "Power"), else the shown material's family ("Stones"); empty with neither.</summary>
    public string CategoryTag =>
        TryGetCardCategory(out BuildCategory category) ? BuildPresentation.CategoryName(category)
        : IsMaterialMode ? BuildPresentation.FamilyName(BuildPresentation.FamilyOf(ShownMaterial.Value))
        : "";

    public string Description =>
        CardType != null ? BuildPresentation.Description(CardType.Value)
        : ArmedVerb != null ? (BuildPresentation.TryGetVerb(ArmedVerb.Value, out BuildPresentation.VerbEntry verb) ? verb.description : "")
        : IsMaterialMode ? BuildPresentation.MaterialDescription(ShownMaterial.Value)
        : "";

    /// <summary>The bottom hint for the current mode; empty in material mode.</summary>
    public string Hint => ArmedType != null ? HintText : IsWireMode ? WireHintText : IsPipeMode ? PipeHintText
        : IsPlacedMode ? PlacedHintText : "";

    private Stockpile CurrentStockpile => placementManager != null ? placementManager.Stockpile : null;

    private string MaterialName(MaterialType material)
    {
        Stockpile stockpile = CurrentStockpile;
        return stockpile != null ? stockpile.DisplayName(material) : material.ToString();
    }

    // ---- material mode (#126) ----

    /// <summary>"12 on hand": the live stockpile count of the shown material. Empty outside material mode.</summary>
    public string OnHandLine
    {
        get
        {
            if (!IsMaterialMode) return "";
            Stockpile stockpile = CurrentStockpile;
            return $"{(stockpile != null ? stockpile.Count(ShownMaterial.Value) : 0)} on hand";
        }
    }

    /// <summary>"Source: Dug · mid depths". Empty outside material mode, or for a material with no source copy.</summary>
    public string SourceLine
    {
        get
        {
            if (!IsMaterialMode) return "";
            string source = BuildPresentation.MaterialSource(ShownMaterial.Value);
            return string.IsNullOrEmpty(source) ? "" : $"Source: {source}";
        }
    }

    /// <summary>"Waiting orders: 2": orders placed in the shown material, not yet funded. Empty outside material mode.</summary>
    public string WaitingLine => IsMaterialMode && placementManager != null ? $"Waiting orders: {placementManager.WaitingOrders(ShownMaterial.Value)}" : "";

    /// <summary>The shown material's property highlights; empty outside material mode. See MaterialHighlights(manager, material).</summary>
    public List<string> MaterialHighlights() =>
        IsMaterialMode ? MaterialHighlights(placementManager, ShownMaterial.Value) : new List<string>();

    /// <summary>
    /// What a material makes each placeable type do, where its property row differs from the defaults (#110 table), in
    /// type order: a rest rate other than 1 ("Lair Cot rest ×1.25"), and the magical-creatures flag ("Lair Cot rests
    /// magical creatures"). restQuality has no consumer and is not shown. Empty for all-default rows, or no manager.
    /// </summary>
    public static List<string> MaterialHighlights(PlacementManager manager, MaterialType material)
    {
        var lines = new List<string>();
        if (manager == null) return lines;
        foreach (PlaceableType type in System.Enum.GetValues(typeof(PlaceableType)))
        {
            if (!manager.TryGetProperties(type, material, out PlaceableProperty row)) continue;
            string name = BuildPresentation.DisplayName(type);
            if (!Mathf.Approximately(row.restRateMultiplier, 1f)) lines.Add($"{name} rest ×{Number(row.restRateMultiplier, "0.0#")}");
            if (row.restsMagicalCreatures) lines.Add($"{name} rests magical creatures");
        }
        return lines;
    }

    /// <summary>
    /// The shown material's uses: one "Lair Cot · 3" per type with a cost row in it, in type order (display only).
    /// Empty outside material mode.
    /// </summary>
    public List<string> UseChipLabels()
    {
        var labels = new List<string>();
        if (!IsMaterialMode || placementManager == null) return labels;
        foreach (PlaceableType type in System.Enum.GetValues(typeof(PlaceableType)))
            if (placementManager.TryGetCost(type, ShownMaterial.Value, out MaterialAmount cost))
                labels.Add($"{BuildPresentation.DisplayName(type)} · {cost.amount}");
        return labels;
    }

    /// <summary>
    /// The armed type's stat lines: its build time, then the stats BuildPresentation declares for it, each read from
    /// PlacementManager for the selected material. A stat with nothing to read is left out. Empty with nothing armed.
    /// </summary>
    public List<string> StatLines()
    {
        var lines = new List<string>();
        if (IsWireMode && ArmedType == null) return WireLines(placementManager != null ? placementManager.PowerGrid : null);
        if (IsPipeMode && ArmedType == null) return PipeLines(placementManager != null ? placementManager.ManaFlow : null);
        if (IsPlacedMode) return PlacedLines(ShownPlaceable, placementManager);
        if (ArmedType == null || placementManager == null) return lines;

        PlaceableType type = ArmedType.Value;
        MaterialType material = placementController.SelectedMaterial;
        lines.Add($"Build time: {Number(placementManager.BuildSecondsFor(type), "0.#")}s");
        foreach (BuildStat stat in BuildPresentation.Stats(type))
        {
            string line = StatLine(stat, placementManager, type, material);
            if (!string.IsNullOrEmpty(line)) lines.Add(line);
        }
        return lines;
    }

    /// <summary>One stat's line for a type made of a material, from the manager's own values. Null with no manager.</summary>
    public static string StatLine(BuildStat stat, PlacementManager manager, PlaceableType type, MaterialType material)
    {
        if (manager == null) return null;
        switch (stat)
        {
            case BuildStat.RestRate:
                // The material's property row (#110); no row is the baseline rate, as GoblinAI's sleep reads it.
                float rate = manager.TryGetProperties(type, material, out PlaceableProperty row) ? row.restRateMultiplier : 1f;
                return $"Rest rate: ×{Number(rate, "0.0#")}";
            case BuildStat.FoodGrowth:
                return $"Grows 1 food every {Number(manager.MushroomGrowthSecondsPerFood, "0.#")}s";
            case BuildStat.FoodCapacity:
                return $"Holds up to {manager.MushroomCapacity} food";
            case BuildStat.TrapDamage:
                return $"Damage: {manager.SpikeDamage}";
            case BuildStat.PowerOutput: // #142: the power table's watts
                return manager.TryGetPower(type, out PlaceablePower output) && output.role == PowerRole.Producer ? $"Output {output.watts} W" : null;
            case BuildStat.NeedsOperator:
                return manager.TryGetPower(type, out PlaceablePower operated) && operated.needsOperator ? "Needs an imp" : null;
            case BuildStat.PowerDraw: // #143
                return manager.TryGetPower(type, out PlaceablePower draw) && draw.role == PowerRole.Consumer ? $"Draw {draw.watts} W" : null;
            case BuildStat.FuelUse: // #143: the row's fuel and burn interval
                return manager.TryGetPower(type, out PlaceablePower fuel) && !string.IsNullOrEmpty(fuel.fuelId)
                    ? $"Burns 1 {FuelUnitName(fuel.fuelId)} per {Number(fuel.fuelSecondsPerUnit, "0.#")} s"
                    : null;
            case BuildStat.LampCoverage: // #143
                return $"Plots within {manager.LampRadius} tiles grow ×{Number(manager.LampGrowthMultiplier, "0.#")}";
            case BuildStat.ManaCapacity: // #145
                return type == PlaceableType.Accumulator ? $"Holds up to {manager.AccumulatorCapacity} mana"
                    : type == PlaceableType.ManaContainer ? $"Holds up to {manager.ManaContainerCapacity} mana of one type"
                    : null;
            case BuildStat.ManaCollection: // #145
                return $"Collects {manager.AccumulatorCollectPerCycle} mana per {Number(manager.AccumulatorCollectSeconds, "0.#")} s from beside it";
            default:
                return null;
        }
    }

    // ---- wire and placed modes (#142) ----

    /// <summary>The pipe verb's lines (#145): imp time per tile and the throughput, from the service's own values.</summary>
    public static List<string> PipeLines(ManaFlow mana)
    {
        var lines = new List<string>();
        if (mana == null) return lines;
        lines.Add($"Imp time: {Number(mana.PipeSecondsPerTile, "0.0#")}s per tile");
        lines.Add($"Carries up to {Number(mana.PipeThroughputPerSecond, "0.#")} mana/s per network");
        lines.Add("No material cost · Removing is instant, no refund");
        return lines;
    }

    /// <summary>The wire verb's lines: the imp time per tile, from the grid's own value. None with no grid.</summary>
    public static List<string> WireLines(PowerGrid grid)
    {
        var lines = new List<string>();
        if (grid == null) return lines;
        lines.Add($"Imp time: {Number(grid.WireSecondsPerTile, "0.0#")}s per tile");
        lines.Add("No material cost · Removing is instant, no refund");
        return lines;
    }

    /// <summary>
    /// A built placeable's lines: what it's doing (cot claimed/free, plot food, trap armed/spent), then, for a power node,
    /// its role, its state and its grid's totals — for the furnace (#143) with its fuel, for the Grow-Lamp the plots it
    /// covers (counted through the manager; 0 without one). Empty for none.
    /// </summary>
    public static List<string> PlacedLines(Placeable placeable, PlacementManager manager = null)
    {
        var lines = new List<string>();
        if (placeable == null) return lines;

        if (placeable.Type == PlaceableType.LairCot) lines.Add(placeable.IsClaimed ? "Claimed" : "Free");
        if (placeable.TryGetComponent(out MushroomPlot plot)) lines.Add($"Food: {plot.FoodCount} / {plot.Capacity}");
        if (placeable.TryGetComponent(out SpikeTrap trap)) lines.Add(TrapLine(trap.IsArmed, trap.PoweredRearmProgress));
        if (placeable.TryGetComponent(out Accumulator accumulator)) // #145
        {
            ManaFlow mana = accumulator.Flow;
            float now = mana != null ? mana.ElapsedSeconds : 0f;
            lines.AddRange(AccumulatorLines(accumulator.Buffer.Type, accumulator.Buffer.Amount, accumulator.Buffer.Capacity,
                accumulator.HasCollectableNeighbor(mana), accumulator.RecentCollected(now), accumulator.RecentWindowSeconds,
                mana != null && mana.NetworkAt(accumulator.Tile) >= 0, mana != null ? mana.AccumulatorReserve : 0));
        }
        if (placeable.TryGetComponent(out ManaContainer container)) // #145
        {
            ManaFlow mana = container.Flow;
            lines.AddRange(ContainerLines(container.Store.Type, container.Store.Amount, container.Store.Capacity,
                mana != null && mana.NetworkAt(container.Tile) >= 0));
        }

        if (placeable.TryGetComponent(out PowerNode node) && node.Role != PowerRole.None)
        {
            PowerGrid.GridTotals totals = node.Totals;
            bool powered = PowerGrid.IsNodePowered(node.Enabled, totals.Connected, totals.Supply, totals.Demand);
            if (placeable.TryGetComponent(out FurnaceGenerator furnace))
                lines.AddRange(FurnaceLines(node.Watts, node.Enabled, totals.Connected, furnace.IsBurning, furnace.FuelOnHand,
                    furnace.FuelId, furnace.SecondsPerUnit, furnace.MinutesRemaining, totals.Supply, totals.Demand));
            else if (placeable.Type == PlaceableType.GrowLamp)
                lines.AddRange(LampLines(node.Watts, node.Enabled, totals.Connected, powered,
                    manager != null ? manager.PlotsCoveredFrom(placeable.Tile) : 0, totals.Supply, totals.Demand));
            else
                lines.AddRange(PowerLines(node.Role, node.Watts, node.NeedsOperator, node.IsTended, totals.Connected, powered,
                    totals.Supply, totals.Demand, node.Enabled));
        }
        return lines;
    }

    /// <summary>
    /// The accumulator's lines (#145): its buffer ("Buffer: Fire 12/20" or "Buffer: empty (0/20)"), what it's doing (full,
    /// collecting, or starved: nothing it can take beside it), its recent rate, and its pipe (what overflows where).
    /// </summary>
    public static List<string> AccumulatorLines(string type, int amount, int capacity, bool canCollectNow, int recent,
        float windowSeconds, bool onPipe, int reserve)
    {
        var lines = new List<string>
        {
            type != null && amount > 0 ? $"Buffer: {type} {amount}/{capacity}" : $"Buffer: empty (0/{capacity})",
            amount >= capacity ? "Full · stopped collecting"
                : canCollectNow ? "Collecting"
                : "Waiting: no mana it can take beside it",
            $"Collected {recent} in the last {Number(windowSeconds, "0.#")} s",
            onPipe ? $"On a pipe · keeps {reserve}, the rest flows to containers" : "Not on a pipe · keeps what it collects",
        };
        return lines;
    }

    /// <summary>The container's lines (#145): "Fire 40/100" or the empty note, and whether a pipe reaches it.</summary>
    public static List<string> ContainerLines(string type, int amount, int capacity, bool onPipe) => new List<string>
    {
        type != null && amount > 0 ? $"{type} {amount}/{capacity}" : $"Empty — adopts the first type piped in (0/{capacity})",
        onPipe ? "On a pipe" : "Not on a pipe · lay pipe onto its tile to connect it",
    };

    /// <summary>
    /// The furnace's lines (#143): "Producer · 100 W", its state, "Fuel: 7 mushrooms · burns 1 per 12 s", the minutes of
    /// burn left (its paid time plus the stock, if it alone drew on it), and the grid's totals when connected.
    /// </summary>
    public static List<string> FurnaceLines(int watts, bool enabled, bool connected, bool burning, int fuelOnHand, string fuelId,
        float secondsPerUnit, float minutesLeft, int supply, int demand)
    {
        var lines = new List<string>
        {
            $"Producer · {watts} W",
            FurnaceStateLine(enabled, connected, burning),
            $"Fuel: {fuelOnHand} {FuelUnitName(fuelId, fuelOnHand)} · burns 1 per {Number(secondsPerUnit, "0.#")} s",
            minutesLeft > 0f ? $"About {Number(minutesLeft, "0.#")} min of fuel left" : "No fuel left",
        };
        if (connected) lines.Add($"Grid: {supply} produced · {demand} demanded");
        return lines;
    }

    /// <summary>
    /// The furnace's state (#143): Disabled; Not connected (or "Burning · not connected": it burns fuel either way);
    /// Burning; Out of fuel.
    /// </summary>
    public static string FurnaceStateLine(bool enabled, bool connected, bool burning)
    {
        if (!enabled) return "Disabled";
        if (!connected) return burning ? "Burning · not connected" : "Not connected";
        return burning ? "Burning" : "Out of fuel";
    }

    /// <summary>The Grow-Lamp's lines (#143): "Consumer · 20 W", its state, "Covers 3 plots", the grid's totals when connected.</summary>
    public static List<string> LampLines(int watts, bool enabled, bool connected, bool powered, int plotsCovered, int supply, int demand)
    {
        var lines = new List<string>
        {
            $"Consumer · {watts} W",
            PowerStateLine(PowerRole.Consumer, false, false, connected, powered, enabled),
            plotsCovered == 1 ? "Covers 1 plot" : $"Covers {plotsCovered} plots",
        };
        if (connected) lines.Add($"Grid: {supply} produced · {demand} demanded");
        return lines;
    }

    // A fuel's unit word: "mushrooms" (or "mushroom" for one), a material's name.
    private static string FuelUnitName(string fuelId, int count = 1)
    {
        string name = PlacementManager.FuelDisplayName(fuelId);
        return count == 1 && name.EndsWith("s") ? name.Substring(0, name.Length - 1) : name;
    }

    /// <summary>A trap's line: "Armed", "Spent", or "Spent · rearming itself 45%" while powered progress is under way.</summary>
    public static string TrapLine(bool armed, float poweredProgress) =>
        armed ? "Armed"
        : poweredProgress > 0f ? $"Spent · rearming itself {Mathf.FloorToInt(Mathf.Clamp01(poweredProgress) * 100f)}%"
        : "Spent";

    /// <summary>
    /// A node's power lines: the role ("Producer · 40 W", "Consumer · 10 W"), the state, and the grid's totals when
    /// connected. Wheel states: Disabled / Not connected / Producing / Untended; consumer states: Disabled / Not connected /
    /// Powered / Unpowered — grid over-drawn. (#143: "drawn whenever built" left the consumer line once nodes could be
    /// switched off.)
    /// </summary>
    public static List<string> PowerLines(PowerRole role, int watts, bool needsOperator, bool tended, bool connected, bool powered,
        int supply, int demand, bool enabled = true)
    {
        var lines = new List<string>();
        if (role == PowerRole.None) return lines;
        lines.Add(role == PowerRole.Producer ? $"Producer · {watts} W" : $"Consumer · {watts} W");
        lines.Add(PowerStateLine(role, needsOperator, tended, connected, powered, enabled));
        if (connected) lines.Add($"Grid: {supply} produced · {demand} demanded");
        return lines;
    }

    public static string PowerStateLine(PowerRole role, bool needsOperator, bool tended, bool connected, bool powered, bool enabled = true)
    {
        if (!enabled) return "Disabled"; // #143: off by choice, whatever the grid is doing
        if (!connected) return "Not connected";
        if (role == PowerRole.Producer) return !needsOperator || tended ? "Producing" : "Untended";
        return powered ? "Powered" : "Unpowered — grid over-drawn";
    }

    private static string Number(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    // ---- the node toggle (#143) ----

    /// <summary>The shown placed building's power node, when it has a role; null otherwise (and outside placed mode).</summary>
    public PowerNode ShownNode
    {
        get
        {
            if (!IsPlacedMode) return null;
            return ShownPlaceable.TryGetComponent(out PowerNode node) && node.Role != PowerRole.None ? node : null;
        }
    }

    /// <summary>The card carries the Enable/Disable button: placed mode on a power node.</summary>
    public bool HasToggle => ShownNode != null;

    /// <summary>The button's label: "Disable" while the node is on, "Enable" while it's off. Empty with no toggle.</summary>
    public string ToggleLabel
    {
        get
        {
            PowerNode node = ShownNode;
            return node == null ? "" : node.Enabled ? "Disable" : "Enable";
        }
    }

    /// <summary>The button's click: flips the shown node's Enabled and repaints. False (nothing changes) with no toggle.</summary>
    public bool ToggleShownNode()
    {
        PowerNode node = ShownNode;
        if (node == null) return false;
        node.SetEnabled(!node.Enabled);
        Repaint();
        return true;
    }

    /// <summary>
    /// One chip per material the armed type can be ordered in (the controller's AvailableMaterials, in that order):
    /// its cost for this type, the amount owned, whether it is the selected material and whether an order in it would
    /// fund now. Empty with nothing armed.
    /// </summary>
    public List<Chip> Chips()
    {
        var chips = new List<Chip>();
        if (ArmedType == null || placementManager == null) return chips;

        PlaceableType type = ArmedType.Value;
        Stockpile stockpile = placementManager.Stockpile;
        foreach (MaterialType material in placementController.AvailableMaterials)
        {
            if (!placementManager.TryGetCost(type, material, out MaterialAmount cost)) continue;
            chips.Add(new Chip(
                material,
                stockpile != null ? stockpile.DisplayName(material) : material.ToString(),
                cost.amount,
                stockpile != null ? stockpile.Count(material) : 0,
                material == placementController.SelectedMaterial,
                placementManager.WouldFundNow(type, material)));
        }
        return chips;
    }

    /// <summary>A chip's second line: "Cost 3 · Own 10", the cost red when an order in that material would wait.</summary>
    public string ChipCostLine(Chip chip) =>
        $"Cost {PlacementController.CostText(chip.cost, chip.affordable, unaffordableCostColor)} · Own {chip.owned}";

    /// <summary>A chip's click: orders of the armed type are placed in this material from now on. False if it can't be.</summary>
    public bool SelectChip(MaterialType material)
    {
        if (ArmedType == null) return false;
        bool selected = placementController.SelectMaterial(material);
        Repaint();
        return selected;
    }

    // ---- chrome ----

    /// <summary>Builds the (hidden) card. Start calls this; public so tests can paint without Play.</summary>
    public void Build()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("DetailCardCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // The chips are buttons

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var cardGo = new GameObject("Card");
        cardGo.transform.SetParent(canvasGo.transform, false);
        cardGo.AddComponent<Image>().color = panelColor; // A raycast target: a press on the card never reaches the board
        cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = Vector2.zero;
        cardRect.pivot = Vector2.zero;
        cardRect.anchoredPosition = new Vector2(leftOffset, bottomOffset);

        titleText = CreateText(cardGo.transform, "Title", titleFontSize, Color.white, TextAnchor.MiddleLeft);
        tagText = CreateText(cardGo.transform, "Tag", tagFontSize, secondaryTextColor, TextAnchor.MiddleRight);
        onHandText = CreateText(cardGo.transform, "OnHand", onHandFontSize, Color.white, TextAnchor.MiddleLeft);
        descriptionText = CreateText(cardGo.transform, "Description", descriptionFontSize, secondaryTextColor, TextAnchor.UpperLeft);
        statsText = CreateText(cardGo.transform, "Stats", statFontSize, Color.white, TextAnchor.UpperLeft);
        statsText.verticalOverflow = VerticalWrapMode.Overflow;

        var chipRowGo = new GameObject("Chips", typeof(RectTransform));
        chipRowGo.transform.SetParent(cardGo.transform, false);
        chipRow = (RectTransform)chipRowGo.transform;

        hintText = CreateText(cardGo.transform, "Hint", hintFontSize, secondaryTextColor, TextAnchor.UpperLeft);
        hintText.text = HintText;
        waitingText = CreateText(cardGo.transform, "Waiting", statFontSize, Color.white, TextAnchor.UpperLeft);
        CreateToggle(cardGo.transform);

        chromeWired = true; // Chips are built (and wired) later, by Repaint, under this same flag
        Repaint();
    }

    /// <summary>Destroys everything Build made.</summary>
    public void DestroyChrome()
    {
        if (canvas != null) DestroyChromeObject(canvas.gameObject);

        canvas = null;
        chromeWired = false;
        cardRect = null;
        titleText = null;
        tagText = null;
        descriptionText = null;
        statsText = null;
        chipRow = null;
        hintText = null;
        onHandText = null;
        waitingText = null;
        chipViews = null;
        toggleButton = null;
        toggleImage = null;
        toggleText = null;
    }

    private static void DestroyChromeObject(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    /// <summary>
    /// Repaints from the mode and the stockpile: hidden unless a type is armed or a material is shown; otherwise the armed
    /// type's texts, its stat lines for the selected material, and its chips with live costs, counts and the selected
    /// outline — or, in material mode, the shown material's (#126).
    /// </summary>
    public void Repaint()
    {
        if (canvas == null || cardRect == null) return; // Not built (EditMode), or lost to a recompile

        bool visible = IsVisible;
        cardRect.gameObject.SetActive(visible);
        if (!visible) return;

        bool materialMode = IsMaterialMode;
        bool hasToggle = !materialMode && HasToggle;
        if (toggleButton != null) toggleButton.gameObject.SetActive(hasToggle);
        if (onHandText != null) onHandText.gameObject.SetActive(materialMode);
        if (waitingText != null) waitingText.gameObject.SetActive(materialMode);
        if (hintText != null)
        {
            hintText.gameObject.SetActive(!materialMode);
            hintText.text = Hint;
        }

        float inner = cardWidth - 2f * padding;
        float y = padding;

        if (titleText != null)
        {
            titleText.text = Title;
            PlaceTopLeft(titleText.rectTransform, padding, y, inner, headerHeight);
        }
        if (tagText != null)
        {
            tagText.text = CategoryTag;
            tagText.color = !materialMode && TryGetCardCategory(out BuildCategory category)
                ? BuildPresentation.Accent(category)
                : secondaryTextColor;
            PlaceTopLeft(tagText.rectTransform, padding, y, inner, headerHeight);
        }
        y += headerHeight + sectionGap;

        if (materialMode)
        {
            if (onHandText != null)
            {
                onHandText.text = OnHandLine;
                PlaceTopLeft(onHandText.rectTransform, padding, y, inner, onHandHeight);
            }
            y += onHandHeight + sectionGap;
        }

        if (descriptionText != null)
        {
            descriptionText.text = Description;
            PlaceTopLeft(descriptionText.rectTransform, padding, y, inner, descriptionHeight);
        }
        y += descriptionHeight + sectionGap;

        if (materialMode)
        {
            RepaintMaterialMode(inner, y);
            return;
        }

        List<string> stats = StatLines();
        if (statsText != null)
        {
            statsText.text = string.Join("\n", stats);
            PlaceTopLeft(statsText.rectTransform, padding, y, inner, stats.Count * statLineHeight);
        }
        if (stats.Count > 0) y += stats.Count * statLineHeight + sectionGap;

        if (hasToggle && toggleButton != null)
        {
            bool on = ShownNode.Enabled;
            if (toggleText != null) toggleText.text = ToggleLabel;
            if (toggleImage != null) toggleImage.color = on ? toggleOnColor : toggleOffColor;
            PlaceTopLeft((RectTransform)toggleButton.transform, padding, y, toggleWidth, toggleHeight);
            y += toggleHeight + sectionGap;
        }

        List<Chip> chips = Chips();
        if (chipRow != null)
        {
            PlaceTopLeft(chipRow, padding, y, inner, chipHeight);
            RepaintChips(chips, inner);
        }
        if (chips.Count > 0) y += chipHeight + sectionGap;

        if (hintText != null) PlaceTopLeft(hintText.rectTransform, padding, y, inner, hintHeight);
        y += hintHeight + padding;

        cardRect.sizeDelta = new Vector2(cardWidth, y);
    }

    // Material mode's lower half (#126): source and property highlights, the read-only use chips, the waiting count.
    private void RepaintMaterialMode(float inner, float y)
    {
        var lines = new List<string>();
        string source = SourceLine;
        if (!string.IsNullOrEmpty(source)) lines.Add(source);
        lines.AddRange(MaterialHighlights());
        if (statsText != null)
        {
            statsText.text = string.Join("\n", lines);
            PlaceTopLeft(statsText.rectTransform, padding, y, inner, lines.Count * statLineHeight);
        }
        if (lines.Count > 0) y += lines.Count * statLineHeight + sectionGap;

        List<string> uses = UseChipLabels();
        if (chipRow != null)
        {
            PlaceTopLeft(chipRow, padding, y, inner, useChipHeight);
            RepaintUseChips(uses, inner);
        }
        if (uses.Count > 0) y += useChipHeight + sectionGap;

        if (waitingText != null)
        {
            waitingText.text = WaitingLine;
            PlaceTopLeft(waitingText.rectTransform, padding, y, inner, statLineHeight);
        }
        y += statLineHeight + padding;

        cardRect.sizeDelta = new Vector2(cardWidth, y);
    }

    // The use chips reuse the chip objects as plain labels: no swatch, no selection, no click.
    private void RepaintUseChips(List<string> labels, float rowWidth)
    {
        EnsureChipViews(labels.Count);
        if (chipViews == null) return;

        float width = labels.Count > 0 ? (rowWidth - (labels.Count - 1) * chipGap) / labels.Count : 0f;
        for (int i = 0; i < labels.Count && i < chipViews.Length; i++)
        {
            ChipView view = chipViews[i];
            if (view == null || view.rect == null) continue;

            view.rect.gameObject.name = "Use" + i;
            PlaceTopLeft(view.rect, i * (width + chipGap), 0f, width, useChipHeight);
            if (view.button != null)
            {
                view.button.interactable = false;
                view.button.transition = Selectable.Transition.None; // Not dimmed as a disabled button: it's a label
            }
            if (view.border != null) view.border.color = chipColor;
            if (view.swatch != null) view.swatch.enabled = false;
            if (view.name != null)
            {
                view.name.text = labels[i];
                view.name.rectTransform.anchorMin = Vector2.zero; // The whole chip: one centered line
                view.name.rectTransform.offsetMin = new Vector2(chipBorder + 4f, chipBorder);
            }
            if (view.cost != null) view.cost.text = "";
        }
    }

    private void RepaintChips(List<Chip> chips, float rowWidth)
    {
        EnsureChipViews(chips.Count);
        if (chipViews == null) return;

        float width = chips.Count > 0 ? (rowWidth - (chips.Count - 1) * chipGap) / chips.Count : 0f;
        for (int i = 0; i < chips.Count && i < chipViews.Length; i++)
        {
            ChipView view = chipViews[i];
            if (view == null || view.rect == null) continue;

            Chip chip = chips[i];
            view.material = chip.material;
            view.rect.gameObject.name = chip.material + "Chip";
            PlaceTopLeft(view.rect, i * (width + chipGap), 0f, width, chipHeight);
            if (view.button != null)
            {
                view.button.interactable = true; // The objects may have been use chips a moment ago
                view.button.transition = Selectable.Transition.ColorTint;
            }
            if (view.name != null)
            {
                view.name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                view.name.rectTransform.offsetMin = new Vector2(NameLeft, 0f);
            }

            if (view.border != null) view.border.color = chip.selected ? chipSelectedColor : chipColor;
            if (view.swatch != null)
            {
                Stockpile stockpile = placementManager != null ? placementManager.Stockpile : null;
                Color swatchColor = default;
                view.swatch.enabled = stockpile != null && stockpile.TryGetColor(chip.material, out swatchColor); // No catalog color, no swatch
                view.swatch.color = swatchColor;
            }
            if (view.name != null) view.name.text = chip.name;
            if (view.cost != null) view.cost.text = ChipCostLine(chip);
        }
    }

    // The chip objects match the chip count: rebuilt whenever it changes (another type's materials), or when the views
    // are gone or emptied while the chrome still stands.
    private void EnsureChipViews(int count)
    {
        if (chipRow == null) return;
        if (HasChipViews(count)) return;

        for (int i = chipRow.childCount - 1; i >= 0; i--)
        {
            GameObject stale = chipRow.GetChild(i).gameObject;
            stale.SetActive(false); // Destroy is deferred in Play: out of sight (and out of Find's reach) at once
            DestroyChromeObject(stale);
        }

        chipViews = new ChipView[count];
        for (int i = 0; i < count; i++) chipViews[i] = CreateChip(i);
    }

    private bool HasChipViews(int count)
    {
        if (chipViews == null || chipViews.Length != count) return false;
        foreach (ChipView view in chipViews)
            if (view == null || view.rect == null) return false;
        return true;
    }

    private ChipView CreateChip(int index)
    {
        var view = new ChipView();

        var go = new GameObject("Chip");
        go.transform.SetParent(chipRow, false);
        view.rect = go.AddComponent<RectTransform>();
        view.border = go.AddComponent<Image>();
        view.border.color = chipColor;
        view.button = go.AddComponent<Button>();
        view.button.targetGraphic = view.border;
        view.button.onClick.AddListener(() => ClickChip(index));

        // The chip's face, inset so the button's own image shows around it as the selected outline.
        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        var fill = fillGo.AddComponent<Image>();
        fill.color = chipColor;
        fill.raycastTarget = false;
        Stretch(fill.rectTransform, chipBorder, chipBorder, chipBorder, chipBorder);

        var swatchGo = new GameObject("Swatch");
        swatchGo.transform.SetParent(go.transform, false);
        view.swatch = swatchGo.AddComponent<Image>();
        view.swatch.raycastTarget = false;
        float inset = chipBorder + 4f;
        view.swatch.rectTransform.anchorMin = view.swatch.rectTransform.anchorMax = new Vector2(0f, 0.75f);
        view.swatch.rectTransform.pivot = new Vector2(0f, 0.5f);
        view.swatch.rectTransform.anchoredPosition = new Vector2(inset, 0f);
        view.swatch.rectTransform.sizeDelta = new Vector2(swatchSize, swatchSize);

        float textLeft = NameLeft;
        view.name = CreateText(go.transform, "Name", chipNameFontSize, Color.white, TextAnchor.MiddleLeft);
        view.name.horizontalOverflow = HorizontalWrapMode.Overflow;
        view.name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        view.name.rectTransform.anchorMax = new Vector2(1f, 1f);
        view.name.rectTransform.offsetMin = new Vector2(textLeft, 0f);
        view.name.rectTransform.offsetMax = new Vector2(-chipBorder, -chipBorder);

        view.cost = CreateText(go.transform, "Cost", chipCostFontSize, secondaryTextColor, TextAnchor.MiddleLeft);
        view.cost.supportRichText = true; // The cost is colored on its own
        view.cost.horizontalOverflow = HorizontalWrapMode.Overflow;
        view.cost.rectTransform.anchorMin = new Vector2(0f, 0f);
        view.cost.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        view.cost.rectTransform.offsetMin = new Vector2(inset, chipBorder);
        view.cost.rectTransform.offsetMax = new Vector2(-chipBorder, 0f);

        return view;
    }

    // The placed card's Enable/Disable button (#143). Its image is the only raycast target in it.
    private void CreateToggle(Transform card)
    {
        var go = new GameObject("Toggle");
        go.transform.SetParent(card, false);
        go.AddComponent<RectTransform>();
        toggleImage = go.AddComponent<Image>();
        toggleImage.color = toggleOnColor;
        toggleButton = go.AddComponent<Button>();
        toggleButton.targetGraphic = toggleImage;
        toggleButton.onClick.AddListener(() => ToggleShownNode());
        toggleText = CreateText(go.transform, "Label", toggleFontSize, Color.white, TextAnchor.MiddleCenter);
        Stretch(toggleText.rectTransform, 0f, 0f, 0f, 0f);
        go.SetActive(false);
    }

    // A material chip's name starts right of its swatch.
    private float NameLeft => chipBorder + 4f + swatchSize + 4f;

    private void ClickChip(int index)
    {
        if (chipViews == null || index < 0 || index >= chipViews.Length || chipViews[index] == null) return;
        SelectChip(chipViews[index].material);
    }

    // ---- chrome helpers ----

    private Text CreateText(Transform parent, string name, int size, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    // Fills the parent, inset by the given margins.
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // x right and y down from the parent's top-left corner.
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
