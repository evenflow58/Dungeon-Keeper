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

    public PlaceableType Type { get; private set; }
    public Vector2Int Tile { get; private set; }

    /// <summary>Lair Cot rest-spot claim (used by goblins in Epic #4). Always false for other types.</summary>
    public bool IsClaimed { get; private set; }

    /// <summary>The code-built base model (cot / mound / plate); null before Initialize.</summary>
    public GameObject Model { get; private set; }

    /// <summary>The footprint size the model was built at, in tiles.</summary>
    public float Size { get; private set; }

    /// <summary>Height of the model's top surface: where pips (plot) and spikes (trap) sit.</summary>
    public float SurfaceHeight => Type == PlaceableType.LairCot ? PropModel.CotSurfaceHeight
        : Type == PlaceableType.MushroomPlot ? PropModel.MoundHeight
        : PropModel.TrapPlateHeight;

    /// <summary>
    /// Sets type and tile and builds the model. Called once by PlacementManager on creation.
    /// color is the type's primary color: the cot's mattress, the plot's mushroom caps, the trap's plate.
    /// </summary>
    public void Initialize(PlaceableType type, Vector2Int tile, Color color, float size)
    {
        Type = type;
        Tile = tile;
        Size = size;
        CreateModel(color, size);
    }

    /// <summary>Claims a Lair Cot. Fails if this isn't a cot or it's already claimed.</summary>
    public bool TryClaim()
    {
        if (Type != PlaceableType.LairCot || IsClaimed) return false;
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
}
