using System.Collections.Generic;
using UnityEngine;

public static class Pathfinder
{
    // Fixed neighbor order for determinism: right, up, left, down
    private static readonly Vector2Int[] Dirs = {
        new Vector2Int( 1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int(-1,  0),
        new Vector2Int( 0, -1),
    };

    /// <summary>
    /// Returns the shortest path from start to goal (both endpoints included),
    /// or an empty list if no path exists or the goal is not a walkable Floor tile.
    /// </summary>
    public static List<Vector2Int> FindPath(DungeonBoard board, Vector2Int start, Vector2Int goal)
    {
        if (!board.IsWalkable(goal.x, goal.y))
            return new List<Vector2Int>();

        if (start == goal)
            return new List<Vector2Int> { start };

        return Search(board, start, goal);
    }

    /// <summary>
    /// Returns the shortest path from start to any walkable neighbor of target.
    /// If target itself is a walkable Floor tile, behaves exactly like FindPath.
    /// If start is already adjacent to target, returns the trivial one-tile path [start].
    /// Returns an empty list when no reachable neighbor exists.
    /// </summary>
    public static List<Vector2Int> FindPathToAdjacent(DungeonBoard board, Vector2Int start, Vector2Int target)
    {
        if (board.IsWalkable(target.x, target.y))
            return FindPath(board, start, target);

        // Collect walkable neighbors in fixed direction order
        var candidates = new List<Vector2Int>();
        foreach (var dir in Dirs)
        {
            var n = target + dir;
            if (board.IsWalkable(n.x, n.y))
                candidates.Add(n);
        }

        if (candidates.Count == 0)
            return new List<Vector2Int>();

        // Start is already standing next to the target
        foreach (var c in candidates)
            if (c == start) return new List<Vector2Int> { start };

        // Return shortest path to any candidate; ties broken by candidate iteration order
        List<Vector2Int> best = null;
        foreach (var c in candidates)
        {
            var path = Search(board, start, c);
            if (path.Count > 0 && (best == null || path.Count < best.Count))
                best = path;
        }

        return best ?? new List<Vector2Int>();
    }

    // ---- internal ----

    private static List<Vector2Int> Search(DungeonBoard board, Vector2Int start, Vector2Int goal)
    {
        var openSet  = new List<Vector2Int> { start };
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore   = new Dictionary<Vector2Int, int> { [start] = 0 };
        var fScore   = new Dictionary<Vector2Int, int> { [start] = Manhattan(start, goal) };

        while (openSet.Count > 0)
        {
            var current = PickLowestF(openSet, fScore);
            if (current == goal) return BuildPath(cameFrom, current);
            openSet.Remove(current);

            foreach (var dir in Dirs)
            {
                var nb = current + dir;
                if (!Walkable(board, nb, start)) continue;

                int g = gScore[current] + 1;
                if (!gScore.TryGetValue(nb, out int existing) || g < existing)
                {
                    cameFrom[nb] = current;
                    gScore[nb]   = g;
                    fScore[nb]   = g + Manhattan(nb, goal);
                    if (!openSet.Contains(nb)) openSet.Add(nb);
                }
            }
        }
        return new List<Vector2Int>();
    }

    private static bool Walkable(DungeonBoard board, Vector2Int pos, Vector2Int start)
    {
        if (!board.IsInBounds(pos.x, pos.y)) return false;
        if (pos == start) return true; // mover is always on their start tile
        return board.IsWalkable(pos.x, pos.y);
    }

    private static Vector2Int PickLowestF(List<Vector2Int> openSet, Dictionary<Vector2Int, int> f)
    {
        var best  = openSet[0];
        int bestF = f.TryGetValue(best, out int v) ? v : int.MaxValue;
        for (int i = 1; i < openSet.Count; i++)
        {
            int fi = f.TryGetValue(openSet[i], out int vi) ? vi : int.MaxValue;
            if (fi < bestF) { best = openSet[i]; bestF = fi; }
        }
        return best;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private static List<Vector2Int> BuildPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int end)
    {
        var path = new List<Vector2Int> { end };
        while (cameFrom.TryGetValue(end, out var prev)) { path.Add(prev); end = prev; }
        path.Reverse();
        return path;
    }
}
