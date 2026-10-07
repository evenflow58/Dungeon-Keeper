using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hero's body (DESIGN §A.6): tile movement with Goblin's API and semantics, plus hit-point death.
/// No needs clock. Decisions live in HeroAI; the HeroSpawner configures the Health (team Hero, max HP).
/// </summary>
[DisallowMultipleComponent]
public class Hero : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Health health; // Optional: a hero with no Health never dies

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // Tiles per second (provisional Fighter stat)

    [Header("Model (code-built, ticket #76)")]
    [SerializeField] private float modelHeight = 0.85f;                             // Tiles: the tallest creature
    [SerializeField] private CreatureModel.HeroPalette palette = CreatureModel.HeroPalette.Default; // Pale steel, blue heraldry, dark steel (#84)
    [SerializeField] private float modelSmoothness = 0.35f;                         // Toy (vinyl) sheen

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Health Health { get => health; set => health = value; }
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }

    public bool IsDead { get; private set; }

    /// <summary>The board tile whose cell contains the hero (its ground point).</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>The waypoint the hero is currently walking toward; equals CurrentTile when idle.</summary>
    public Vector2Int NextTile { get; private set; }

    public bool IsMoving => path.Count > 0;

    // Waypoints still to visit; path[0] is the tile being walked toward (NextTile).
    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private Vector2Int segmentStart;
    private GameObject model;

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        health ??= GetComponent<Health>();

        CreateModel();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Dies if its Health has run out, else moves. Called from Update; public so tests can step it.
    /// No-op once dead.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (IsDead) return;
        if (health != null && health.IsDead)
        {
            Die();
            return;
        }

        Advance(deltaTime);
    }

    /// <summary>Snaps the hero to the given tile's center and clears any orders.</summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        path.Clear();
        CurrentTile = tile;
        NextTile = tile;
        segmentStart = tile;
        transform.position = TileCenter(tile);
    }

    /// <summary>
    /// Paths to the given tile and starts walking it, with Goblin.SetDestination's semantics: mid-move the
    /// new path starts from the waypoint ahead; with no path the request is ignored (current orders kept).
    /// Returns whether a path was found. Always false once dead.
    /// </summary>
    public bool SetDestination(Vector2Int tile)
    {
        if (dungeonBoard == null || IsDead) return false;

        // Between tile centers, the hero is committed to the waypoint ahead; otherwise it
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
            // newPath[0] is the tile the hero is already standing on.
            path.AddRange(newPath.GetRange(1, newPath.Count - 1));
            CurrentTile = origin;
        }

        NextTile = path.Count > 0 ? path[0] : CurrentTile;
        return true;
    }

    /// <summary>
    /// Moves the hero along its path by deltaTime seconds' worth of travel. Tick calls it; public so tests
    /// can step movement alone. No-op once dead.
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (IsDead) return;

        float remaining = moveSpeed * deltaTime; // In tiles (= world units, 1 unit per tile)

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

    /// <summary>The waypoints still ahead (NextTile first); empty when idle.</summary>
    public IReadOnlyList<Vector2Int> RemainingPath => path;

    private void Die()
    {
        IsDead = true;
        path.Clear();
        NextTile = CurrentTile;
        gameObject.SetActive(false); // Slice placeholder for death: no corpse system
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
        model = CreatureModel.Build(transform, "HeroModel", CreatureModel.HeroRecipe(modelHeight, palette), modelSmoothness);
    }
}
