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

    /// <summary>
    /// Stages discovery (#126) the way play earns it: one of each material lands in the stockpile and is spent again, so
    /// the materials are discovered and every count is left exactly as it was.
    /// </summary>
    public static void Discover(this Stockpile stockpile, params MaterialType[] materials)
    {
        foreach (MaterialType material in materials)
        {
            stockpile.Add(material, 1);
            stockpile.TrySpend(material, 1);
        }
    }

    /// <summary>Discover, for every material in the enum.</summary>
    public static void DiscoverAll(this Stockpile stockpile) =>
        stockpile.Discover((MaterialType[])System.Enum.GetValues(typeof(MaterialType)));
}
