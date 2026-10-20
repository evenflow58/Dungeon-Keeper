using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The board's 3D view and the single source of tile ↔ world mapping (ticket #74). The ground is the XZ
/// plane at y = 0 (Y up): tile (x, y) has its center — its ground point — at BoardOrigin + (x + 0.5, 0, y + 0.5),
/// with the origin at (−Width/2, 0, −Height/2). Each tile is one code-built box view, created once under a
/// code-made root and restyled from OnTileChanged (never destroyed/recreated): Floor a thin slab whose top is
/// the ground, Rock/Designated a raised block, Entrance a slab with a simple box-built door.
/// Lit (ticket #75): every box is a URP Lit surface, one shared material per color, casting and receiving
/// shadows, so faces shade from the scene's light instead of baked tints.
/// Fog of war (#131): with a FogOfWar wired, an unexplored tile renders as a uniform dark block at rock height
/// whatever its state (no door), so the unexplored region is one dark mass; explored tiles render by state as before.
/// Without one, every tile is explored.
/// Tile identity (#132): explored Rock is tinted by its material (TileIdentity, from the ImpDigger's band seam):
/// stone keeps rockColor, glimmerstone and deepstone get their own rock tints. Designated keeps its amber.
/// Conduit (#142): with a PowerGrid wired, each conduit tile draws on the floor: built conduit as a copper inlay (a centre
/// plate plus an arm toward each 4-neighbor that has conduit), pending conduit as the same run in a translucent ghost.
/// Explored only: conduit under fog doesn't render.
/// View modes (#144): the renderer holds the board's ViewMode. Every tile color is resolved through PowerOverlay.Resolve,
/// which leaves Normal untouched; in Power mode explored tiles off the network dim, built conduit takes its grid's hue
/// (the warning hue while that grid is browned out), and a built node's tile takes its state color. Fog wins over every
/// overlay: unexplored tiles render exactly as in Normal. The mode only overrides at resolution time, so switching back
/// to Normal restores the normal colors exactly. While an overlay is on it repaints when the network changes shape and on
/// a short cadence for live state (tending, fuel, toggles); in Normal it doesn't poll. Non-power placeables turn
/// see-through in the power overlay so conduit under them shows (PlacementManager applies it on ViewModeChanged and
/// OverlayRepainted, at overlayPropAlpha).
/// </summary>
public class BoardRenderer : MonoBehaviour
{
    public const string LitShaderName = "Universal Render Pipeline/Lit";
    public const float DefaultOverlayLift = 0.01f; // overlayLift's default
    private const int PickSamples = 32; // Ray steps between the block-top plane and the ground in RaycastBoard

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private FogOfWar fogOfWar; // Optional (#131): unwired, everything renders as explored
    [SerializeField] private ImpDigger impDigger; // Optional (#132): the band seam rock is tinted by; unwired, all rock is rockColor
    [SerializeField] private PowerGrid powerGrid; // Optional (#142): the conduit layer drawn on the floor; unwired, none
    [SerializeField] private ManaFlow manaFlow;   // Optional (#145): the vent, mana units and pipes; unwired, none (found on this object)

    [Header("Tile Shapes")]
    [SerializeField] private float rockHeight = 0.6f;     // Rock and designated blocks, in tiles
    [SerializeField] private float floorThickness = 0.1f; // Floor slab; its top face is the ground (y = 0)
    [SerializeField] private float overlayLift = DefaultOverlayLift; // Hover highlight and drag previews sit this far above a surface

    [Header("Tile Colors (Placeholder Art)")]
    [SerializeField] private Color rockColor = new Color(0.22f, 0.22f, 0.22f, 1f);       // Dark gray
    [SerializeField] private Color floorColor = new Color(0.48f, 0.32f, 0.18f, 1f);      // Brown
    [SerializeField] private Color designatedColor = new Color(0.85f, 0.65f, 0.15f, 1f); // Amber/Gold
    [SerializeField] private Color doorPanelColor = new Color(0.72f, 0.46f, 0.22f, 1f);  // Warm wood, lighter than Floor
    [SerializeField] private Color doorFrameColor = new Color(0.16f, 0.09f, 0.04f, 1f);  // Near-black frame and lintel
    [SerializeField] private Color doorIronColor = new Color(0.20f, 0.20f, 0.22f, 1f);   // Bands and handle
    [SerializeField] private Color fogColor = new Color(0.06f, 0.06f, 0.07f, 1f);        // Unexplored (#131): darker than rock, one tone for every state
    [SerializeField] private Color glimmerstoneRockColor = new Color(0.45f, 0.38f, 0.55f, 1f); // #132: rock of the shallow band, a rockier cousin of the catalog lavender
    [SerializeField] private Color deepstoneRockColor = new Color(0.22f, 0.26f, 0.36f, 1f);    // #132: rock of the deep band, a rockier cousin of the catalog slate

    [Header("Conduit (#142)")]
    [SerializeField] private Color conduitColor = new Color(0.80f, 0.48f, 0.22f, 1f);              // Copper inlay
    [SerializeField] private Color pendingConduitColor = new Color(0.95f, 0.70f, 0.40f, 0.45f);    // Ghost run: ordered, not wired
    [SerializeField] private float conduitWidth = 0.16f;     // Of the centre plate and the arms, in tiles
    [SerializeField] private float conduitThickness = 0.012f; // Raised this far above the floor's top

