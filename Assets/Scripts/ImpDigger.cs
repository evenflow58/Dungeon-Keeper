using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The imp's work loop, with three job kinds: digging, building, and trap rearming. It picks the nearest
/// reachable work item (a Designated tile, a construction site, or a Spent Spike Trap), walks to a Floor tile
/// next to it, works it (digSecondsPerTile to convert to Floor; the site type's build time, read from
/// PlacementManager.BuildSecondsFor, to complete a site; rearmSecondsPerTrap to re-arm), and repeats.
/// Build progress lives on the site (Placeable.BuildProgress), so an interrupted build resumes where it stopped.
/// Work with no reachable stand-point is left as it is and skipped.
/// Power (#142): pending conduit tiles are construction-family work beside sites (the imp stands next to the tile and
/// wires it over PowerGrid.WireSecondsPerTile, progress kept on the tile). Operate is the lowest job: only when there is
/// no dig, construction or rearm work does the imp walk to a built Imp Wheel and tend it. Operating is never committed:
/// every idle-recheck interval the imp re-runs selection and leaves the wheel the moment real work exists (Evan, Oct 17,
/// 2026), and the wheel's output stops with it.
/// Mana (#145): pending pipe tiles are construction-family work too, right after conduit (ties: dig &gt; sites &gt;
/// conduit &gt; pipe &gt; rearm), built beside the tile over ManaFlow.PipeSecondsPerTile with progress kept on the tile.
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
    [SerializeField] private PowerGrid powerGrid;               // #142: pending conduit to build; null = no wiring work
    [SerializeField] private ManaFlow manaFlow;                 // #145: pending pipe to build; null = no pipe work

    [Header("Digging")]
    [SerializeField] private float digSecondsPerTile = 2.5f;
    [SerializeField] private int stoneYieldPerTile = 1;         // Material paid per completed dig (#103), whichever material the tile is (#114)
    [SerializeField] private int glimmerstoneBandRows = 6;      // #114: rows nearest the entrance (depth < this) are glimmerstone
    [SerializeField] private int deepstoneDepthThreshold = 20;  // #111: rows this deep or deeper (depth >= this) are deepstone
    [SerializeField] private float idleRecheckSeconds = 0.25f; // How often an idle imp rescans for work

    [Header("Rearming")]
    [SerializeField] private float rearmSecondsPerTrap = 8f;

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public float DigSecondsPerTile { get => digSecondsPerTile; set => digSecondsPerTile = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public PowerGrid PowerGrid { get => powerGrid; set => powerGrid = value; }
    public ManaFlow ManaFlow { get => manaFlow; set => manaFlow = value; }
    public int StoneYieldPerTile { get => stoneYieldPerTile; set => stoneYieldPerTile = value; }
    public int GlimmerstoneBandRows { get => glimmerstoneBandRows; set => glimmerstoneBandRows = value; }
    public int DeepstoneDepthThreshold { get => deepstoneDepthThreshold; set => deepstoneDepthThreshold = value; }
    public float IdleRecheckSeconds { get => idleRecheckSeconds; set => idleRecheckSeconds = value; }
    public float RearmSecondsPerTrap { get => rearmSecondsPerTrap; set => rearmSecondsPerTrap = value; }

    /// <summary>The Designated tile being dug, or null when not on a dig job.</summary>
    public Vector2Int? CurrentTarget { get; private set; }

    /// <summary>The Spent trap being rearmed, or null when not on a rearm job. Never set alongside another job.</summary>
    public SpikeTrap CurrentTrap { get; private set; }

    /// <summary>The construction site being built, or null when not on a build job. Never set alongside another job.</summary>
    public Placeable CurrentSite { get; private set; }

    /// <summary>The pending conduit tile being wired (#142), or null when not on a wiring job.</summary>
    public Vector2Int? CurrentConduit { get; private set; }

    /// <summary>True while stationary beside CurrentConduit, adding wire progress to it (#142).</summary>
    public bool IsWiring { get; private set; }

    /// <summary>The pending pipe tile being built (#145), or null when not on a pipe job.</summary>
    public Vector2Int? CurrentPipe { get; private set; }

    /// <summary>True while stationary beside CurrentPipe, adding pipe progress to it (#145).</summary>
    public bool IsPiping { get; private set; }

    /// <summary>
    /// The wheel the imp is walking to or operating (#142), or null. Never set alongside another job: real work takes the
    /// imp away from it.
    /// </summary>
    public PowerNode CurrentWheel { get; private set; }

    /// <summary>True while standing beside CurrentWheel, tending it: the wheel produces exactly while this holds (#142).</summary>
    public bool IsOperating { get; private set; }

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

    private bool HasJob => CurrentTarget != null || CurrentTrap != null || CurrentSite != null || CurrentConduit != null
        || CurrentPipe != null;

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
        powerGrid ??= FindAnyObjectByType<PowerGrid>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();
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

        if (CurrentWheel != null)
            TickOperating(deltaTime);
        else if (!HasJob)
            TickIdle(deltaTime);
        else if (IsDigging)
            TickDigging(deltaTime);
        else if (IsBuilding)
            TickBuilding(deltaTime);
        else if (IsWiring)
            TickWiring(deltaTime);
        else if (IsPiping)
            TickPiping(deltaTime);
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

        if (!TryDispatchWork()) TryDispatchOperate(); // Operate only when there's no real work (#142)
    }

    // Selects the nearest real work (dig, construction, rearm) and sets off for it. True when work was found, even if the
    // dispatch failed (then it reselects next tick).
    private bool TryDispatchWork()
    {
        if (!TrySelectRealWork(out Vector2Int target, out Vector2Int standPoint, out Placeable site, out bool conduit, out bool pipe,
                out SpikeTrap trap))
            return false;

        // Commit to this job; selection doesn't re-run while traveling.
        if (!imp.SetDestination(standPoint))
        {
            selectPending = true; // Board changed between selection and dispatch
            return true;
        }
        if (site != null) CurrentSite = site;
        else if (trap != null) CurrentTrap = trap;
        else if (conduit) CurrentConduit = target;
        else if (pipe) CurrentPipe = target;
        else CurrentTarget = target;
        StandPoint = standPoint;
        return true;
    }

    private bool TrySelectRealWork(out Vector2Int target, out Vector2Int standPoint, out Placeable site, out bool conduit,
        out bool pipe, out SpikeTrap trap)
    {
        site = null;
        trap = null;
        conduit = false;
        pipe = false;
        if (placementManager == null && powerGrid == null && manaFlow == null)
            return TrySelectTarget(dungeonBoard, imp.CurrentTile, out target, out standPoint); // No manager: dig-only
        return TrySelectWork(dungeonBoard, placementManager != null ? placementManager.GetConstructionSites() : null,
            powerGrid != null ? powerGrid.PendingTiles() : null,
            manaFlow != null ? manaFlow.PendingPipeTiles() : null,
            placementManager != null ? placementManager.GetBuiltSpikeTraps() : null,
            imp.CurrentTile, out target, out standPoint, out site, out conduit, out pipe, out trap);
    }

    // The bottom job (#142): walk to the nearest free built wheel and tend it.
    private void TryDispatchOperate()
    {
        if (placementManager == null) return;
        if (!TrySelectOperate(dungeonBoard, placementManager.GetBuiltWheels(), this, imp.CurrentTile,
                out PowerNode wheel, out Vector2Int standPoint))
            return;
        if (!imp.SetDestination(standPoint))
        {
            selectPending = true;
            return;
        }
        CurrentWheel = wheel;
        StandPoint = standPoint;
    }

    // Walking to or tending a wheel. Never committed: on the idle rhythm, real work takes the imp away at once.
    private void TickOperating(float deltaTime)
    {
        if (CurrentWheel == null || !CurrentWheel.IsBuilt || !CurrentWheel.Enabled
            || (CurrentWheel.Operator != null && CurrentWheel.Operator != this))
        {
            DropTarget(); // Gone, switched off (#143), or another imp tends it
            return;
        }

        idleTimer += deltaTime;
        if (selectPending || idleTimer >= idleRecheckSeconds)
        {
            idleTimer = 0f;
            selectPending = false;
            if (TrySelectRealWork(out _, out _, out _, out _, out _, out _))
            {
                DropTarget();   // Leave the wheel: its output stops now
                TickIdle(0f);   // ...and set off for the work this same tick
                return;
            }
        }

        if (IsOperating || imp.IsMoving) return;
        if (imp.CurrentTile != StandPoint || !IsAdjacent(imp.CurrentTile, CurrentWheel.Tile))
        {
            DropTarget();
            return;
        }
        IsOperating = true;
        CurrentWheel.SetOperator(this);
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
        else if (CurrentConduit != null)
            IsWiring = true;   // Resumes from the tile's own progress (#142)
        else if (CurrentPipe != null)
            IsPiping = true;   // Likewise for pipe (#145)
        else if (CurrentTrap != null)
        {
            IsRearming = true;
            RearmElapsed = 0f;
            CurrentTrap.ImpRearming = true; // #142: the trap's powered self-rearm pauses while the imp channels
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
    /// the tile's band by depth (#114: glimmerstone near the entrance; #111: deepstone far from it; stone between); the amount is
    /// stoneYieldPerTile of whichever material it is. False when the tile yields nothing.
    /// </summary>
    public bool TryGetTileYield(Vector2Int tile, out MaterialType material, out int amount)
    {
        material = MaterialOf(tile);
        amount = stoneYieldPerTile;
        return amount > 0;
    }

    /// <summary>
    /// The material a tile is made of: its band by depth, from this digger's serialized band values (stone without a
    /// board). The one answer to "what is this rock" (#132): TryGetTileYield pays it, and the board view and the
    /// hover tooltip show it (TileIdentity), so what you see is what a dig yields.
    /// </summary>
    public MaterialType MaterialOf(Vector2Int tile) =>
        dungeonBoard != null ? MaterialAtDepth(DepthOf(dungeonBoard, tile), glimmerstoneBandRows, deepstoneDepthThreshold)
            : MaterialType.Stone;

    /// <summary>
    /// A tile's depth on the entrance axis (#114): 0 on the door's row (the board's south edge, y = height − 1),
    /// growing toward y = 0.
    /// </summary>
    public static int DepthOf(DungeonBoard board, Vector2Int tile) => board.Height - 1 - tile.y;

    /// <summary>
    /// The material a tile at this depth is made of (#114): the bands along the entrance axis, first match wins, else
    /// stone. The deep band (#111): deepstone for depth ≥ deepstoneDepthThreshold, checked first so it outranks the
    /// shallow band if a configuration ever overlaps them. The shallow band: glimmerstone for
    /// 0 ≤ depth &lt; glimmerstoneBandRows.
    /// </summary>
    public static MaterialType MaterialAtDepth(int depth, int glimmerstoneBandRows, int deepstoneDepthThreshold)
    {
        if (depth >= deepstoneDepthThreshold) return MaterialType.Deepstone;
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

    private void TickWiring(float deltaTime)
    {
        if (!IsJobStillNeeded() || !IsAdjacent(imp.CurrentTile, CurrentConduit.Value))
        {
            DropTarget(); // Removed, or the imp is no longer beside it: progress stays on the tile
            return;
        }
        if (powerGrid.AddWireProgress(CurrentConduit.Value, deltaTime)) DropTarget(); // Built: the grid dirties itself
    }

    private void TickPiping(float deltaTime)
    {
        if (!IsJobStillNeeded() || !IsAdjacent(imp.CurrentTile, CurrentPipe.Value))
        {
            DropTarget(); // Removed, or the imp is no longer beside it: progress stays on the tile
            return;
        }
        if (manaFlow.AddPipeProgress(CurrentPipe.Value, deltaTime)) DropTarget(); // Built: the network dirties itself
    }

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
        if (CurrentTrap != null) CurrentTrap.ImpRearming = false;
        if (CurrentWheel != null) CurrentWheel.ClearOperator(this);
        CurrentTarget = null;
        CurrentTrap = null;
        CurrentSite = null;
        CurrentConduit = null;
        CurrentPipe = null;
        CurrentWheel = null;
        IsOperating = false;
        IsWiring = false;
        IsPiping = false;
        IsBuilding = false;
        IsDigging = false;
        DigElapsed = 0f;
        IsRearming = false;
        RearmElapsed = 0f;
        selectPending = true;
    }

    // A build job is needed while its site exists unbuilt; a rearm job while its trap exists and is Spent
    // (!= null is Unity's destroyed-object check) and, until the channel starts, while the powered self-rearm doesn't
    // own it (#142: power arriving en route hands the trap to it; once channelling, the imp finishes); a wiring job while
    // its tile is still pending; a dig while its tile is still Designated.
    private bool IsJobStillNeeded() =>
        CurrentSite != null ? !CurrentSite.IsBuilt
        : CurrentTrap != null ? !CurrentTrap.IsArmed && (IsRearming || !CurrentTrap.SelfRearmOwns)
        : CurrentConduit != null ? powerGrid != null && powerGrid.IsPending(CurrentConduit.Value)
        : CurrentPipe != null ? manaFlow != null && manaFlow.IsPipePending(CurrentPipe.Value)
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
        Vector2Int fromTile, out Vector2Int targetTile, out Vector2Int standPoint, out Placeable site, out SpikeTrap trap) =>
        TrySelectWork(board, sites, null, traps, fromTile, out targetTile, out standPoint, out site, out _, out trap);

    /// <summary>
    /// Unified selection with conduit (#142): pending conduit tiles are construction-family work, evaluated right after
    /// the sites, so exact ties break dig &gt; construction (sites, then conduit) &gt; rearm. Like a site, a conduit tile
    /// is Floor and the imp stands beside it. A spent trap the powered self-rearm owns isn't rearm work.
    /// Exactly one outcome is set: site, conduit (targetTile = its tile), trap, or a dig.
    /// </summary>
    public static bool TrySelectWork(DungeonBoard board, IReadOnlyList<Placeable> sites, IReadOnlyList<Vector2Int> conduits,
        IReadOnlyList<SpikeTrap> traps, Vector2Int fromTile, out Vector2Int targetTile, out Vector2Int standPoint,
        out Placeable site, out bool conduit, out SpikeTrap trap) =>
        TrySelectWork(board, sites, conduits, null, traps, fromTile, out targetTile, out standPoint, out site, out conduit,
            out _, out trap);

    /// <summary>
    /// Unified selection with pipe (#145): pending pipe tiles are construction-family work right after conduit, so exact ties
    /// break dig &gt; sites &gt; conduit &gt; pipe &gt; rearm. Like conduit, the imp stands beside a pipe tile. Exactly one
    /// outcome is set: site, conduit, pipe (targetTile = its tile), trap, or a dig.
    /// </summary>
    public static bool TrySelectWork(DungeonBoard board, IReadOnlyList<Placeable> sites, IReadOnlyList<Vector2Int> conduits,
        IReadOnlyList<Vector2Int> pipes, IReadOnlyList<SpikeTrap> traps, Vector2Int fromTile, out Vector2Int targetTile,
        out Vector2Int standPoint, out Placeable site, out bool conduit, out bool pipe, out SpikeTrap trap)
    {
        trap = null;
        site = null;
        conduit = false;
        pipe = false;
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

        if (conduits != null)
        {
            foreach (Vector2Int tile in conduits)
            {
                List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, tile);
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable (stays pending), or not strictly nearer

                bestCost = path.Count;
                site = null;
                conduit = true;
                targetTile = tile;
                standPoint = path[path.Count - 1];
            }
        }

        if (pipes != null)
        {
            foreach (Vector2Int tile in pipes)
            {
                List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, tile);
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable (stays pending), or not strictly nearer

                bestCost = path.Count;
                site = null;
                conduit = false;
                pipe = true;
                targetTile = tile;
                standPoint = path[path.Count - 1];
            }
        }

        if (traps != null)
        {
            foreach (SpikeTrap candidate in traps)
            {
                // Armed traps aren't work, and a construction site isn't a spent trap (#91): never rearm one. A spent
                // trap on a powered grid rearms itself (#142): not the imp's.
                if (candidate == null || !candidate.IsBuilt || candidate.IsArmed || candidate.SelfRearmOwns) continue;

                List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, candidate.Tile);
                if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

                bestCost = path.Count;
                site = null; // A strictly nearer rearm beats the best build
                conduit = false;
                pipe = false;
                trap = candidate;
                targetTile = candidate.Tile;
                standPoint = path[path.Count - 1];
            }
        }

        return bestCost != int.MaxValue;
    }

    /// <summary>
    /// Operate selection (#142): the nearest built, enabled (#143) wheel that needs an operator and isn't tended by another
    /// imp, by path length to a stand-point beside it (never onto it). Ties keep the earlier wheel in list (scan) order.
    /// </summary>
    public static bool TrySelectOperate(DungeonBoard board, IReadOnlyList<PowerNode> wheels, ImpDigger self, Vector2Int fromTile,
        out PowerNode wheel, out Vector2Int standPoint)
    {
        wheel = null;
        standPoint = default;
        if (wheels == null) return false;
        int bestCost = int.MaxValue;
        foreach (PowerNode candidate in wheels)
        {
            if (candidate == null || !candidate.IsBuilt || !candidate.NeedsOperator || !candidate.Enabled) continue; // #143: off = no tending
            if (candidate.Operator != null && candidate.Operator != self) continue;

            List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, candidate.Tile);
            if (path.Count == 0 || path.Count >= bestCost) continue;

            bestCost = path.Count;
            wheel = candidate;
            standPoint = path[path.Count - 1];
        }
        return wheel != null;
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
