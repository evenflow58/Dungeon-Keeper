public enum TileState
{
    Rock,
    Designated,
    Floor,
    Entrance // The dungeon's door at the board edge, once dug out (#163): heroes arrive and leave here, expedition imps leave
             // and return. Not walkable, diggable or placeable.
}
