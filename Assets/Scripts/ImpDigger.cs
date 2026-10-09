using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The imp's work loop, with three job kinds: digging, building, and trap rearming. It picks the nearest
/// reachable work item (a Designated tile, a construction site, or a Spent Spike Trap), walks to a Floor tile
/// next to it, works it (digSecondsPerTile to convert to Floor; the site type's build time, read from
/// PlacementManager.BuildSecondsFor, to complete a site; rearmSecondsPerTrap to re-arm), and repeats.
/// Build progress lives on the site (Placeable.BuildProgress), so an interrupted build resumes where it stopped.
/// Work with no reachable stand-point is left as it is and skipped.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Imp))]
public class ImpDigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private Imp imp;
    [SerializeField] private PlacementManager placementManager; // Source of traps, sites, and build times; null = dig-only
    [SerializeField] private Stockpile stockpile;               // Where completed digs pay their yield (#103); null = no pay

    [Header("Digging")]
    [SerializeField] private float digSecondsPerTile = 2.5f;
    [SerializeField] private int stoneYieldPerTile = 1;         // Material paid per completed dig (#103), whichever material the tile is (#114)
    [SerializeField] private int glimmerstoneBandRows = 6;      // #114: rows nearest the entrance (depth < this) are glimmerstone
    [SerializeField] private float idleRecheckSeconds = 0.25f; // How often an idle imp rescans for work

    [Header("Rearming")]
    [SerializeField] private float rearmSecondsPerTrap = 8f;

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public float DigSecondsPerTile { get => digSecondsPerTile; set => digSecondsPerTile = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public int StoneYieldPerTile { get => stoneYieldPerTile; set => stoneYieldPerTile = value; }
    public int GlimmerstoneBandRows { get => glimmerstoneBandRows; set => glimmerstoneBandRows = value; }
    public float IdleRecheckSeconds { get => idleRecheckSeconds; set => idleRecheckSeconds = value; }
    public float RearmSecondsPerTrap { get => rearmSecondsPerTrap; set => rearmSecondsPerTrap = value; }

    /// <summary>The Designated tile being dug, or null when not on a dig job.</summary>
    public Vector2Int? CurrentTarget { get; private set; }

    /// <summary>The Spent trap being rearmed, or null when not on a rearm job. Never set alongside another job.</summary>
    public SpikeTrap CurrentTrap { get; private set; }

    /// <summary>The construction site being built, or null when not on a build job. Never set alongside another job.</summary>
    public Placeable CurrentSite { get; private set; }

    /// <summary>The Floor tile the imp stands on to work the current job (meaningful only while it has one).</summary>
    public Vector2Int StandPoint { get; private set; }

    public bool IsDigging { get; private set; }
    public float DigElapsed { get; private set; }
    public float DigProgress => digSecondsPerTile > 0f ? Mathf.Clamp01(DigElapsed / digSecondsPerTile) : 1f;

    public bool IsRearming { get; private set; }
    public float RearmElapsed { get; private set; }
    public float RearmProgress => rearmSecondsPerTrap > 0f ? Mathf.Clamp01(RearmElapsed / rearmSecondsPerTrap) : 1f;

    /// <summary>True while stationary beside CurrentSite, writing progress onto it.</summary>
    public bool IsBuilding { get; private set; }

    private bool HasJob => CurrentTarget != null || CurrentTrap != null || CurrentSite != null;

    // True after a state transition (start, completed job, dropped job): select on the next Tick
    // instead of waiting for the idle recheck interval.
    private bool selectPending = true;
    private float idleTimer;

    private void Start()
    {
        imp ??= GetComponent<Imp>();
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        stockpile ??= FindAnyObjectByType<Stockpile>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances the work loop by deltaTime. Called from Update; public so tests can step
    /// it deterministically alongside Imp.Advance.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (dungeonBoard == null || imp == null) return;

        if (!HasJob)
            TickIdle(deltaTime);
        else if (IsDigging)
            TickDigging(deltaTime);
        else if (IsBuilding)
            TickBuilding(deltaTime);
        else if (IsRearming)
            TickRearming(deltaTime);
        else
            TickTraveling();
    }

    private void TickIdle(float deltaTime)
    {
        if (!selectPending)
        {
            idleTimer += deltaTime;
            if (idleTimer < idleRecheckSeconds) return;
        }
        idleTimer = 0f;
        selectPending = false;

        Vector2Int target, standPoint;
        SpikeTrap trap = null;
        Placeable site = null;
        bool found = placementManager != null
            ? TrySelectWork(dungeonBoard, placementManager.GetConstructionSites(), placementManager.GetBuiltSpikeTraps(),
                imp.CurrentTile, out target, out standPoint, out site, out trap)
            : TrySelectTarget(dungeonBoard, imp.CurrentTile, out target, out standPoint); // No manager: dig-only
        if (!found) return;

        // Commit to this job; selection doesn't re-run while traveling.
        if (!imp.SetDestination(standPoint))
        {
            selectPending = true; // Board changed between selection and dispatch
            return;
        }
        if (site != null) CurrentSite = site;
        else if (trap != null) CurrentTrap = trap;
        else CurrentTarget = target;
        StandPoint = standPoint;
    }

    private void TickTraveling()
    {
        if (imp.IsMoving) return;

        if (imp.CurrentTile != StandPoint || !IsJobStillNeeded())
        {
            DropTarget(); // Designation cleared / trap re-armed en route, or the imp ended up elsewhere
            return;
        }
        if (CurrentSite != null)
            IsBuilding = true; // Resumes from the site's own BuildProgress, never from zero
        else if (CurrentTrap != null)
        {
            IsRearming = true;
            RearmElapsed = 0f;
        }
        else
        {
            IsDigging = true;
            DigElapsed = 0f;
        }
    }

    private void TickDigging(float deltaTime)
    {
        if (!IsStillDesignated() || !IsAdjacent(imp.CurrentTile, CurrentTarget.Value))
        {
            DropTarget(); // Abandon: no conversion
            return;
        }

        DigElapsed += deltaTime;
        if (DigElapsed < digSecondsPerTile) return;

        Vector2Int dug = CurrentTarget.Value;
        bool pays = TryGetTileYield(dug, out MaterialType material, out int amount); // Asked before the tile changes
        DropTarget();
        dungeonBoard.SetTile(dug.x, dug.y, TileState.Floor);
        if (pays && stockpile != null) stockpile.Add(material, amount); // The only place digging earns (#103)
    }

    /// <summary>
    /// What digging out this tile yields (#103): the one seam the dig pipeline asks at conversion. The material is
    /// the tile's band by depth (#114: glimmerstone near the entrance, stone elsewhere); the amount is
    /// stoneYieldPerTile of whichever material it is. False when the tile yields nothing.
    /// </summary>
    public bool TryGetTileYield(Vector2Int tile, out MaterialType material, out int amount)
    {
        material = dungeonBoard != null ? MaterialAtDepth(DepthOf(dungeonBoard, tile), glimmerstoneBandRows) : MaterialType.Stone;
        amount = stoneYieldPerTile;
        return amount > 0;
    }

    /// <summary>
    /// A tile's depth on the entrance axis (#114): 0 on the door's row (the board's south edge, y = height − 1),
    /// growing toward y = 0.
    /// </summary>
    public static int DepthOf(DungeonBoard board, Vector2Int tile) => board.Height - 1 - tile.y;

    /// <summary>
    /// The material a tile at this depth is made of (#114): the bands along the entrance axis, first match wins, else
    /// stone. Today one band: glimmerstone for depth &lt; glimmerstoneBandRows. A deep band (#111's deepstone) slots
    /// in here as another check against the far end of the same axis.
    /// </summary>
    public static MaterialType MaterialAtDepth(int depth, int glimmerstoneBandRows)
    {
        if (depth >= 0 && depth < glimmerstoneBandRows) return MaterialType.Glimmerstone;
        return MaterialType.Stone;
    }

    private void TickBuilding(float deltaTime)
    {
        if (!IsJobStillNeeded() || !IsAdjacent(imp.CurrentTile, CurrentSite.Tile))
        {
            DropTarget(); // Built by other means, gone, or the imp is no longer beside it: progress stays on the site
            return;
        }

        float seconds = placementManager != null ? placementManager.BuildSecondsFor(CurrentSite.Type) : 0f;
        CurrentSite.BuildProgress += seconds > 0f ? deltaTime / seconds : 1f;
        if (CurrentSite.BuildProgress < 1f - BuildCompleteTolerance) return;

        Placeable built = CurrentSite;
        DropTarget();
        built.CompleteConstruction(); // #91's seam: the only door into the built state
    }

    // Progress is a sum of deltaTime / seconds steps; absorb float rounding so a build lands on its full duration.
    private const float BuildCompleteTolerance = 1e-4f;

    private void TickRearming(float deltaTime)
    {
        if (!IsJobStillNeeded() || !IsAdjacent(imp.CurrentTile, CurrentTrap.Tile))
        {
            DropTarget(); // Armed by other means mid-channel (or the trap is gone): no action
            return;
        }

        RearmElapsed += deltaTime;
        if (RearmElapsed < rearmSecondsPerTrap) return;

        SpikeTrap rearmed = CurrentTrap;
        DropTarget();
        rearmed.Rearm();
    }

    /// <summary>Clears whichever job is held (dig, build, or rearm) and asks for an immediate reselect.</summary>
    private void DropTarget()
    {
        CurrentTarget = null;
        CurrentTrap = null;
        CurrentSite = null;
        IsBuilding = false;
        IsDigging = false;
        DigElapsed = 0f;
        IsRearming = false;
        RearmElapsed = 0f;
        selectPending = true;
    }

    // A build job is needed while its site exists unbuilt; a rearm job while its trap exists and is Spent
    // (!= null is Unity's destroyed-object check); a dig while its tile is still Designated.
    private bool IsJobStillNeeded() =>
        CurrentSite != null ? !CurrentSite.IsBuilt
        : CurrentTrap != null ? !CurrentTrap.IsArmed
        : CurrentTarget != null && IsStillDesignated();

    private bool IsStillDesignated()
    {
        Vector2Int t = CurrentTarget.Value;
        return dungeonBoard.GetTile(t.x, t.y) == TileState.Designated;
    }

    private static bool IsAdjacent(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    /// <summary>
    /// Picks the Designated tile with the shortest path to a stand-point (a Floor tile next to it),
    /// scanning x-ascending then y-ascending; ties keep the earlier tile in that scan order.
    /// Tiles with no reachable stand-point are skipped and left as they are.
    /// standPoint is the last tile of Pathfinder.FindPathToAdjacent's path.
    /// </summary>
    public static bool TrySelectTarget(DungeonBoard board, Vector2Int fromTile, out Vector2Int target, out Vector2Int standPoint) =>
        SelectNearestDig(board, fromTile, out target, out standPoint) != int.MaxValue;

    /// <summary>
    /// Unified dig + rearm selection: nearest work wins across both kinds, by path length to a stand-point.
    /// Dig candidates are evaluated first, exactly as TrySelectTarget does; then Spent traps in list order,
    /// each replacing the best only when strictly nearer — so an exact tie goes to the dig.
    /// Armed traps aren't work; unreachable work of either kind is skipped and left as it is.
    /// A trap wins → trap is set and targetTile is its tile; a dig wins → trap is null.
    /// The stand-point is always a Floor tile next to the target: a trap tile is itself Floor, so traps use
    /// FindPathToNeighbor (never onto the tile) — the imp never stands on a trap to rearm it.
    /// </summary>
    public static bool TrySelectWork(DungeonBoard board, IReadOnlyList<SpikeTrap> traps, Vector2Int fromTile,
        out Vector2Int targetTile, out Vector2Int standPoint, out SpikeTrap trap) =>
        TrySelectWork(board, null, traps, fromTile, out targetTile, out standPoint, out _, out trap);

    /// <summary>
    /// Unified dig + build + rearm selection (#92): nearest work wins across all three kinds, by path length to a
    /// stand-point. Kinds are evaluated in order dig, build, rearm, each candidate replacing the best only when
    /// strictly nearer, so exact ties break dig &gt; build &gt; rearm, then scan order within a kind.
    /// Build candidates are unbuilt sites; like traps, a site's tile is Floor, so the imp stands BESIDE it
    /// (FindPathToNeighbor, never onto it). Built placeables aren't build work; unreachable work is skipped.
    /// Exactly one outcome is set: site (targetTile = its tile), trap (targetTile = its tile), or a dig
    /// (site and trap null).
    /// </summary>
    public static bool TrySelectWork(DungeonBoard board, IReadOnlyList<Placeable> sites, IReadOnlyList<SpikeTrap> traps,
        Vector2Int fromTile, out Vector2Int targetTile, out Vector2Int standPoint, out Placeable site, out SpikeTrap trap)
    {
        trap = null;
        site = null;
        int bestCost = SelectNearestDig(board, fromTile, out targetTile, out standPoint);

        if (sites != null)
        {
            foreach (Placeable candidate in sites)
            {
                // Only funded sites are build work: an order awaiting materials is never travelled to (#104).
                if (candidate == null || candidate.IsBuilt || !candidate.IsFunded) continue;

                List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, candidate.Tile);
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

                bestCost = path.Count;
                site = candidate;
                targetTile = candidate.Tile;
                standPoint = path[path.Count - 1];
            }
        }

        if (traps != null)
        {
            foreach (SpikeTrap candidate in traps)
            {
                // Armed traps aren't work, and a construction site isn't a spent trap (#91): never rearm one.
                if (candidate == null || !candidate.IsBuilt || candidate.IsArmed) continue;

                List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, candidate.Tile);
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

                bestCost = path.Count;
                site = null; // A strictly nearer rearm beats the best build
                trap = candidate;
                targetTile = candidate.Tile;
                standPoint = path[path.Count - 1];
            }
        }

        return bestCost != int.MaxValue;
    }

    /// <summary>The dig scan shared by both selectors. Returns the winning path cost, or int.MaxValue for none.</summary>
    private static int SelectNearestDig(DungeonBoard board, Vector2Int fromTile, out Vector2Int target, out Vector2Int standPoint)
    {
        target = default;
        standPoint = default;
        int bestCost = int.MaxValue;

        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                if (board.GetTile(x, y) != TileState.Designated) continue;

                List<Vector2Int> path = Pathfinder.FindPathToAdjacent(board, fromTile, new Vector2Int(x, y));
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

                bestCost = path.Count;
                target = new Vector2Int(x, y);
                standPoint = path[path.Count - 1];
            }
        }

        return bestCost;
    }
}
