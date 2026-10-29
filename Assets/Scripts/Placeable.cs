using UnityEngine;

public enum PlaceableType
{
    LairCot,
    MushroomPlot,
    SpikeTrap,
    ImpWheel,         // #142: appended (never reorder: serialized rows reference these by value)
    FurnaceGenerator, // #143: appended
    GrowLamp,         // #143: appended
    Accumulator,      // #145: appended
    ManaContainer,    // #145: appended
    CombinationChamber, // #146: appended
    Warren,             // #157: appended
    Wall,               // #165: appended
    Door                // #165: appended
}

/// <summary>
/// A placed item occupying one board tile. Placement never changes the tile's TileState:
/// the tile stays Floor and walkable; occupancy lives in PlacementManager's registry.
/// Its look is a code-built model (ticket #77): a cot, a mushroom mound, a trap's base plate, (#142) an imp wheel, or
/// (#143) a furnace with its chimney or a lamp on a post, (#145) an accumulator's basin or a mana container's tank, or
/// (#146) a combination chamber's vessel, or (#157) a warren's den, or (#165) a wall's stone block or a door in its frame.
/// Placement is an order (#91): it starts as an inert construction site (a materials pile) until
/// CompleteConstruction, which the builder calls (story #92; tests and staging call it directly).
/// Orders cost materials (#104): an order records its requirement and waits UNFUNDED — showing an order marker (a
/// translucent ghost of the finished model on a ground outline), no pile — until PlacementManager's funding pass
/// pays for it; then the marker goes, the pile arrives, and it's the #91 site from there.
/// </summary>
[DisallowMultipleComponent]
public class Placeable : MonoBehaviour
{
    [Header("Model (code-built, ticket #77)")]
    [SerializeField] private Color cotFrameColor = new Color(0.42f, 0.27f, 0.15f, 1f);  // Wood
    [SerializeField] private Color cotPillowColor = new Color(0.92f, 0.90f, 0.85f, 1f); // Off-white
    [SerializeField] private Color plotSoilColor = new Color(0.26f, 0.17f, 0.10f, 1f);  // Dark earth
    [SerializeField] private Color plotStemColor = new Color(0.90f, 0.87f, 0.78f, 1f);  // Pale stems
    [SerializeField] private Color wheelFrameColor = new Color(0.30f, 0.20f, 0.12f, 1f); // #142: dark timber stand
    [SerializeField] private Color wheelIronColor = new Color(0.25f, 0.25f, 0.27f, 1f);  // #142: hub and axle
    [SerializeField] private Color furnaceIronColor = new Color(0.18f, 0.18f, 0.20f, 1f); // #143: door, bands and chimney
    [SerializeField] private Color furnaceFireColor = new Color(1.00f, 0.50f, 0.15f, 1f); // #143: the firebox glow
    [SerializeField] private Color lampPostColor = new Color(0.22f, 0.22f, 0.24f, 1f);    // #143: iron post and cap
    [SerializeField] private Color accumulatorStoneColor = new Color(0.40f, 0.38f, 0.44f, 1f); // #145: the basin and prongs
    [SerializeField] private Color containerBandColor = new Color(0.22f, 0.22f, 0.26f, 1f);    // #145: iron bands, plinth, lid
    [SerializeField] private Color containerGlassColor = new Color(0.70f, 0.55f, 1.00f, 1f);   // #145: the gauge
    [SerializeField] private Color chamberBandColor = new Color(0.24f, 0.22f, 0.28f, 1f);      // #146: plinth, collar, ports
    [SerializeField] private Color chamberCoreColor = new Color(0.85f, 0.95f, 1.00f, 1f);      // #146: the core crystal
    [SerializeField] private Color warrenStoneColor = new Color(0.46f, 0.44f, 0.42f, 1f);      // #157: the doorway stones
    [SerializeField] private Color warrenDoorColor = new Color(0.10f, 0.07f, 0.05f, 1f);       // #157: the dark way in
    [SerializeField] private Color warrenBannerColor = new Color(0.62f, 0.78f, 0.30f, 1f);     // #157: a goblin-green rag on a stick
    [SerializeField] private Color wallCapColor = new Color(0.30f, 0.29f, 0.31f, 1f);          // #165: the wall's darker cap and course
    [SerializeField] private Color doorStoneColor = new Color(0.42f, 0.40f, 0.40f, 1f);        // #165: jambs and lintel
    [SerializeField] private Color doorIronColor = new Color(0.20f, 0.20f, 0.22f, 1f);         // #165: the leaf's bands
    [SerializeField] private float modelSmoothness = 0.2f;                              // A little sheen, less than creatures

