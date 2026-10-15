using System;
using UnityEngine;

/// <summary>
/// Fog of war (#131): what the player has seen. A tile is explored once an imp's sight has covered it, and stays
/// explored for the run (nothing un-reveals). Imps are the only revealers: each frame the imp's current tile is
/// compared with the last one revealed from, and a move reveals the Chebyshev disc of impSightRange around it.
/// Presentation only: pathing, AI, heroes and the economy never read it. Consumers (BoardRenderer, DigDesignator,
/// TileHover) treat a missing FogOfWar as "everything explored" (IsSeen), which is the game without fog.
/// </summary>
public class FogOfWar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private Imp imp; // The revealer; a second imp later is another RevealAround caller

    [Header("Sight")]
    [SerializeField] private int impSightRange = 3; // Chebyshev tiles around the imp's tile

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public int ImpSightRange { get => impSightRange; set => impSightRange = value; }

    /// <summary>
    /// Raised once per newly explored tile, after it's recorded. BoardRenderer restyles that tile from it, the same
    /// per-tile flow as DungeonBoard.OnTileChanged.
    /// </summary>
    public event Action<Vector2Int> TileRevealed;

    private bool[,] explored; // Sized from the board on first use; runtime state only
    private Vector2Int? lastRevealCenter;

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>();
    }

    // The first Update runs after every Start, so the imp has spawned on the cavern centre by then: the starter
    // disc is revealed before the first frame renders. Later moves reveal as CurrentTile changes.
    private void Update()
    {
        RevealFromImp();
    }

    /// <summary>
    /// Reveals around the imp when its tile differs from the last one revealed from. Called from Update; public
    /// so tests can step it deterministically.
    /// </summary>
    public void RevealFromImp()
    {
        if (imp == null) return;

        Vector2Int tile = imp.CurrentTile;
        if (lastRevealCenter == tile) return;
        lastRevealCenter = tile;
        RevealAround(tile);
    }

    /// <summary>True when the tile has been seen. Out of bounds (or no board) reads as unexplored.</summary>
    public bool IsExplored(Vector2Int tile) =>
        EnsureExplored() && tile.x >= 0 && tile.y >= 0 && tile.x < explored.GetLength(0) && tile.y < explored.GetLength(1)
        && explored[tile.x, tile.y];

    /// <summary>
    /// The consumers' rule: a tile is seen when there's no fog at all (no FogOfWar wired, as in today's game and in
    /// staging scenes) or when it's explored.
    /// </summary>
    public static bool IsSeen(FogOfWar fog, Vector2Int tile) => fog == null || fog.IsExplored(tile);

    /// <summary>
    /// Explores every tile within impSightRange (Chebyshev) of center, clipped to the board, and returns how many
    /// were newly explored. Already-explored tiles stay explored and don't count.
    /// </summary>
    public int RevealAround(Vector2Int center)
    {
        if (!EnsureExplored()) return 0;

        RectInt disc = SightRect(center, impSightRange, explored.GetLength(0), explored.GetLength(1));
        int revealed = 0;
        for (int x = disc.xMin; x < disc.xMax; x++)
        {
            for (int y = disc.yMin; y < disc.yMax; y++)
            {
                if (explored[x, y]) continue;
                explored[x, y] = true;
                revealed++;
                TileRevealed?.Invoke(new Vector2Int(x, y));
            }
        }
        return revealed;
    }

    /// <summary>How many tiles are explored (tests and probes).</summary>
    public int ExploredCount
    {
        get
        {
            if (!EnsureExplored()) return 0;
            int count = 0;
            foreach (bool seen in explored) if (seen) count++;
            return count;
        }
    }

    /// <summary>
    /// The Chebyshev disc of radius range around center as a rect, clipped to a width × height board (empty when
    /// the disc misses the board). Pure: the reveal's geometry, callable without a scene.
    /// </summary>
    public static RectInt SightRect(Vector2Int center, int range, int width, int height)
    {
        int r = Mathf.Max(0, range);
        int xMin = Mathf.Max(0, center.x - r);
        int yMin = Mathf.Max(0, center.y - r);
        int xMax = Mathf.Min(width, center.x + r + 1);
        int yMax = Mathf.Min(height, center.y + r + 1);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    private bool EnsureExplored()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>();
        if (dungeonBoard == null) return false;

        explored ??= new bool[dungeonBoard.Width, dungeonBoard.Height];
        return true;
    }
}