    [Header("Power Overlay (#144)")]
    [SerializeField] private List<Color> gridPalette = new List<Color>
    {
        new Color(0.25f, 0.55f, 1.00f, 1f), // Blue
        new Color(0.30f, 0.85f, 0.40f, 1f), // Green
        new Color(0.80f, 0.40f, 0.95f, 1f), // Violet
        new Color(0.20f, 0.80f, 0.80f, 1f), // Teal
    }; // Grid hues by grid id; no reds or yellows, which belong to faults and producers
    [SerializeField] private Color brownoutGridColor = new Color(1.00f, 0.35f, 0.15f, 1f);      // A grid whose supply is short
    [SerializeField] private Color pendingOverlayColor = new Color(0.80f, 0.80f, 0.80f, 0.45f); // Planned conduit: a neutral ghost
    [SerializeField] private Color activeProducerColor = new Color(1.00f, 0.88f, 0.30f, 1f);    // Making watts now: bright
    [SerializeField] private Color idleProducerColor = new Color(0.55f, 0.48f, 0.25f, 1f);      // Connected, not producing
    [SerializeField] private Color poweredConsumerColor = new Color(0.55f, 0.90f, 1.00f, 1f);   // Lit
    [SerializeField] private Color unpoweredConsumerColor = new Color(0.95f, 0.15f, 0.12f, 1f); // The fault red
    [SerializeField] private Color disabledNodeColor = new Color(0.45f, 0.45f, 0.45f, 1f);      // Switched off: grey
    [SerializeField] private Color unconnectedNodeColor = new Color(0.20f, 0.20f, 0.22f, 1f);   // On no grid: dim
    [SerializeField] private float overlayDimFactor = 0.35f;      // Off-network explored tiles keep this much brightness
    [SerializeField] private float conduitTileTint = 0.5f;        // How far the floor under a built run leans to its grid hue
    [SerializeField] private float overlayRefreshSeconds = 0.25f; // Live-state repaint cadence while an overlay is on
    [SerializeField] private float overlayPropAlpha = 0.25f;      // Non-power placeables' opacity in the power overlay (conduit shows through)

    [Header("Mana (#145)")]
    [SerializeField] private float manaGlowSize = 0.26f;          // A mana unit's glow, in tiles
    [SerializeField] private float manaGlowHeight = 0.2f;         // Its centre above the floor
    [SerializeField] private float manaGlowIntensity = 2f;        // Emission: it glows past the lit surface
    [SerializeField] private Color pipeColor = new Color(0.46f, 0.40f, 0.62f, 1f);           // Slate-violet pipe
    [SerializeField] private Color pendingPipeColor = new Color(0.70f, 0.62f, 0.95f, 0.45f); // Ghost run: ordered, not built
    [SerializeField] private float pipeWidth = 0.12f;
    [SerializeField] private float pipeHeight = 0.07f;            // A raised tube, above conduit's flat inlay
    [SerializeField] private float pipeLaneOffset = 0.2f;         // Toward the tile's back edge (+Z), so pipe and conduit sharing a tile both show
    [SerializeField] private Color ventRockColor = new Color(0.16f, 0.13f, 0.12f, 1f);
    [SerializeField] private Color ventEmberColor = new Color(1.00f, 0.45f, 0.12f, 1f);
    [SerializeField] private Color ventDormantEmberColor = new Color(0.30f, 0.12f, 0.06f, 1f); // Dull while entombed or choked
    [SerializeField] private float ventEmberIntensity = 2.5f;

    [Header("Magic Overlay (#145)")]
    [SerializeField] private Color pipeOverlayColor = new Color(0.80f, 0.72f, 1.00f, 1f);       // A built pipe network: one neutral tint
    [SerializeField] private Color pendingPipeOverlayColor = new Color(0.85f, 0.85f, 0.85f, 0.45f);
    [SerializeField] private Color storeEmptyOverlayColor = new Color(0.55f, 0.52f, 0.62f, 1f); // A store holding nothing
    [SerializeField] private Color ventActiveOverlayColor = new Color(1.00f, 0.55f, 0.20f, 1f);
    [SerializeField] private Color ventDormantOverlayColor = new Color(0.40f, 0.22f, 0.14f, 1f);
    [SerializeField] private float manaTileTint = 0.6f;            // How far a mana tile's floor leans to its type's color
    [SerializeField] private float pipeTileTint = 0.45f;           // How far the floor under built pipe leans to the pipe tint

    [Header("Door (Placeholder Art)")]
    [SerializeField] private float doorHeight = 0.9f;  // Taller than the rock so the entrance reads from afar
    [SerializeField] private float doorPostWidth = 0.12f;

    [Header("Surface")]
    [SerializeField] private float surfaceSmoothness = 0f; // Lit materials: matte rock and earth (no highlights, no reflections)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }

    /// <summary>The conduit layer this view draws (#142). Setting it moves the subscription and redraws every conduit tile.</summary>
    public PowerGrid Grid
    {
        get => powerGrid;
        set
        {
            if (powerGrid == value) return;
            UnsubscribeGrid();
            ClearConduitViews();
            powerGrid = value;
            if (isActiveAndEnabled) SubscribeGrid();
            RefreshAllConduit();
        }
    }

    /// <summary>The mana service this view draws (#145). Setting it moves the subscriptions and redraws its views.</summary>
    public ManaFlow Mana
    {
        get => manaFlow;
        set
        {
            if (manaFlow == value) return;
            UnsubscribeMana();
            ClearManaViews();
            manaFlow = value;
            if (isActiveAndEnabled) SubscribeMana();
            RefreshAllMana();
        }
    }

    /// <summary>The digger whose band seam names each rock's material (#132). Setting it restyles built views.</summary>
    public ImpDigger Digger
    {
        get => impDigger;
        set
        {
            impDigger = value;
            if (tileViews != null) RenderFullBoard();
        }
    }

    /// <summary>The fog this view follows (#131). Setting it moves the reveal subscription and restyles built views.</summary>
    public FogOfWar Fog
    {
        get => fogOfWar;
        set
        {
            if (fogOfWar == value) return;
            UnsubscribeFog();
            fogOfWar = value;
            if (isActiveAndEnabled) SubscribeFog();
            if (tileViews != null) RenderFullBoard();
        }
    }

    /// <summary>The board's view mode (#144). Normal unless an overlay is on.</summary>
    public ViewMode Mode => viewMode;

    /// <summary>Raised after the view mode changes, with the new mode.</summary>
    public event System.Action<ViewMode> ViewModeChanged;

    /// <summary>Raised after each overlay repaint (the cadence and network changes), so model-level overlay looks can follow.</summary>
    public event System.Action OverlayRepainted;

    public float OverlayDimFactor => overlayDimFactor;
    public float OverlayPropAlpha => overlayPropAlpha;
    public float OverlayRefreshSeconds { get => overlayRefreshSeconds; set => overlayRefreshSeconds = value; }
    public IReadOnlyList<Color> GridPalette => gridPalette;
    public Color BrownoutGridColor => brownoutGridColor;
    public Color PendingOverlayColor => pendingOverlayColor;