    [Header("Construction Site (#91)")]
    [SerializeField] private Color crateColor = new Color(0.55f, 0.38f, 0.22f, 1f);     // Lighter, fresh wood
    [SerializeField] private Color sackColor = new Color(0.72f, 0.62f, 0.44f, 1f);      // Burlap

    [Header("Construction Animation (#93)")]
    [SerializeField] private float revealFirstThirdAt = 1f / 3f;  // Progress at which the first third of the parts show
    [SerializeField] private float revealSecondThirdAt = 2f / 3f; // ...two thirds
    [SerializeField] private float revealAllAt = 0.95f;           // ...all of them, just before completion
    [SerializeField] private float pileMinScale = 0.25f;          // The pile shrinks toward this as progress reaches 1
    [SerializeField] private float completionBounceScale = 1.12f; // Pop overshoot when the build completes
    [SerializeField] private float completionBounceSeconds = 0.3f;

    [Header("Order Marker (#104)")]
    [SerializeField] private Color orderMarkerTint = new Color(0.45f, 0.80f, 1.00f, 1f); // Blueprint cyan: the outline always; the ghost when the material has no color
    [SerializeField] private float orderGhostTintAmount = 0.6f; // How far the ghost's part colors pull toward its tint (the material's color, #109)
    [SerializeField] private float orderGhostAlpha = 0.35f;     // The ghost of what will stand here
    [SerializeField] private float orderOutlineAlpha = 0.85f;   // The ground outline: the order's footprint, readable flat
    [SerializeField] private float orderOutlineThickness = 0.05f;

    public PlaceableType Type { get; private set; }
    public Vector2Int Tile { get; private set; }

    /// <summary>Lair Cot rest-spot claim (used by goblins in Epic #4). Always false for other types.</summary>
    public bool IsClaimed { get; private set; }

    /// <summary>
    /// False while a construction site: a materials pile, totally inert (no claim, no growth or food, never armed).
    /// Only CompleteConstruction makes it true.
    /// </summary>
    public bool IsBuilt { get; private set; }

    /// <summary>#165: raised once when the site completes (PlacementManager re-raises it as PlaceablesChanged).</summary>
    public event System.Action<Placeable> Constructed;

    /// <summary>Construction progress 0–1, for the builder (#92). Storage only: reaching 1 doesn't complete the site.</summary>
    public float BuildProgress
    {
        get => buildProgress;
        set => buildProgress = IsBuilt ? 1f : Mathf.Clamp01(value);
    }

    /// <summary>The code-built finished model (cot / mound / plate); null before Initialize, inactive while a site.</summary>
    public GameObject Model { get; private set; }

    /// <summary>The construction site's materials pile; null once built, inactive while unfunded.</summary>
    public GameObject MaterialsPile { get; private set; }

    /// <summary>The unfunded order's marker (ghost + outline); null once funded or built.</summary>
    public GameObject OrderMarker { get; private set; }

    /// <summary>
    /// The material this placeable was ordered in (#109) — what its order costs, and once built what it's made of. Set at
    /// placement and kept after construction (later stories read material properties through it).
    /// </summary>
    public MaterialType OrderedMaterial { get; private set; }

    /// <summary>How much of OrderedMaterial the order costs (#104). Paid in full at funding.</summary>
    public int RequiredAmount { get; private set; }

    /// <summary>The order marker ghost's current body tint (#109): the ordered material's color, or the cyan fallback.</summary>
    public Color OrderGhostTint { get; private set; }

    /// <summary>The marker's "ordered" blueprint color: the outline always, and the ghost body's fallback tint.</summary>
    public Color OrderMarkerFallbackTint => orderMarkerTint;

