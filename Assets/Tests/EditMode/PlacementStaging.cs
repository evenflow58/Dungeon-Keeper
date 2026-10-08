using UnityEngine;

/// <summary>
/// Test staging for construction sites (#91): placement creates an inert site, so suites that exercise a
/// working placeable place it and then finish it through the CompleteConstruction seam (the builder's job
/// from story #92 on).
/// </summary>
public static class PlacementStaging
{
    /// <summary>TryPlace, then CompleteConstruction: a built, working placeable. False when placement fails.</summary>
    public static bool TryPlaceBuilt(this PlacementManager manager, PlaceableType type, Vector2Int tile, Vector2Int fromTile,
        out Placeable placeable)
    {
        if (!manager.TryPlace(type, tile, fromTile, out placeable)) return false;
        placeable.CompleteConstruction();
        return true;
    }
}
