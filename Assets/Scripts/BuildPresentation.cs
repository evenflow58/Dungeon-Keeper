using System.Collections.Generic;
using UnityEngine;

public enum BuildCategory
{
    Living,
    Farming,
    Defense,
    Magic
}

/// <summary>The simple filled shape a rail entry or tile wears as its icon (code-built; the runtime font has no emoji).</summary>
public enum BuildIconShape
{
    Triangle,
    Bar,
    Circle,
    Diamond,
    Cross
}

/// <summary>A stat line the detail card can show for a type (#125). DetailCard.StatLine reads each from PlacementManager.</summary>
public enum BuildStat
{
    RestRate,     // The material's rest-rate multiplier (the #110 property table)
    FoodGrowth,   // Seconds per food grown
    FoodCapacity, // Food a plot holds
    TrapDamage
}

/// <summary>
/// The one declared place the build chrome reads its presentation from (#123): the category order, and per
/// PlaceableType its category, display name, description, accent and the stats its detail card lists (#125).
/// BuildRail and DetailCard paint from this; nothing else in the project holds build display data. A category with no types never appears on the rail.
/// </summary>
public static class BuildPresentation
{
    public sealed class Entry
    {
        public readonly PlaceableType type;
        public readonly BuildCategory category;
        public readonly string displayName;
        public readonly string description; // The detail card's text (#125)
        public readonly Color accent;
        public readonly BuildStat[] stats;  // The detail card's stat lines, in order, after the build time (#125)

        public Entry(PlaceableType type, BuildCategory category, string displayName, string description, Color accent,
            params BuildStat[] stats)
        {
            this.type = type;
            this.category = category;
            this.displayName = displayName;
            this.description = description;
            this.accent = accent;
            this.stats = stats ?? new BuildStat[0];
        }
    }

    /// <summary>The rail's category order, top to bottom.</summary>
    public static readonly BuildCategory[] CategoryOrder =
    {
        BuildCategory.Living, BuildCategory.Farming, BuildCategory.Defense, BuildCategory.Magic,
    };

    // In tile order within a category.
    private static readonly Entry[] Entries =
    {
        new Entry(PlaceableType.LairCot, BuildCategory.Living, "Lair Cot",
            "A bed of piled material. Goblins sleep here to restore energy.", new Color(0.55f, 0.72f, 0.92f, 1f),
            BuildStat.RestRate),
        new Entry(PlaceableType.MushroomPlot, BuildCategory.Farming, "Mushroom Plot",
            "Grows food over time, up to 5. Goblins eat from a stocked plot.", new Color(0.50f, 0.78f, 0.45f, 1f),
            BuildStat.FoodGrowth, BuildStat.FoodCapacity),
        new Entry(PlaceableType.SpikeTrap, BuildCategory.Defense, "Spike Trap",
            "A one-shot trap. Hurts the first hero to step on it; an imp rearms it.", new Color(0.85f, 0.45f, 0.40f, 1f),
            BuildStat.TrapDamage),
    };

    public const string DigLabel = "DIG";
    public const BuildIconShape DigShape = BuildIconShape.Triangle;
    public static readonly Color DigAccent = new Color(0.90f, 0.90f, 0.92f, 1f);

    public static bool TryGet(PlaceableType type, out Entry entry)
    {
        foreach (Entry e in Entries)
        {
            if (e.type != type) continue;
            entry = e;
            return true;
        }
        entry = null;
        return false;
    }

    /// <summary>The type's display name ("Lair Cot"); the enum name for a type with no entry.</summary>
    public static string DisplayName(PlaceableType type) => TryGet(type, out Entry e) ? e.displayName : type.ToString();

    public static string Description(PlaceableType type) => TryGet(type, out Entry e) ? e.description : "";

    /// <summary>The stats the type's detail card lists, in order; none for a type with no entry.</summary>
    public static BuildStat[] Stats(PlaceableType type) => TryGet(type, out Entry e) ? e.stats : new BuildStat[0];

    public static bool TryGetCategory(PlaceableType type, out BuildCategory category)
    {
        bool found = TryGet(type, out Entry e);
        category = found ? e.category : default;
        return found;
    }

    /// <summary>The types a category's flyout lists, in tile order. Empty for a category nothing declares.</summary>
    public static List<PlaceableType> TypesInCategory(BuildCategory category)
    {
        var types = new List<PlaceableType>();
        foreach (Entry e in Entries)
            if (e.category == category) types.Add(e.type);
        return types;
    }

    /// <summary>The categories the rail shows: CategoryOrder without the empty ones.</summary>
    public static List<BuildCategory> VisibleCategories()
    {
        var visible = new List<BuildCategory>();
        foreach (BuildCategory c in CategoryOrder)
            if (TypesInCategory(c).Count > 0) visible.Add(c);
        return visible;
    }

    /// <summary>The flyout's title ("Farming").</summary>
    public static string CategoryName(BuildCategory category) => category.ToString();

    /// <summary>The rail's micro-label under the icon ("FARM").</summary>
    public static string RailLabel(BuildCategory category)
    {
        switch (category)
        {
            case BuildCategory.Living: return "LIVING";
            case BuildCategory.Farming: return "FARM";
            case BuildCategory.Defense: return "DEFENSE";
            default: return "MAGIC";
        }
    }

    public static BuildIconShape Shape(BuildCategory category)
    {
        switch (category)
        {
            case BuildCategory.Living: return BuildIconShape.Bar;
            case BuildCategory.Farming: return BuildIconShape.Circle;
            case BuildCategory.Defense: return BuildIconShape.Diamond;
            default: return BuildIconShape.Cross;
        }
    }

    public static Color Accent(BuildCategory category)
    {
        switch (category)
        {
            case BuildCategory.Living: return new Color(0.55f, 0.72f, 0.92f, 1f);
            case BuildCategory.Farming: return new Color(0.50f, 0.78f, 0.45f, 1f);
            case BuildCategory.Defense: return new Color(0.85f, 0.45f, 0.40f, 1f);
            default: return new Color(0.62f, 0.50f, 0.88f, 1f);
        }
    }
}