    private Color primaryColor; // The type's color the models were built with (kept to rebuild the marker's ghost)

    /// <summary>
    /// False while the order waits for materials (#104): no pile, never a build job. PlacementManager's funding pass
    /// (or an unwired stockpile, which funds at once) sets it; never unset.
    /// </summary>
    public bool IsFunded { get; private set; }

    /// <summary>Placement order (#104): the funding pass serves each material's waiting orders strictly in this order (#120).</summary>
    public long OrderIndex { get; private set; }

    /// <summary>How many of the finished model's parts (recipe order) the assembly currently shows.</summary>
    public int RevealedPartCount { get; private set; }

    /// <summary>True while the completion pop plays on the model.</summary>
    public bool IsBouncing => bounceElapsed >= 0f;

    private float buildProgress;
    private float syncedProgress = -1f; // BuildProgress the visuals last matched
    private float bounceElapsed = -1f;  // Game seconds into the completion pop; < 0 when not playing

    /// <summary>The footprint size the model was built at, in tiles.</summary>
    public float Size { get; private set; }

    /// <summary>Height of the model's top surface: where pips (plot) and spikes (trap) sit.</summary>
    public float SurfaceHeight => Type == PlaceableType.LairCot ? PropModel.CotSurfaceHeight
        : Type == PlaceableType.MushroomPlot ? PropModel.MoundHeight
        : PropModel.TrapPlateHeight;

    /// <summary>
    /// Sets type and tile and builds the model. Called once by PlacementManager on creation, so every placeable
    /// starts as a construction site: the finished model is built but inactive, a materials pile shows instead.
    /// color is the type's primary color: the cot's mattress, the plot's mushroom caps, the trap's plate.
    /// </summary>
    public void Initialize(PlaceableType type, Vector2Int tile, Color color, float size)
    {
        Type = type;
        Tile = tile;
        Size = size;
        IsBuilt = false;
        IsFunded = false;
        buildProgress = 0f;
        primaryColor = color;
        OrderGhostTint = orderMarkerTint;
        CreateModel(color, size);
        Model.SetActive(false);
        CreateMaterialsPile(color, size);
        MaterialsPile.SetActive(false); // The materials haven't arrived: funding brings them (#104)
        CreateOrderMarker(color, size);
    }

    /// <summary>Stamps the order's place in line and its cost (#104). PlacementManager calls it right after Initialize.</summary>
    public void SetOrder(long orderIndex, MaterialType material, int amount)
    {
        OrderIndex = orderIndex;
        OrderedMaterial = material;
        RequiredAmount = Mathf.Max(0, amount);
    }

    /// <summary>
    /// The marker's ghost body takes this tint (#109: the ordered material's catalog color, chosen by MarkerTintFor);
    /// the outline stays the blueprint "ordered" color. No-op once the marker is gone (funded or built).
    /// </summary>
    public void TintOrderMarker(Color tint)
    {
        OrderGhostTint = tint;
        if (OrderMarker == null) return;
        DestroyOrderMarker();
        CreateOrderMarker(primaryColor, Size);
    }

    /// <summary>
    /// The ghost tint for an order in this material (#109): the stockpile catalog's color for it, else the fallback
    /// (#104's blueprint cyan) when there's no stockpile or no definition.
    /// </summary>
    public static Color MarkerTintFor(Stockpile stockpile, MaterialType material, Color fallback) =>
        stockpile != null && stockpile.TryGetColor(material, out Color color) ? color : fallback;

    /// <summary>
    /// The order is paid for (#104): the marker goes and the materials pile arrives — from here it's #91's site.
    /// Called by PlacementManager's funding pass after the stockpile spend succeeds. Idempotent.
    /// </summary>
    public void MarkFunded()
    {
        if (IsFunded) return;
        IsFunded = true;
        DestroyOrderMarker();
        if (!IsBuilt && MaterialsPile != null) MaterialsPile.SetActive(true);
    }