    public float RockHeight { get => rockHeight; set => rockHeight = value; }
    public float FloorThickness => floorThickness;
    public float OverlayLift => overlayLift;
    public Vector3 BoardOrigin => dungeonBoard != null
        ? new Vector3(-dungeonBoard.Width * 0.5f, 0f, -dungeonBoard.Height * 0.5f)
        : Vector3.zero;

    /// <summary>Parent of the tile views; null until RenderFullBoard has run (Start).</summary>
    public Transform ViewRoot => viewRoot;

    /// <summary>How a tile state is shaped: a unit box's scale and the height of its center.</summary>
    public struct TileShape
    {
        public Vector3 Scale;
        public float CenterY;
        public TileShape(Vector3 scale, float centerY) { Scale = scale; CenterY = centerY; }
        public float TopY => CenterY + Scale.y * 0.5f;
        public float BottomY => CenterY - Scale.y * 0.5f;
    }

    private Transform viewRoot;
    private GameObject[,] tileViews;
    private readonly Dictionary<Vector2Int, GameObject> doors = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<Vector2Int, GameObject> conduitViews = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<Vector2Int, GameObject> pipeViews = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<Vector2Int, GameObject> manaViews = new Dictionary<Vector2Int, GameObject>();
    private GameObject ventView;
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private Mesh boxMesh;
    private ViewMode viewMode = ViewMode.Normal;
    private float overlayTimer;

    private void Awake()
    {
        InitializeReferences();
    }

    private void OnEnable()
    {
        if (dungeonBoard != null)
        {
            dungeonBoard.OnTileChanged += HandleTileChanged;
        }
        SubscribeFog();
        SubscribeGrid();
        SubscribeMana();
    }

    private void OnDisable()
    {
        if (dungeonBoard != null)
        {
            dungeonBoard.OnTileChanged -= HandleTileChanged;
        }
        UnsubscribeFog();
        UnsubscribeGrid();
        UnsubscribeMana();
    }

    // Unsubscribe first so a repeated OnEnable never doubles the handlers (#139 posture).
    private void SubscribeMana()
    {
        if (manaFlow == null) return;
        manaFlow.ManaChanged -= RefreshMana;
        manaFlow.ManaChanged += RefreshMana;
        manaFlow.PipeChanged -= HandlePipeChanged;
        manaFlow.PipeChanged += HandlePipeChanged;
        manaFlow.VentChanged -= RefreshVent;
        manaFlow.VentChanged += RefreshVent;
    }

    private void UnsubscribeMana()
    {
        if (manaFlow == null) return;
        manaFlow.ManaChanged -= RefreshMana;
        manaFlow.PipeChanged -= HandlePipeChanged;
        manaFlow.VentChanged -= RefreshVent;
    }

    private void SubscribeGrid()
    {
        if (powerGrid == null) return;
        powerGrid.ConduitChanged -= HandleConduitChanged;
        powerGrid.ConduitChanged += HandleConduitChanged;
        powerGrid.NetworkChanged -= HandleNetworkChanged;
        powerGrid.NetworkChanged += HandleNetworkChanged;
    }

    private void UnsubscribeGrid()
    {
        if (powerGrid == null) return;
        powerGrid.ConduitChanged -= HandleConduitChanged;
        powerGrid.NetworkChanged -= HandleNetworkChanged;
    }

    // Live state (a wheel tended or left, a furnace's burn lapsing, a toggle) has no event: an overlay polls it on the
    // cadence. Unscaled, so the overlay keeps up while paused. Normal mode never polls.
    private void Update()
    {
        if (viewMode == ViewMode.Normal) return;
        overlayTimer += Time.unscaledDeltaTime;
        if (overlayTimer < overlayRefreshSeconds) return;
        overlayTimer = 0f;
        RepaintOverlay();
    }

    // Unsubscribe first so a repeated OnEnable (or a Fog set while enabled) never doubles the handler.
    private void SubscribeFog()
    {
        if (fogOfWar == null) return;
        fogOfWar.TileRevealed -= HandleTileRevealed;
        fogOfWar.TileRevealed += HandleTileRevealed;
    }

    private void UnsubscribeFog()
    {
        if (fogOfWar != null) fogOfWar.TileRevealed -= HandleTileRevealed;
    }

    private void Start()
    {
        InitializeReferences();
        RenderFullBoard();
    }

    private void OnDestroy()
    {
        foreach (Material material in materials.Values) DestroyRuntimeObject(material);
        materials.Clear();
        DestroyRuntimeObject(boxMesh); // The views are children of this object and go with it
    }

