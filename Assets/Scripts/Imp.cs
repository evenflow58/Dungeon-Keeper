using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Imp : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;

    [Header("Movement")]
    [SerializeField] private float moveSpeedTilesPerSecond = 4f;

    [Header("Model (code-built, ticket #76)")]
    [SerializeField] private float modelHeight = 0.6f;                                   // Tiles: the smallest creature
    [SerializeField] private CreatureModel.ImpPalette palette = CreatureModel.ImpPalette.Default; // Red skin, dark horns, leather, pickaxe (#84)
    [SerializeField] private float modelSmoothness = 0.35f;                              // Toy (vinyl) sheen
    [SerializeField] private MotionTuning motion = MotionTuning.Imp();                   // Walk, idle, dig swing (#87)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public float MoveSpeed { get => moveSpeedTilesPerSecond; set => moveSpeedTilesPerSecond = value; }

    /// <summary>The board tile whose cell contains the imp (its ground point).</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>The waypoint the imp is currently walking toward; equals CurrentTile when idle.</summary>
    public Vector2Int NextTile { get; private set; }

    public bool IsMoving => path.Count > 0;

    // Waypoints still to visit; path[0] is the tile being walked toward (NextTile).
    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private Vector2Int segmentStart;
    private GameObject model;
    private CreatureMotion poser;
    private ImpDigger digger;

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();

        CreateModel();
        Spawn();
    }

    private void Update()
    {
        Advance(Time.deltaTime);
        Pose(Time.deltaTime);
    }

    /// <summary>
    /// Places the imp on the starter cavern's center tile and clears any orders.
    /// </summary>
    public void Spawn()
    {
        if (dungeonBoard == null) return;

        PlaceOnTile(GetCavernCenterTile(dungeonBoard.Width, dungeonBoard.Height, dungeonBoard.CavernSize));
    }

    /// <summary>
    /// Snaps the imp to the given tile's center and clears any orders.
    /// </summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        path.Clear();
        CurrentTile = tile;
        NextTile = tile;
        segmentStart = tile;
        transform.position = TileCenter(tile);
    }

    /// <summary>
    /// Paths to the given tile and starts walking it. Mid-move, the new path starts from
    /// the waypoint the imp is already heading toward (so it never backtracks to a tile
    /// center it has left). If no path exists the request is ignored: an idle imp stays
    /// put, a moving imp keeps its current orders. Returns whether a path was found.
    /// </summary>
    public bool SetDestination(Vector2Int tile)
    {
        if (dungeonBoard == null) return false;

        // Between tile centers, the imp is committed to the waypoint ahead; otherwise it
        // stands on segmentStart's center (which is CurrentTile).
        bool betweenTiles = IsMoving && transform.position != TileCenter(segmentStart);
        Vector2Int origin = betweenTiles ? NextTile : segmentStart;

        List<Vector2Int> newPath = Pathfinder.FindPath(dungeonBoard, origin, tile);
        if (newPath.Count == 0) return false;

        path.Clear();
        if (betweenTiles)
        {
            // Finish the current segment first: newPath[0] is the waypoint ahead.
            path.AddRange(newPath);
        }
        else
        {
            // newPath[0] is the tile the imp is already standing on.
            path.AddRange(newPath.GetRange(1, newPath.Count - 1));
            CurrentTile = origin;
        }

        NextTile = path.Count > 0 ? path[0] : CurrentTile;
        return true;
    }

    /// <summary>
    /// Moves the imp along its path by deltaTime seconds' worth of travel.
    /// Called from Update; public so tests can step movement deterministically.
    /// </summary>
    public void Advance(float deltaTime)
    {
        float remaining = moveSpeedTilesPerSecond * deltaTime; // In tiles (= world units, 1 unit per tile)

        while (path.Count > 0 && remaining > 0f)
        {
            Vector3 target = TileCenter(path[0]);
            float distance = Vector3.Distance(transform.position, target);

            if (distance <= remaining)
            {
                // Reach this waypoint exactly, then carry leftover travel into the next one.
                transform.position = target;
                remaining -= distance;
                segmentStart = path[0];
                CurrentTile = path[0];
                path.RemoveAt(0);
                NextTile = path.Count > 0 ? path[0] : CurrentTile;
            }
            else
            {
                transform.position = Vector3.MoveTowards(transform.position, target, remaining);
                // Mid-segment, its cell is whichever segment end it's closer to.
                CurrentTile = distance - remaining < 0.5f * SegmentLength() ? path[0] : segmentStart;
                remaining = 0f;
            }
        }
    }

    /// <summary>
    /// The starter cavern's center tile, matching DungeonBoard's centered carve.
    /// For even cavern sizes this is the upper-right of the four middle tiles.
    /// </summary>
    public static Vector2Int GetCavernCenterTile(int width, int height, int cavernSize)
    {
        int startX = (width - cavernSize) / 2;
        int startY = (height - cavernSize) / 2;
        return new Vector2Int(startX + cavernSize / 2, startY + cavernSize / 2);
    }

    private float SegmentLength() =>
        Vector3.Distance(TileCenter(segmentStart), TileCenter(NextTile));

    // The tile's ground point; the root (and its model's base) stands there.
    private Vector3 TileCenter(Vector2Int tile) =>
        boardRenderer != null
            ? boardRenderer.GetTileCenterWorldPosition(tile.x, tile.y)
            : BoardRenderer.UnanchoredTileCenter(tile);

    /// <summary>The code-built model (null until CreateModel, which Start calls).</summary>
    public GameObject Model => model;

    /// <summary>Builds the 3D model once, standing on the ground point (its base at the root's y = 0).</summary>
    public void CreateModel()
    {
        if (model != null) return;
        model = CreatureModel.Build(transform, "ImpModel", CreatureModel.ImpRecipe(modelHeight, palette), modelSmoothness);
        poser = new CreatureMotion(transform, model, motion);
    }

    /// <summary>The pose driver (null until CreateModel).</summary>
    public CreatureMotion Motion => poser;

    /// <summary>
    /// Steps the pose driver by deltaTime (game time). Called from Update after Advance; public so tests can step
    /// it. The pickaxe swings while ImpDigger is digging or rearming. No-op without a model.
    /// </summary>
    public void Pose(float deltaTime)
    {
        if (poser == null) return;
        if (digger == null) TryGetComponent(out digger);
        bool working = digger != null && (digger.IsDigging || digger.IsRearming);
        poser.Step(deltaTime, new CreatureMotion.Flags { Digging = working, FaceTarget = working ? WorkPoint() : null });
    }

    // The tile being worked (#94): the trap being rearmed, else the rock being dug. Unity null check on the trap.
    private Vector3? WorkPoint()
    {
        if (digger.CurrentTrap != null) return digger.CurrentTrap.transform.position;
        return digger.CurrentTarget.HasValue ? TileCenter(digger.CurrentTarget.Value) : (Vector3?)null;
    }
}