    /// <summary>
    /// Finishes the site: the pile goes, the finished model shows, and the type starts working (the trap arms,
    /// the plot starts growing from empty). The only way into the built state; idempotent.
    /// </summary>
    public void CompleteConstruction()
    {
        if (IsBuilt) return;
        IsBuilt = true;
        buildProgress = 1f;
        DestroyOrderMarker(); // Staging may complete an order that was never funded

        if (MaterialsPile != null)
        {
            if (Application.isPlaying) Destroy(MaterialsPile);
            else DestroyImmediate(MaterialsPile);
            MaterialsPile = null;
        }
        if (Model != null)
        {
            // Every part in, whatever stage the assembly reached, then the pop.
            foreach (Transform part in Model.transform) part.gameObject.SetActive(true);
            RevealedPartCount = Model.transform.childCount;
            Model.SetActive(true);
            bounceElapsed = 0f;
            Model.transform.localScale = Vector3.one * CompletionBounce(0f, completionBounceSeconds, completionBounceScale);
        }

        if (TryGetComponent(out SpikeTrap trap)) trap.OnConstructed();
        if (TryGetComponent(out MushroomPlot plot)) plot.OnConstructed();
        if (TryGetComponent(out PowerNode node)) node.OnConstructed(); // #142: a node joins its grid once built
        if (TryGetComponent(out FurnaceGenerator furnace)) furnace.OnConstructed(); // #143: lights at once if there's fuel
        if (TryGetComponent(out Accumulator accumulator)) accumulator.OnConstructed(); // #145: joins the mana service
        if (TryGetComponent(out ManaContainer container)) container.OnConstructed();
        if (TryGetComponent(out CombinationChamber chamber)) chamber.OnConstructed(); // #146
        if (TryGetComponent(out Partition partition)) partition.OnConstructed();     // #165: the wall or door goes on the board
        Constructed?.Invoke(this);
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Per-frame visuals (game time: pause freezes them). While a site: re-syncs the assembly whenever
    /// BuildProgress has changed since the last sync. Once built: plays the completion pop. Public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsBuilt)
        {
            if (buildProgress != syncedProgress) SyncConstructionVisuals();
            return;
        }
        if (bounceElapsed < 0f || deltaTime <= 0f) return;

        bounceElapsed += deltaTime;
        if (Model != null)
            Model.transform.localScale = Vector3.one * CompletionBounce(bounceElapsed, completionBounceSeconds, completionBounceScale);
        if (bounceElapsed >= completionBounceSeconds) bounceElapsed = -1f; // Landed exactly on 1
    }

    /// <summary>
    /// Makes the site's visuals match BuildProgress (#93): the finished model's parts appear in thirds by recipe
    /// order and the materials pile shrinks toward pileMinScale. At progress 0 it's #91's site exactly (model
    /// inactive, pile full size). Idempotent; does nothing once built (completion shows the whole model).
    /// </summary>
    public void SyncConstructionVisuals()
    {
        if (IsBuilt) return;
        syncedProgress = buildProgress;

        if (Model != null)
        {
            Transform root = Model.transform;
            int shown = RevealedParts(buildProgress, root.childCount, revealFirstThirdAt, revealSecondThirdAt, revealAllAt);
            for (int i = 0; i < root.childCount; i++) root.GetChild(i).gameObject.SetActive(i < shown);
            Model.SetActive(shown > 0);
            RevealedPartCount = shown;
        }
        if (MaterialsPile != null) MaterialsPile.transform.localScale = Vector3.one * PileScale(buildProgress, pileMinScale);
    }

    /// <summary>
    /// Parts shown at a progress: none below firstAt, a third (rounded up) from firstAt, two thirds from secondAt,
    /// all from allAt. Piecewise thirds, not a per-part drip.
    /// </summary>
    public static int RevealedParts(float progress, int partCount, float firstAt, float secondAt, float allAt)
    {
        if (progress >= allAt) return partCount;
        if (progress >= secondAt) return (partCount * 2 + 2) / 3;
        if (progress >= firstAt) return (partCount + 2) / 3;
        return 0;
    }