    public void InitializeReferences()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        manaFlow ??= GetComponent<ManaFlow>(); // #145: the service lives on the DungeonManager beside this view
    }

    // ---- shapes and colors (pure: what each state looks like) ----

    public static bool IsRaised(TileState state) => state == TileState.Rock || state == TileState.Designated;

    /// <summary>Floor and Entrance: a slab with its top at the ground. Rock and Designated: a block standing on it.</summary>
    public TileShape ShapeFor(TileState state) =>
        IsRaised(state)
            ? new TileShape(new Vector3(1f, rockHeight, 1f), rockHeight * 0.5f)
            : new TileShape(new Vector3(1f, floorThickness, 1f), -floorThickness * 0.5f);

    public Color ColorFor(TileState state)
    {
        switch (state)
        {
            case TileState.Floor:      return floorColor;
            case TileState.Designated: return designatedColor;
            case TileState.Entrance:   return floorColor; // The slab; the door on it is separate geometry
            default:                   return rockColor;
        }
    }

    // ---- fog (#131): explored and state are independent inputs to one style ----

    /// <summary>A tile's shape given whether it's explored: unexplored is the fog block at rock height, whatever the state.</summary>
    public TileShape ShapeFor(TileState state, bool explored) => ShapeFor(explored ? state : TileState.Rock);

    /// <summary>A tile's color given whether it's explored: unexplored is the one fog color, whatever the state.</summary>
    public Color ColorFor(TileState state, bool explored) => explored ? ColorFor(state) : fogColor;

    // ---- identity (#132): what an explored tile is made of ----

    /// <summary>The tile's identity (state, and for rock its material) from the board and the wired digger.</summary>
    public TileIdentity IdentityAt(int x, int y) => TileIdentity.Of(dungeonBoard, impDigger, new Vector2Int(x, y));

    /// <summary>
    /// A tile's color from its identity: unexplored is the fog color whatever it is; explored Rock is its material's
    /// rock tint (stone when the material is unknown); every other state is its state color, so Designated stays
    /// amber in every band (designation is an order, not a material).
    /// </summary>
    public Color ColorFor(TileIdentity identity, bool explored)
    {
        if (!explored) return fogColor;
        return identity.State == TileState.Rock ? RockColorFor(identity.Material ?? MaterialType.Stone) : ColorFor(identity.State);
    }

    /// <summary>The rock tint for a material: stone is rockColor, the shallow and deep bands their own tints.</summary>
    public Color RockColorFor(MaterialType material)
    {
        switch (material)
        {
            case MaterialType.Glimmerstone: return glimmerstoneRockColor;
            case MaterialType.Deepstone:    return deepstoneRockColor;
            default:                        return rockColor;
        }
    }

    /// <summary>Whether the tile shows the door: only an explored Entrance (the fog hides the door entirely).</summary>
    public static bool ShowsDoor(TileState state, bool explored) => explored && state == TileState.Entrance;

    /// <summary>True when the player has seen the tile (always, without a FogOfWar).</summary>
    public bool IsExplored(int x, int y) => FogOfWar.IsSeen(fogOfWar, new Vector2Int(x, y));

    // What the tile looks like from outside: a raised block for Rock/Designated, and for every unexplored tile.
    private bool RendersRaised(int x, int y) => IsRaised(dungeonBoard.GetTile(x, y)) || !IsExplored(x, y);

    /// <summary>Height of a tile's top surface: the block top for raised tiles (and fog), the ground (0) otherwise.</summary>
    public float GetTileSurfaceHeight(int x, int y) =>
        dungeonBoard != null && dungeonBoard.IsInBounds(x, y) && RendersRaised(x, y) ? rockHeight : 0f;

    // ---- views ----

    public void RenderFullBoard()
    {
        if (dungeonBoard == null) return;

        EnsureViews();
        for (int x = 0; x < dungeonBoard.Width; x++)
        {
            for (int y = 0; y < dungeonBoard.Height; y++)
            {
                RefreshTile(x, y);
            }
        }
        RefreshAllConduit();
        RefreshAllMana();
    }

    public void RefreshTile(int x, int y)
    {
        if (dungeonBoard == null || tileViews == null) return;
        if (!dungeonBoard.IsInBounds(x, y)) return;

        TileState state = dungeonBoard.GetTile(x, y);
        bool explored = IsExplored(x, y);
        TileShape shape = ShapeFor(state, explored);
        Vector3 center = GetTileCenterWorldPosition(x, y);

        GameObject view = tileViews[x, y];
        view.transform.position = new Vector3(center.x, shape.CenterY, center.z);
        view.transform.localScale = shape.Scale;
        view.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(DisplayColorAt(x, y));

        // The door is built on first sight of an explored Entrance and only shown while it is one.
        Vector2Int tile = new Vector2Int(x, y);
        bool showsDoor = ShowsDoor(state, explored);
        if (showsDoor && !doors.ContainsKey(tile)) doors[tile] = CreateDoor(center);
        if (doors.TryGetValue(tile, out GameObject door)) door.SetActive(showsDoor);
    }

    // ---- view modes (#144) ----

    /// <summary>Switches the view mode and repaints the board. No-op for the current mode.</summary>
    public void SetViewMode(ViewMode mode)
    {
        if (viewMode == mode) return;
        viewMode = mode;
        overlayTimer = 0f;
        if (tileViews != null) RenderFullBoard();
        ViewModeChanged?.Invoke(mode);
    }

    /// <summary>Turns an overlay on, or back to Normal when it's already the one showing.</summary>
    public void ToggleViewMode(ViewMode mode) => SetViewMode(viewMode == mode ? ViewMode.Normal : mode);

    /// <summary>
    /// The color a tile displays in the current mode: its normal color in Normal mode (and under fog in every mode),
    /// otherwise the overlay's override, else its normal color dimmed. A designation keeps its amber in every mode.
    /// </summary>
    public Color DisplayColorAt(int x, int y)
    {
        bool explored = IsExplored(x, y);
        Color normal = ColorFor(IdentityAt(x, y), explored);
        if (viewMode == ViewMode.Normal || dungeonBoard == null) return normal;
        bool dimmed = dungeonBoard.GetTile(x, y) != TileState.Designated;
        return PowerOverlay.Resolve(viewMode, explored, normal, OverlayColorAt(new Vector2Int(x, y), normal), dimmed,
            overlayDimFactor);
    }

    // The power overlay's override for an explored tile: a built node's state color; under built conduit, the dimmed
    // floor leaning toward the grid's hue; otherwise none (the tile dims).
    private Color? OverlayColorAt(Vector2Int tile, Color normal)
    {
        if (viewMode == ViewMode.Magic) return MagicOverlayColorAt(tile, normal);
        if (viewMode != ViewMode.Power || powerGrid == null) return null;
        PowerNode node = powerGrid.NodeAt(tile);
        if (node != null) return NodeColor(LookOfNode(node));
        if (powerGrid.IsBuilt(tile))
            return Color.Lerp(PowerOverlay.Dim(normal, overlayDimFactor), GridColor(powerGrid.GridAt(tile)), conduitTileTint);
        return null;
    }

    /// <summary>A grid's overlay color: the warning hue while it's browned out, else its palette hue.</summary>
    public Color GridColor(int grid)
    {
        if (powerGrid != null && PowerGrid.IsBrownout(powerGrid.TotalsOfGrid(grid))) return brownoutGridColor;
        return PowerOverlay.GridHue(gridPalette, grid, conduitColor);
    }

    /// <summary>A live node's look in the power overlay.</summary>
    public static NodeLook LookOfNode(PowerNode node)
    {
        PowerGrid.GridTotals totals = node.Totals;
        bool powered = PowerGrid.IsNodePowered(node.Enabled, totals.Connected, totals.Supply, totals.Demand);
        return PowerOverlay.LookOf(node.Role, node.Enabled, totals.Connected, node.IsActive, powered);
    }

    /// <summary>The overlay color for a node look.</summary>
    public Color NodeColor(NodeLook look)
    {
        switch (look)
        {
            case NodeLook.ActiveProducer:    return activeProducerColor;
            case NodeLook.IdleProducer:      return idleProducerColor;
            case NodeLook.PoweredConsumer:   return poweredConsumerColor;
            case NodeLook.UnpoweredConsumer: return unpoweredConsumerColor;
            case NodeLook.Disabled:          return disabledNodeColor;
            default:                         return unconnectedNodeColor;
        }
    }

    /// <summary>
    /// Recolors the board for the current overlay without rebuilding anything: every tile's material and every conduit
    /// view's. The cadence and network changes call it; a no-op in Normal mode and before the views exist.
    /// </summary>
    public void RepaintOverlay()
    {
        if (viewMode == ViewMode.Normal || dungeonBoard == null || tileViews == null) return;
        for (int x = 0; x < dungeonBoard.Width; x++)
            for (int y = 0; y < dungeonBoard.Height; y++)
                tileViews[x, y].GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(DisplayColorAt(x, y));
        if (powerGrid != null)
        {
            foreach (KeyValuePair<Vector2Int, GameObject> entry in conduitViews)
            {
                if (entry.Value == null) continue;
                Material material = ConduitMaterial(entry.Key, powerGrid.IsBuilt(entry.Key));
                foreach (MeshRenderer part in entry.Value.GetComponentsInChildren<MeshRenderer>()) part.sharedMaterial = material;
            }
        }
        if (manaFlow != null)
        {
            foreach (KeyValuePair<Vector2Int, GameObject> entry in pipeViews)
            {
                if (entry.Value == null) continue;
                Material material = PipeMaterial(manaFlow.IsPipeBuilt(entry.Key));
                foreach (MeshRenderer part in entry.Value.GetComponentsInChildren<MeshRenderer>()) part.sharedMaterial = material;
            }
        }
        OverlayRepainted?.Invoke();
    }

    // ---- the magic overlay (#145) ----

    // The magic overlay's override for an explored tile: the vent by its state; a store by what it holds; a mana tile
    // leaning to its type's color; the floor under built pipe leaning to the pipe tint; otherwise none (the tile dims).
    private Color? MagicOverlayColorAt(Vector2Int tile, Color normal)
    {
        if (manaFlow == null) return null;
        if (manaFlow.IsVentTile(tile)) return manaFlow.VentDormant ? ventDormantOverlayColor : ventActiveOverlayColor;
        IManaPort port = manaFlow.PortAt(tile);
        if (port != null) return StoreOverlayColor(port.Store);
        Color dimmed = PowerOverlay.Dim(normal, overlayDimFactor);
        string type = manaFlow.TypeAt(tile);
        if (type != null) return Color.Lerp(dimmed, manaFlow.ColorOf(type), manaTileTint);
        if (manaFlow.IsPipeBuilt(tile)) return Color.Lerp(dimmed, pipeOverlayColor, pipeTileTint);
        return null;
    }

    /// <summary>A store's magic-overlay color: its type's color while it holds mana, the neutral empty color otherwise.</summary>
    public Color StoreOverlayColor(ManaStore store) =>
        store != null && store.Type != null && manaFlow != null ? manaFlow.ColorOf(store.Type) : storeEmptyOverlayColor;

    public Color PipeOverlayColor => pipeOverlayColor;
    public Color VentActiveOverlayColor => ventActiveOverlayColor;
    public Color VentDormantOverlayColor => ventDormantOverlayColor;
    public float ManaTileTint => manaTileTint;

    /// <summary>A pipe tile's color in the current mode: slate-violet or the pending ghost; in Magic, the network tint or a neutral ghost.</summary>
    public Color PipeDisplayColor(bool built)
    {
        if (viewMode == ViewMode.Magic) return built ? pipeOverlayColor : pendingPipeOverlayColor;
        return built ? pipeColor : pendingPipeColor;
    }

    private Material PipeMaterial(bool built)
    {
        Color color = PipeDisplayColor(built);
        return built ? MaterialFor(color) : CreatureModel.GhostMaterialFor(color);
    }

    // ---- pipes (#145): a raised tube on its own lane, conduit's twin otherwise ----

    /// <summary>A tile's pipe view (its tube or ghost run), or null when it shows none.</summary>
    public GameObject GetPipeView(int x, int y) =>
        pipeViews.TryGetValue(new Vector2Int(x, y), out GameObject view) && view != null && view.activeSelf ? view : null;

    /// <summary>Whether the tile's pipe shows: it has pipe and it's explored.</summary>
    public bool ShowsPipe(Vector2Int tile) => manaFlow != null && manaFlow.HasPipe(tile) && IsExplored(tile.x, tile.y);

    private void HandlePipeChanged(Vector2Int tile)
    {
        RefreshPipe(tile);
        foreach (Vector2Int d in ConduitNeighbors) RefreshPipe(tile + d); // Their arms toward this tile change too
        if (viewMode == ViewMode.Magic) RepaintOverlay(); // The floor under the run changes tint
    }

    /// <summary>Rebuilds one tile's pipe view: built tube, pending ghost, or nothing.</summary>
    public void RefreshPipe(Vector2Int tile)
    {
        if (dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return;
        if (pipeViews.TryGetValue(tile, out GameObject old))
        {
            pipeViews.Remove(tile);
            if (old != null) DestroyRuntimeObject(old);
        }
        if (!ShowsPipe(tile)) return;

        bool built = manaFlow.IsPipeBuilt(tile);
        Material material = PipeMaterial(built);
        Vector3 center = GetTileCenterWorldPosition(tile.x, tile.y);
        var view = new GameObject(built ? "Pipe" : "PipePending");
        view.transform.SetParent(ViewRootOrSelf(), false);
        view.transform.position = new Vector3(center.x, 0f, center.z + pipeLaneOffset);

        float y = pipeHeight * 0.5f;
        AddConduitPart(view, "Joint", material, new Vector3(0f, y, 0f), new Vector3(pipeWidth * 1.3f, pipeHeight * 1.2f, pipeWidth * 1.3f));
        foreach (Vector2Int d in ConduitNeighbors)
        {
            if (!manaFlow.HasPipe(tile + d)) continue;
            Vector3 dir = new Vector3(d.x, 0f, d.y);
            Vector3 scale = d.x != 0 ? new Vector3(0.5f, pipeHeight, pipeWidth) : new Vector3(pipeWidth, pipeHeight, 0.5f);
            AddConduitPart(view, "Arm" + d.x + d.y, material, dir * 0.25f + new Vector3(0f, y, 0f), scale);
        }
        pipeViews[tile] = view;
    }

    // ---- mana units and the vent (#145) ----

    /// <summary>A tile's mana glow, or null when it shows none.</summary>
    public GameObject GetManaView(int x, int y) =>
        manaViews.TryGetValue(new Vector2Int(x, y), out GameObject view) && view != null && view.activeSelf ? view : null;

    /// <summary>The vent's model (null with no vent, or while its tile is unexplored).</summary>
    public GameObject VentView => ventView != null && ventView.activeSelf ? ventView : null;

    /// <summary>Rebuilds one tile's mana glow: a small glowing orb in the type's color, explored tiles only.</summary>
    public void RefreshMana(Vector2Int tile)
    {
        if (dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return;
        string type = manaFlow != null && IsExplored(tile.x, tile.y) ? manaFlow.TypeAt(tile) : null;
        manaViews.TryGetValue(tile, out GameObject view);
        if (type == null)
        {
            if (view != null) view.SetActive(false);
            if (viewMode == ViewMode.Magic && tileViews != null) RefreshTile(tile.x, tile.y);
            return;
        }
        if (view == null)
        {
            view = new GameObject("Mana");
            view.transform.SetParent(ViewRootOrSelf(), false);
            view.AddComponent<MeshFilter>().sharedMesh = CreatureModel.MeshFor(CreatureModel.Shape.Sphere);
            var meshRenderer = view.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Light, not a body
            manaViews[tile] = view;
        }
        Vector3 center = GetTileCenterWorldPosition(tile.x, tile.y);
        view.transform.position = new Vector3(center.x, manaGlowHeight, center.z);
        view.transform.localScale = Vector3.one * manaGlowSize;
        view.GetComponent<MeshRenderer>().sharedMaterial = CreatureModel.EmissiveMaterialFor(manaFlow.ColorOf(type), manaGlowIntensity, 0.3f);
        view.SetActive(true);
        if (viewMode == ViewMode.Magic && tileViews != null) RefreshTile(tile.x, tile.y);
    }

    /// <summary>Shows the vent on its tile once explored: a cracked mound whose fissure glows while it emits, dull while dormant.</summary>
    public void RefreshVent()
    {
        bool shows = manaFlow != null && manaFlow.HasVent && dungeonBoard != null
            && dungeonBoard.IsInBounds(manaFlow.VentTile.x, manaFlow.VentTile.y) && IsExplored(manaFlow.VentTile.x, manaFlow.VentTile.y);
        if (!shows)
        {
            if (ventView != null) ventView.SetActive(false);
            return;
        }
        if (ventView == null)
            ventView = CreatureModel.Build(ViewRootOrSelf(), "FireVent", PropModel.FireVentRecipe(ventRockColor, ventEmberColor), surfaceSmoothness);
        ventView.transform.position = GetTileCenterWorldPosition(manaFlow.VentTile.x, manaFlow.VentTile.y);
        Transform ember = ventView.transform.Find("Ember");
        if (ember != null)
            ember.GetComponent<MeshRenderer>().sharedMaterial = manaFlow.VentDormant
                ? CreatureModel.MaterialFor(ventDormantEmberColor, surfaceSmoothness)
                : CreatureModel.EmissiveMaterialFor(ventEmberColor, ventEmberIntensity, surfaceSmoothness);
        ventView.SetActive(true);
        if (viewMode == ViewMode.Magic && tileViews != null) RefreshTile(manaFlow.VentTile.x, manaFlow.VentTile.y);
    }

    private void RefreshAllMana()
    {
        ClearManaViews();
        if (manaFlow == null) return;
        foreach (Vector2Int tile in manaFlow.BuiltPipeTiles()) RefreshPipe(tile);
        foreach (Vector2Int tile in manaFlow.PendingPipeTiles()) RefreshPipe(tile);
        foreach (Vector2Int tile in manaFlow.OccupiedTiles()) RefreshMana(tile);
        RefreshVent();
    }

    private void ClearManaViews()
    {
        foreach (GameObject view in pipeViews.Values) if (view != null) DestroyRuntimeObject(view);
        pipeViews.Clear();
        foreach (GameObject view in manaViews.Values) if (view != null) DestroyRuntimeObject(view);
        manaViews.Clear();
        if (ventView != null) DestroyRuntimeObject(ventView);
        ventView = null;
    }


    /// <summary>
    /// A conduit tile's color in the current mode: copper or the pending ghost in Normal; its grid's color or the
    /// neutral ghost in Power.
    /// </summary>
    public Color ConduitDisplayColor(Vector2Int tile, bool built)
    {
        if (viewMode == ViewMode.Power && powerGrid != null)
            return built ? GridColor(powerGrid.GridAt(tile)) : pendingOverlayColor;
        return built ? conduitColor : pendingConduitColor;
    }

    private Material ConduitMaterial(Vector2Int tile, bool built)
    {
        Color color = ConduitDisplayColor(tile, built);
        return built ? MaterialFor(color) : CreatureModel.GhostMaterialFor(color);
    }

    private void HandleNetworkChanged()
    {
        if (viewMode != ViewMode.Normal) RepaintOverlay(); // Grid ids and membership may have moved: repaint at once
    }

    /// <summary>The tile's view object (its box), or null before RenderFullBoard / out of bounds.</summary>
    public GameObject GetTileView(int x, int y) =>
        tileViews != null && dungeonBoard != null && dungeonBoard.IsInBounds(x, y) ? tileViews[x, y] : null;

    /// <summary>The shared lit material for a color: one per distinct color, created on first use.</summary>
    public Material MaterialFor(Color color)
    {
        if (materials.TryGetValue(color, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find(LitShaderName) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        var material = new Material(shader) { name = "Tile " + ColorUtility.ToHtmlStringRGB(color) };
        material.SetColor("_BaseColor", color); // Material colors are sRGB-authored; Unity converts for linear
        material.SetFloat("_Smoothness", surfaceSmoothness);
        // Matte surfaces: no specular highlights or environment (skybox) reflections on a dungeon floor.
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_EnvironmentReflections", 0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        materials[color] = material;
        return material;
    }

    /// <summary>The door built on an Entrance tile (active while the tile is the Entrance), else null.</summary>
    public GameObject GetDoorView(int x, int y) =>
        doors.TryGetValue(new Vector2Int(x, y), out GameObject door) ? door : null;

    private void HandleTileChanged(int x, int y, TileState newState)
    {
        RefreshTile(x, y);
        if (manaFlow != null && manaFlow.IsVentTile(new Vector2Int(x, y))) RefreshVent(); // #145: the vent's tile was carved
    }

    private void HandleTileRevealed(Vector2Int tile)
    {
        RefreshTile(tile.x, tile.y);
        RefreshConduit(tile); // Explored only: a reveal may show conduit that was there all along
        RefreshPipe(tile);    // #145: and pipe, mana and the vent likewise
        RefreshMana(tile);
        if (manaFlow != null && manaFlow.IsVentTile(tile)) RefreshVent();
    }

    private void HandleConduitChanged(Vector2Int tile)
    {
        RefreshConduit(tile);
        foreach (Vector2Int d in ConduitNeighbors) RefreshConduit(tile + d); // Their arms toward this tile change too
    }

    // ---- conduit (#142) ----

    private static readonly Vector2Int[] ConduitNeighbors =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1),
    };

    /// <summary>A tile's conduit view (its inlay or ghost run), or null when it shows none.</summary>
    public GameObject GetConduitView(int x, int y) =>
        conduitViews.TryGetValue(new Vector2Int(x, y), out GameObject view) && view != null && view.activeSelf ? view : null;

    /// <summary>Whether the tile's conduit shows: it has conduit and it's explored.</summary>
    public bool ShowsConduit(Vector2Int tile) => powerGrid != null && powerGrid.HasConduit(tile) && IsExplored(tile.x, tile.y);

    /// <summary>Rebuilds one tile's conduit view from the grid: built inlay, pending ghost, or nothing.</summary>
    public void RefreshConduit(Vector2Int tile)
    {
        if (dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return;
        if (conduitViews.TryGetValue(tile, out GameObject old))
        {
            conduitViews.Remove(tile);
            if (old != null) DestroyRuntimeObject(old);
        }
        if (!ShowsConduit(tile)) return;

        bool built = powerGrid.IsBuilt(tile);
        Material material = ConduitMaterial(tile, built);
        Vector3 center = GetTileCenterWorldPosition(tile.x, tile.y);
        var view = new GameObject(built ? "Conduit" : "ConduitPending");
        view.transform.SetParent(ViewRootOrSelf(), false);
        view.transform.position = new Vector3(center.x, 0f, center.z);

        float y = conduitThickness * 0.5f;
        AddConduitPart(view, "Plate", material, new Vector3(0f, y, 0f), new Vector3(conduitWidth, conduitThickness, conduitWidth));
        foreach (Vector2Int d in ConduitNeighbors)
        {
            if (!powerGrid.HasConduit(tile + d)) continue;
            Vector3 dir = new Vector3(d.x, 0f, d.y);
            Vector3 scale = d.x != 0 ? new Vector3(0.5f, conduitThickness, conduitWidth) : new Vector3(conduitWidth, conduitThickness, 0.5f);
            AddConduitPart(view, $"Arm{d.x}{d.y}", material, dir * 0.25f + new Vector3(0f, y, 0f), scale);
        }
        conduitViews[tile] = view;
    }

    private void RefreshAllConduit()
    {
        ClearConduitViews();
        if (powerGrid == null) return;
        foreach (Vector2Int tile in powerGrid.BuiltTiles()) RefreshConduit(tile);
        foreach (Vector2Int tile in powerGrid.PendingTiles()) RefreshConduit(tile);
    }

    private void ClearConduitViews()
    {
        foreach (GameObject view in conduitViews.Values) if (view != null) DestroyRuntimeObject(view);
        conduitViews.Clear();
    }

    private Transform ViewRootOrSelf() => viewRoot != null ? viewRoot : transform;

    private void AddConduitPart(GameObject view, string partName, Material material, Vector3 localPosition, Vector3 scale)
    {
        var part = new GameObject(partName);
        part.transform.SetParent(view.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
        var meshRenderer = part.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // A flat inlay casts nothing
    }

    private void EnsureViews()
    {
        int width = dungeonBoard.Width;
        int height = dungeonBoard.Height;
        if (tileViews != null && tileViews.GetLength(0) == width && tileViews.GetLength(1) == height) return;

        if (viewRoot == null)
        {
            viewRoot = new GameObject("BoardView").transform;
            viewRoot.SetParent(transform, false);
        }

        tileViews = new GameObject[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                tileViews[x, y] = CreateBox($"Tile ({x}, {y})", viewRoot, rockColor);
            }
        }
    }

    // A minimal door from boxes: two posts and a lintel in the frame color, a plank panel between them.
    private GameObject CreateDoor(Vector3 groundPoint)
    {
        var door = new GameObject("Door");
        door.transform.SetParent(viewRoot, false);
        door.transform.position = groundPoint;

        float halfSpan = 0.5f - doorPostWidth * 0.5f;
        float panelWidth = 1f - doorPostWidth * 2f;
        AddDoorPart(door, "PostLeft", doorFrameColor, new Vector3(-halfSpan, doorHeight * 0.5f, 0f), new Vector3(doorPostWidth, doorHeight, doorPostWidth));
        AddDoorPart(door, "PostRight", doorFrameColor, new Vector3(halfSpan, doorHeight * 0.5f, 0f), new Vector3(doorPostWidth, doorHeight, doorPostWidth));
        AddDoorPart(door, "Lintel", doorFrameColor, new Vector3(0f, doorHeight - doorPostWidth * 0.5f, 0f), new Vector3(1f, doorPostWidth, doorPostWidth));
        float panelHeight = doorHeight - doorPostWidth;
        AddDoorPart(door, "Panel", doorPanelColor, new Vector3(0f, panelHeight * 0.5f, 0f), new Vector3(panelWidth, panelHeight, doorPostWidth * 0.5f));

        // Dressing on the panel's camera-facing side (#77): plank seams, a crossbar, two iron bands, a handle.
        float front = -doorPostWidth * 0.25f;                  // The panel's front face
        Color seam = Color.Lerp(doorPanelColor, doorFrameColor, 0.45f);
        AddDoorPart(door, "SeamLeft", seam, new Vector3(-panelWidth / 6f, panelHeight * 0.5f, front - 0.004f), new Vector3(0.015f, panelHeight * 0.94f, 0.01f));
        AddDoorPart(door, "SeamRight", seam, new Vector3(panelWidth / 6f, panelHeight * 0.5f, front - 0.004f), new Vector3(0.015f, panelHeight * 0.94f, 0.01f));
        AddDoorPart(door, "Crossbar", doorFrameColor, new Vector3(0f, panelHeight * 0.5f, front - 0.012f), new Vector3(panelWidth * 0.96f, 0.06f, 0.025f));
        AddDoorPart(door, "BandTop", doorIronColor, new Vector3(0f, panelHeight * 0.82f, front - 0.009f), new Vector3(panelWidth * 0.98f, 0.035f, 0.018f));
        AddDoorPart(door, "BandBottom", doorIronColor, new Vector3(0f, panelHeight * 0.18f, front - 0.009f), new Vector3(panelWidth * 0.98f, 0.035f, 0.018f));
        GameObject handle = AddDoorPart(door, "Handle", doorIronColor, new Vector3(panelWidth * 0.32f, panelHeight * 0.42f, front - 0.03f), Vector3.one * 0.06f);
        handle.GetComponent<MeshFilter>().sharedMesh = CreatureModel.MeshFor(CreatureModel.Shape.Sphere); // A round knob
        return door;
    }

    private GameObject AddDoorPart(GameObject door, string partName, Color color, Vector3 localPosition, Vector3 scale)
    {
        GameObject part = CreateBox(partName, door.transform, color);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        return part;
    }

    private GameObject CreateBox(string boxName, Transform parent, Color color)
    {
        var go = new GameObject(boxName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = MaterialFor(color);
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
        return go;
    }

    // One unit box centered on the origin, shared by every view: per-face normals for lighting, UVs per face.
    private Mesh BoxMesh()
    {
        if (boxMesh != null) return boxMesh;

        var vertices = new List<Vector3>(24);
        var normals = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var triangles = new List<int>(36);
        AddFace(vertices, normals, uvs, triangles, Vector3.up, Vector3.right, Vector3.forward);
        AddFace(vertices, normals, uvs, triangles, Vector3.down, Vector3.right, Vector3.back);
        AddFace(vertices, normals, uvs, triangles, Vector3.back, Vector3.right, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.forward, Vector3.left, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.right, Vector3.forward, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.left, Vector3.back, Vector3.up);

        boxMesh = new Mesh { name = "TileBox" };
        boxMesh.SetVertices(vertices);
        boxMesh.SetNormals(normals);
        boxMesh.SetUVs(0, uvs);
        boxMesh.SetTriangles(triangles, 0);
        boxMesh.RecalculateBounds();
        boxMesh.RecalculateTangents();
        return boxMesh;
    }

    // One outward face: u x v = -normal, so the two triangles wind clockwise seen from outside (Unity's front face).
    private static void AddFace(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
        Vector3 normal, Vector3 u, Vector3 v)
    {
        int start = vertices.Count;
        Vector3 c = normal * 0.5f;
        vertices.Add(c - u * 0.5f - v * 0.5f);
        vertices.Add(c - u * 0.5f + v * 0.5f);
        vertices.Add(c + u * 0.5f + v * 0.5f);
        vertices.Add(c + u * 0.5f - v * 0.5f);
        for (int i = 0; i < 4; i++) normals.Add(normal);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));

        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ---- mapping (the single source of tile ↔ world) ----

    /// <summary>A tile's center on the ground for code without a renderer: the board origin taken as (0, 0, 0).</summary>
    public static Vector3 UnanchoredTileCenter(Vector2Int tile) => new Vector3(tile.x + 0.5f, 0f, tile.y + 0.5f);

    public Vector3 GetTileWorldPosition(int x, int y) => BoardOrigin + new Vector3(x, 0f, y);

    /// <summary>The ground point (y = 0) at the center of tile (x, y).</summary>
    public Vector3 GetTileCenterWorldPosition(int x, int y) => BoardOrigin + new Vector3(x + 0.5f, 0f, y + 0.5f);

    /// <summary>Exact inverse of the mapping: the tile whose ground cell contains the point (its y is ignored).</summary>
    public Vector2Int WorldToBoardCoords(Vector3 worldPosition)
    {
        Vector3 origin = BoardOrigin;
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x - origin.x),
            Mathf.FloorToInt(worldPosition.z - origin.z)
        );
    }

    /// <summary>
    /// Picks the tile a screen ray actually lands on. A plain ground raycast lands behind a raised block (it
    /// passes over the block's top to y = 0), so this walks the ray from the block-top plane down to the ground
    /// and returns the first point inside a raised tile's column — its top or near face — else the ground hit.
    /// Fogged tiles are raised columns here as they are on screen (#131), so picking lands where the eye does.
    /// The result is a ground-plane point inside the picked tile, ready for WorldToBoardCoords.
    /// </summary>
    public bool RaycastBoard(Ray ray, out Vector3 groundPoint)
    {
        if (!TileHover.RaycastGroundPlane(ray, out groundPoint)) return false;
        if (dungeonBoard == null || rockHeight <= 0f || ray.direction.y >= 0f) return true;

        float tTop = Mathf.Max(0f, (rockHeight - ray.origin.y) / ray.direction.y);
        float tGround = -ray.origin.y / ray.direction.y;
        for (int i = 0; i <= PickSamples; i++)
        {
            Vector3 p = ray.GetPoint(Mathf.Lerp(tTop, tGround, i / (float)PickSamples));
            Vector2Int tile = WorldToBoardCoords(p);
            if (dungeonBoard.IsInBounds(tile.x, tile.y) && RendersRaised(tile.x, tile.y))
            {
                groundPoint = new Vector3(p.x, 0f, p.z);
                return true;
            }
        }
        return true;
    }
}
