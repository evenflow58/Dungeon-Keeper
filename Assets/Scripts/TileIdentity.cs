using UnityEngine;

/// <summary>
/// What a tile is, as the player is shown it (#132): its state and, for rock (Rock or Designated), the material
/// digging it would yield. Resolved in one place (Of) and read by BoardRenderer (color) and TileHover (label), so
/// the view and the tooltip can't disagree with each other or with the digger: the material comes from
/// ImpDigger.MaterialOf, the same code path and serialized band values TryGetTileYield pays from.
/// Presentation only, and composed from tile facts: future per-tile facts (e.g. something buried) join here as
/// more fields, and the style and label code compose them, rather than rock meaning "one color, one name".
/// </summary>
public readonly struct TileIdentity
{
    public readonly TileState State;
    public readonly MaterialType? Material; // Null for Floor/Entrance, and when no digger is wired to ask

    public TileIdentity(TileState state, MaterialType? material)
    {
        State = state;
        Material = material;
    }

    /// <summary>A tile made of something a dig would yield: Rock, and Designated (an order on rock, still rock).</summary>
    public static bool IsDiggable(TileState state) => state == TileState.Rock || state == TileState.Designated;

    /// <summary>
    /// The tile's identity: its state from the board, and for rock its material from the digger. Without a digger
    /// the material is unknown (null), which renders and labels as before #132.
    /// </summary>
    public static TileIdentity Of(DungeonBoard board, ImpDigger digger, Vector2Int tile)
    {
        TileState state = board.GetTile(tile.x, tile.y);
        MaterialType? material = IsDiggable(state) && digger != null ? digger.MaterialOf(tile) : (MaterialType?)null;
        return new TileIdentity(state, material);
    }
}