    /// <summary>The materials pile's scale at a progress: full at 0, easing linearly to minScale at 1.</summary>
    public static float PileScale(float progress, float minScale) => Mathf.Lerp(1f, minScale, Mathf.Clamp01(progress));

    /// <summary>
    /// The completion pop: starts at overshoot, rings down (a damped half-wave dip below 1), and lands exactly on
    /// 1 at seconds and stays there.
    /// </summary>
    public static float CompletionBounce(float elapsed, float seconds, float overshoot)
    {
        if (seconds <= 0f || elapsed >= seconds) return 1f;
        float u = Mathf.Clamp01(elapsed / seconds);
        float decay = (1f - u) * (1f - u);
        return 1f + (overshoot - 1f) * decay * Mathf.Cos(u * Mathf.PI * 2.5f);
    }

    /// <summary>Claims a built Lair Cot. Fails if this isn't a cot, it's still a site, or it's already claimed.</summary>
    public bool TryClaim()
    {
        if (Type != PlaceableType.LairCot || !IsBuilt || IsClaimed) return false;
        IsClaimed = true;
        return true;
    }

    /// <summary>Releases a claim. No-op when unclaimed.</summary>
    public void Release()
    {
        IsClaimed = false;
    }

    private CreatureModel.Part[] FinishedRecipe(Color color, float size) =>
        Type == PlaceableType.LairCot ? PropModel.LairCotRecipe(size, cotFrameColor, color, cotPillowColor)
        : Type == PlaceableType.MushroomPlot ? PropModel.MushroomPlotRecipe(size, plotSoilColor, plotStemColor, color)
        : Type == PlaceableType.ImpWheel ? PropModel.ImpWheelRecipe(size, wheelFrameColor, color, wheelIronColor)
        : Type == PlaceableType.FurnaceGenerator ? PropModel.FurnaceRecipe(size, color, furnaceIronColor, furnaceFireColor)
        : Type == PlaceableType.GrowLamp ? PropModel.GrowLampRecipe(size, lampPostColor, color)
        : Type == PlaceableType.Accumulator ? PropModel.AccumulatorRecipe(size, accumulatorStoneColor, color)
        : Type == PlaceableType.ManaContainer ? PropModel.ManaContainerRecipe(size, color, containerBandColor, containerGlassColor)
        : Type == PlaceableType.CombinationChamber ? PropModel.CombinationChamberRecipe(size, color, chamberBandColor, chamberCoreColor)
        : Type == PlaceableType.Warren ? PropModel.WarrenRecipe(size, color, warrenStoneColor, warrenDoorColor, cotFrameColor, warrenBannerColor)
        : Type == PlaceableType.Wall ? PropModel.WallRecipe(color, wallCapColor)            // #165: fills its tile
        : Type == PlaceableType.Door ? PropModel.DoorRecipe(doorStoneColor, color, doorIronColor)
        : PropModel.SpikeTrapPlateRecipe(size, color);

    private void CreateModel(Color color, float size)
    {
        if (Model != null) return;
        Model = CreatureModel.Build(transform, Type + "Model", FinishedRecipe(color, size), modelSmoothness);
    }

    // The order marker: a translucent ghost of the finished model (what will stand here, tinted by what it's ordered in)
    // on an outline of its footprint (so even a flat trap plate reads at gameplay zoom) in the blueprint "ordered" tint.
    private void CreateOrderMarker(Color color, float size)
    {
        if (OrderMarker != null) return;
        OrderMarker = CreatureModel.BuildGhost(transform, Type + "Order", FinishedRecipe(color, size),
            OrderGhostTint, orderGhostTintAmount, orderGhostAlpha);
        Color outline = orderMarkerTint;
        outline.a = orderOutlineAlpha;
        GameObject frame = CreatureModel.BuildGhost(OrderMarker.transform, "Outline",
            PropModel.OrderOutlineRecipe(size, orderOutlineThickness, outline), outline, 0f, orderOutlineAlpha);
        frame.name = "Outline";
    }

    // ---- see-through (#144: the power overlay shows conduit under non-power placeables) ----

