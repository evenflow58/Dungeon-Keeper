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

    private float buildProgress;

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
        if (Model != null) Model.SetActive(true);

        if (TryGetComponent(out SpikeTrap trap)) trap.OnConstructed();
        if (TryGetComponent(out MushroomPlot plot)) plot.OnConstructed();
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
