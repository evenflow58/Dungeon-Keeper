using UnityEngine;

public enum PlaceableType
{
    LairCot,
    MushroomPlot,
    SpikeTrap
}

/// <summary>
/// A placed item occupying one board tile. Placement never changes the tile's TileState:
/// the tile stays Floor and walkable; occupancy lives in PlacementManager's registry.
/// Its look is a code-built model (ticket #77): a cot, a mushroom mound, or a trap's base plate.
/// Placement is an order (#91): it starts as an inert construction site (a materials pile) until
/// CompleteConstruction, which the builder calls (story #92; tests and staging call it directly).
/// </summary>
[DisallowMultipleComponent]
public class Placeable : MonoBehaviour
{
    [Header("Model (code-built, ticket #77)")]
    [SerializeField] private Color cotFrameColor = new Color(0.42f, 0.27f, 0.15f, 1f);  // Wood
    [SerializeField] private Color cotPillowColor = new Color(0.92f, 0.90f, 0.85f, 1f); // Off-white
    [SerializeField] private Color plotSoilColor = new Color(0.26f, 0.17f, 0.10f, 1f);  // Dark earth
    [SerializeField] private Color plotStemColor = new Color(0.90f, 0.87f, 0.78f, 1f);  // Pale stems
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

    public PlaceableType Type { get; private set; }
    public Vector2Int Tile { get; private set; }

    /// <summary>Lair Cot rest-spot claim (used by goblins in Epic #4). Always false for other types.</summary>
    public bool IsClaimed { get; private set; }

    /// <summary>
    /// False while a construction site: a materials pile, totally inert (no claim, no growth or food, never armed).
    /// Only CompleteConstruction makes it true.
    /// </summary>
    public bool IsBuilt { get; private set; }

    /// <summary>Construction progress 0–1, for the builder (#92). Storage only: reaching 1 doesn't complete the site.</summary>
    public float BuildProgress
    {
        get => buildProgress;
        set => buildProgress = IsBuilt ? 1f : Mathf.Clamp01(value);
    }

    /// <summary>The code-built finished model (cot / mound / plate); null before Initialize, inactive while a site.</summary>
    public GameObject Model { get; private set; }

    /// <summary>The construction site's materials pile; null once built.</summary>
    public GameObject MaterialsPile { get; private set; }

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
        buildProgress = 0f;
        CreateModel(color, size);
        Model.SetActive(false);
        CreateMaterialsPile(color, size);
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

    private void CreateModel(Color color, float size)
    {
        if (Model != null) return;

        CreatureModel.Part[] recipe =
            Type == PlaceableType.LairCot ? PropModel.LairCotRecipe(size, cotFrameColor, color, cotPillowColor)
            : Type == PlaceableType.MushroomPlot ? PropModel.MushroomPlotRecipe(size, plotSoilColor, plotStemColor, color)
            : PropModel.SpikeTrapPlateRecipe(size, color);
        Model = CreatureModel.Build(transform, Type + "Model", recipe, modelSmoothness);
    }

    private void CreateMaterialsPile(Color color, float size)
    {
        if (MaterialsPile != null) return;

        CreatureModel.Part[] recipe =
            Type == PlaceableType.LairCot ? PropModel.CotMaterialsRecipe(size, cotFrameColor, crateColor, color)
            : Type == PlaceableType.MushroomPlot ? PropModel.PlotMaterialsRecipe(size, plotSoilColor, sackColor, color)
            : PropModel.TrapMaterialsRecipe(size, color, Color.Lerp(color, Color.white, 0.45f), crateColor);
        MaterialsPile = CreatureModel.Build(transform, Type + "Materials", recipe, modelSmoothness);
    }
}
