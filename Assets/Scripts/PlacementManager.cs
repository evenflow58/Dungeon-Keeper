using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of placed items, one per tile. Placement is instant and free (DESIGN §A.4):
/// a target must be Floor, unoccupied, and reachable by the imp. TileState is never changed.
/// </summary>
[DisallowMultipleComponent]
public class PlacementManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;

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

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public float MushroomGrowthSecondsPerFood { get => mushroomGrowthSecondsPerFood; set => mushroomGrowthSecondsPerFood = value; }
    public int MushroomCapacity { get => mushroomCapacity; set => mushroomCapacity = value; }
    public int SpikeDamage { get => spikeDamage; set => spikeDamage = value; }

    public int Count => placed.Count;

    /// <summary>Food stored across all Mushroom Plots (the top bar's source).</summary>
    public int TotalFood
    {
        get
        {
            int total = 0;
            foreach (Placeable p in placed.Values)
            {
                if (p.Type != PlaceableType.MushroomPlot) continue;
                // TryGetComponent rather than ?. on a UnityEngine.Object (?. skips Unity's null check, which
                // matters for destroyed objects). A plot without the component contributes 0.
                if (p.TryGetComponent(out MushroomPlot plot)) total += plot.FoodCount;
            }
            return total;
        }
    }

    private readonly Dictionary<Vector2Int, Placeable> placed = new Dictionary<Vector2Int, Placeable>();
    private Transform holder;

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= GetComponent<BoardRenderer>() ?? FindAnyObjectByType<BoardRenderer>();
    }

    public bool IsOccupied(Vector2Int tile) => placed.ContainsKey(tile);

    public Placeable GetAt(Vector2Int tile) => placed.TryGetValue(tile, out Placeable p) ? p : null;

    public int CountOfType(PlaceableType type)
    {
        int n = 0;
        foreach (Placeable p in placed.Values)
            if (p.Type == type) n++;
        return n;
    }

    /// <summary>
    /// All placed Spike Traps (Armed and Spent), sorted by tile in board scan order (x ascending, then
    /// y ascending) so work selection is deterministic regardless of the registry's iteration order.
    /// </summary>
    public List<SpikeTrap> GetSpikeTraps()
    {
        var traps = new List<SpikeTrap>();
        foreach (Placeable p in placed.Values)
        {
            if (p.Type == PlaceableType.SpikeTrap && p.TryGetComponent(out SpikeTrap trap)) traps.Add(trap);
        }
        traps.Sort((a, b) => CompareScanOrder(a.Tile, b.Tile));
        return traps;
    }

    /// <summary>All placed Mushroom Plots (stocked or empty), sorted by tile in board scan order.</summary>
    public List<MushroomPlot> GetMushroomPlots()
    {
        var plots = new List<MushroomPlot>();
        foreach (Placeable p in placed.Values)
        {
            if (p.Type == PlaceableType.MushroomPlot && p.TryGetComponent(out MushroomPlot plot)) plots.Add(plot);
        }
        plots.Sort((a, b) => CompareScanOrder(a.Tile, b.Tile));
        return plots;
    }

    /// <summary>All placed Lair Cots (claimed and free), sorted by tile in board scan order.</summary>
    public List<Placeable> GetCots()
    {
        var cots = new List<Placeable>();
        foreach (Placeable p in placed.Values)
        {
            if (p.Type == PlaceableType.LairCot) cots.Add(p);
        }
        cots.Sort((a, b) => CompareScanOrder(a.Tile, b.Tile));
        return cots;
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

    public bool TryPlace(PlaceableType type, Vector2Int tile, Vector2Int fromTile, out Placeable placeable)
    {
        placeable = null;
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
        return true;
    }

    /// <summary>
    /// Places on every valid tile in the rect (invalid tiles are skipped). Returns how many were placed.
    /// </summary>
    public int ApplyRect(PlaceableType type, RectInt rect, Vector2Int fromTile)
    {
        int count = 0;
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                if (TryPlace(type, new Vector2Int(x, y), fromTile, out _)) count++;
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
