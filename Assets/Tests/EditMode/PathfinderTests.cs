using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PathfinderTests
{
    private GameObject boardGo;
    private DungeonBoard board;

    [SetUp]
    public void SetUp()
    {
        boardGo = new GameObject("TestBoard");
        board   = boardGo.AddComponent<DungeonBoard>();
        board.InitializeBoard(); // 48x32; cavern Floor at x=[21..26], y=[13..18]
    }

    [TearDown]
    public void TearDown()
    {
        if (boardGo != null) Object.DestroyImmediate(boardGo);
    }

    // ---- helper ----

    // Asserts the path is non-empty, starts/ends correctly, all tiles are Floor,
    // and every consecutive pair is 4-adjacent.
    private void AssertPathValid(List<Vector2Int> path, Vector2Int start, Vector2Int goal)
    {
        Assert.IsNotNull(path);
        Assert.Greater(path.Count, 0, "path must not be empty");
        Assert.AreEqual(start, path[0],  "path must start at start");
        Assert.AreEqual(goal,  path[path.Count - 1], "path must end at goal");

        for (int i = 0; i < path.Count; i++)
        {
            Assert.AreEqual(TileState.Floor, board.GetTile(path[i].x, path[i].y),
                $"tile at index {i} ({path[i]}) is not Floor");
        }

        for (int i = 1; i < path.Count; i++)
        {
            var a = path[i - 1]; var b = path[i];
            int dist = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
            Assert.AreEqual(1, dist, $"tiles at [{i-1}] and [{i}] are not 4-adjacent: {a} → {b}");
        }
    }

    // ---- AC: open-floor path is shortest ----

    [Test]
    public void FindPath_OpenFloor_PathLengthEqualsManhattanDistance()
    {
        // Straight 6-tile corridor at y=0
        for (int x = 0; x <= 5; x++) board.SetTile(x, 0, TileState.Floor);

        var start = new Vector2Int(0, 0);
        var goal  = new Vector2Int(5, 0);
        var path  = Pathfinder.FindPath(board, start, goal);

        Assert.AreEqual(6, path.Count, "path must have 6 tiles (Manhattan 5 + 1)");
        AssertPathValid(path, start, goal);
        Assert.AreEqual(5, path.Count - 1, "steps must equal Manhattan distance");
    }

    [Test]
    public void FindPath_OpenFloor_SamePathOnRepeatedCall()
    {
        for (int x = 0; x <= 5; x++) board.SetTile(x, 0, TileState.Floor);

        var start = new Vector2Int(0, 0);
        var goal  = new Vector2Int(5, 0);
        var first  = Pathfinder.FindPath(board, start, goal);
        var second = Pathfinder.FindPath(board, start, goal);

        Assert.AreEqual(first.Count, second.Count, "path length must be deterministic");
        for (int i = 0; i < first.Count; i++)
            Assert.AreEqual(first[i], second[i], $"tile at index {i} differs between calls");
    }

    // ---- AC: routes around a Rock wall ----

    [Test]
    public void FindPath_RoutesAroundRockWall_ThroughOnlyGap()
    {
        // Corridor y=0: x=[0..3] and x=[5..8] Floor; x=4 stays Rock (the wall).
        // Bypass:  y=1: x=[3,4,5] Floor so the path can go around.
        for (int x = 0; x <= 3; x++) board.SetTile(x, 0, TileState.Floor);
        for (int x = 5; x <= 8; x++) board.SetTile(x, 0, TileState.Floor);
        board.SetTile(3, 1, TileState.Floor);
        board.SetTile(4, 1, TileState.Floor);
        board.SetTile(5, 1, TileState.Floor);

        var start = new Vector2Int(0, 0);
        var goal  = new Vector2Int(8, 0);
        var path  = Pathfinder.FindPath(board, start, goal);

        Assert.IsNotNull(path);
        Assert.Greater(path.Count, 0, "path must exist");
        Assert.IsFalse(path.Contains(new Vector2Int(4, 0)), "path must not pass through the Rock wall");
        AssertPathValid(path, start, goal);
    }

    // ---- AC: no-path cases return empty ----

    [Test]
    public void FindPath_GoalIsRock_ReturnsEmpty()
    {
        // (0,0) is Rock by default
        var path = Pathfinder.FindPath(board, new Vector2Int(21, 13), new Vector2Int(0, 0));
        Assert.IsNotNull(path);
        Assert.AreEqual(0, path.Count);
    }

    [Test]
    public void FindPath_GoalOutOfBounds_ReturnsEmpty()
    {
        var path = Pathfinder.FindPath(board, new Vector2Int(21, 13), new Vector2Int(-1, -1));
        Assert.IsNotNull(path);
        Assert.AreEqual(0, path.Count);
    }

    [Test]
    public void FindPath_GoalFloorFullyEnclosed_ReturnsEmpty()
    {
        // Island Floor tile at (3,3) surrounded entirely by Rock — unreachable from cavern
        board.SetTile(3, 3, TileState.Floor);

        var path = Pathfinder.FindPath(board, new Vector2Int(24, 15), new Vector2Int(3, 3));
        Assert.IsNotNull(path);
        Assert.AreEqual(0, path.Count);
    }

    // ---- AC: start == goal ----

    [Test]
    public void FindPath_StartEqualsGoal_ReturnsSingleTilePath()
    {
        var pos  = new Vector2Int(24, 15); // cavern Floor
        var path = Pathfinder.FindPath(board, pos, pos);

        Assert.IsNotNull(path);
        Assert.AreEqual(1, path.Count);
        Assert.AreEqual(pos, path[0]);
    }

    // ---- AC: FindPathToAdjacent ----

    [Test]
    public void FindPathToAdjacent_RockTarget_EndsOnNeighborFloor()
    {
        // Default board: cavern at x=[21..26], y=[13..18].
        // Target (20,15) is Rock; its only Floor neighbor is (21,15).
        var start  = new Vector2Int(24, 15);
        var target = new Vector2Int(20, 15);
        var path   = Pathfinder.FindPathToAdjacent(board, start, target);

        Assert.IsNotNull(path);
        Assert.Greater(path.Count, 0);
        Assert.AreEqual(TileState.Floor, board.GetTile(path[path.Count - 1].x, path[path.Count - 1].y),
            "last tile must be Floor");
        AssertPathValid(path, start, path[path.Count - 1]);
    }

    [Test]
    public void FindPathToAdjacent_StartAlreadyAdjacent_ReturnsTrivialPath()
    {
        // Target (20,15) Rock; start (21,15) is its only Floor neighbor.
        var start  = new Vector2Int(21, 15);
        var target = new Vector2Int(20, 15);
        var path   = Pathfinder.FindPathToAdjacent(board, start, target);

        Assert.IsNotNull(path);
        Assert.AreEqual(1, path.Count);
        Assert.AreEqual(start, path[0]);
    }

    [Test]
    public void FindPathToAdjacent_NoFloorNeighbor_ReturnsEmpty()
    {
        // Target (3,3) deep in Rock; all four neighbors are also Rock.
        var path = Pathfinder.FindPathToAdjacent(board, new Vector2Int(24, 15), new Vector2Int(3, 3));
        Assert.IsNotNull(path);
        Assert.AreEqual(0, path.Count);
    }

    [Test]
    public void FindPathToAdjacent_WalkableTarget_BehavesLikeFindPath()
    {
        // If target is a Floor tile, FindPathToAdjacent must return the same path as FindPath.
        var start  = new Vector2Int(21, 13);
        var target = new Vector2Int(26, 18); // opposite corner of cavern
        var direct = Pathfinder.FindPath(board, start, target);
        var via    = Pathfinder.FindPathToAdjacent(board, start, target);

        Assert.AreEqual(direct.Count, via.Count, "path length must match FindPath");
        for (int i = 0; i < direct.Count; i++)
            Assert.AreEqual(direct[i], via[i], $"tile at index {i} differs");
    }

    // ---- FindPathToNeighbor ----

    [Test]
    public void FindPathToNeighbor_WalkableTarget_EndsBesideIt_NotOnIt()
    {
        // Unlike FindPathToAdjacent, a Floor target is never the endpoint.
        var start  = new Vector2Int(24, 16);
        var target = new Vector2Int(22, 14);
        var path   = Pathfinder.FindPathToNeighbor(board, start, target);

        Assert.Greater(path.Count, 0);
        Vector2Int end = path[path.Count - 1];
        Assert.AreEqual(1, Mathf.Abs(end.x - target.x) + Mathf.Abs(end.y - target.y), "must end on a 4-neighbor");
        CollectionAssert.DoesNotContain(path, target, "must not pass through the target either way");
        Assert.AreEqual(4, path.Count, "shortest: (24,16) -> (23,14) or (22,15) is 3 steps");
    }

    [Test]
    public void FindPathToNeighbor_StartOnTarget_StepsOffToANeighbor()
    {
        var target = new Vector2Int(24, 16);
        var path   = Pathfinder.FindPathToNeighbor(board, target, target);

        Assert.AreEqual(2, path.Count);
        Assert.AreEqual(target, path[0]);
        Assert.AreEqual(1, Mathf.Abs(path[1].x - target.x) + Mathf.Abs(path[1].y - target.y));
    }

    [Test]
    public void FindPathToNeighbor_StartAlreadyAdjacent_ReturnsTrivialPath()
    {
        var start = new Vector2Int(23, 14);
        var path  = Pathfinder.FindPathToNeighbor(board, start, new Vector2Int(22, 14));

        Assert.AreEqual(1, path.Count);
        Assert.AreEqual(start, path[0]);
    }

    [Test]
    public void FindPathToNeighbor_RockTarget_MatchesFindPathToAdjacent()
    {
        var start  = new Vector2Int(24, 15);
        var target = new Vector2Int(20, 15);

        CollectionAssert.AreEqual(Pathfinder.FindPathToAdjacent(board, start, target),
            Pathfinder.FindPathToNeighbor(board, start, target));
    }
}