    // Each swapped renderer's own material and shadow mode, restored exactly when the see-through look ends.
    private readonly System.Collections.Generic.Dictionary<Renderer, (Material material, UnityEngine.Rendering.ShadowCastingMode shadows)>
        seeThroughOriginals = new System.Collections.Generic.Dictionary<Renderer, (Material, UnityEngine.Rendering.ShadowCastingMode)>();

    /// <summary>True while the model is drawn see-through.</summary>
    public bool IsSeeThrough { get; private set; }

    /// <summary>
    /// Draws every part (model, pile, marker, pips) as a translucent copy of its own color at opacity alpha, casting no
    /// shadow, or puts every part back exactly. Idempotent, and incremental while on: a repeat call also catches parts
    /// created since (a pile arriving at funding), so the caller can simply re-apply.
    /// </summary>
    public void SetSeeThrough(bool on, float alpha)
    {
        if (!on)
        {
            if (!IsSeeThrough) return;
            foreach (var entry in seeThroughOriginals)
            {
                if (entry.Key == null) continue; // Destroyed meanwhile (the pile at completion)
                entry.Key.sharedMaterial = entry.Value.material;
                entry.Key.shadowCastingMode = entry.Value.shadows;
            }
            seeThroughOriginals.Clear();
            IsSeeThrough = false;
            return;
        }

        IsSeeThrough = true;
        foreach (MeshRenderer part in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (seeThroughOriginals.ContainsKey(part)) continue;
            seeThroughOriginals[part] = (part.sharedMaterial, part.shadowCastingMode);
            part.sharedMaterial = CreatureModel.GhostMaterialFor(SeeThroughColor(part.sharedMaterial, alpha));
            part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // A shadow would darken the conduit too
        }
    }

    /// <summary>A part's see-through color: its own base color, at no more than alpha (white with no material).</summary>
    public static Color SeeThroughColor(Material material, float alpha)
    {
        Color color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
        color.a = Mathf.Min(color.a, Mathf.Clamp01(alpha));
        return color;
    }

    private void DestroyOrderMarker()
    {
        if (OrderMarker == null) return;
        if (Application.isPlaying) Destroy(OrderMarker);
        else DestroyImmediate(OrderMarker);
        OrderMarker = null;
    }

    private void CreateMaterialsPile(Color color, float size)
    {
        if (MaterialsPile != null) return;

        CreatureModel.Part[] recipe =
            Type == PlaceableType.LairCot ? PropModel.CotMaterialsRecipe(size, cotFrameColor, crateColor, color)
            : Type == PlaceableType.MushroomPlot ? PropModel.PlotMaterialsRecipe(size, plotSoilColor, sackColor, color)
            : Type == PlaceableType.ImpWheel ? PropModel.WheelMaterialsRecipe(size, wheelFrameColor, crateColor, color)
            : Type == PlaceableType.FurnaceGenerator ? PropModel.FurnaceMaterialsRecipe(size, color, furnaceIronColor, crateColor)
            : Type == PlaceableType.GrowLamp ? PropModel.LampMaterialsRecipe(size, lampPostColor, color, crateColor)
            : Type == PlaceableType.Accumulator ? PropModel.AccumulatorMaterialsRecipe(size, accumulatorStoneColor, color, crateColor)
            : Type == PlaceableType.ManaContainer ? PropModel.ManaContainerMaterialsRecipe(size, color, containerBandColor, crateColor)
            : Type == PlaceableType.CombinationChamber ? PropModel.CombinationChamberMaterialsRecipe(size, color, chamberBandColor, chamberCoreColor, crateColor)
            : Type == PlaceableType.Warren ? PropModel.WarrenMaterialsRecipe(size, color, warrenStoneColor, sackColor, crateColor)
            : Type == PlaceableType.Wall ? PropModel.WallMaterialsRecipe(size, color, crateColor)     // #165
            : Type == PlaceableType.Door ? PropModel.DoorMaterialsRecipe(size, doorStoneColor, color, crateColor)
            : PropModel.TrapMaterialsRecipe(size, color, Color.Lerp(color, Color.white, 0.45f), crateColor);
        MaterialsPile = CreatureModel.Build(transform, Type + "Materials", recipe, modelSmoothness);
    }
}
