using System.Collections.Generic;
using UnityEngine;

public enum BuildCategory
{
    Living,
    Farming,
    Defense,
    Magic,
    Power // #142: appended; the rail seats it between Defense and Magic (CategoryOrder)
}

/// <summary>The simple filled shape a rail entry or tile wears as its icon (code-built; the runtime font has no emoji).</summary>
public enum BuildIconShape
{
    Triangle,
    Bar,
    Circle,
    Diamond,
    Cross,
    Stack, // #126: the STOCK entry, three stacked slabs
    Bolt   // #142: the Power category, a lightning bolt
}

/// <summary>
/// A flyout tile that arms a tool verb rather than a placeable (#142): Lay conduit arms Wire mode. Verbs cost imp time
/// only, so their tiles carry no material cost.
/// </summary>
public enum BuildVerb
{
    LayConduit
}

/// <summary>A material family (#126): the STOCK panel groups its rows under these, in this order.</summary>
public enum MaterialFamily
{
    Stones,
    Other // Where a material with no declared family falls, so it never vanishes from the panel
}

/// <summary>A stat line the detail card can show for a type (#125). DetailCard.StatLine reads each from PlacementManager.</summary>
public enum BuildStat
{
    RestRate,     // The material's rest-rate multiplier (the #110 property table)
    FoodGrowth,   // Seconds per food grown
    FoodCapacity, // Food a plot holds
    TrapDamage,
    PowerOutput,  // #142: a producer's watts, from the power table ("Output 40 W")
    NeedsOperator, // #142: "Needs an imp" for a producer an imp must tend
    PowerDraw,     // #143: a consumer's watts, from the power table ("Draw 20 W")
    FuelUse,       // #143: a fueled producer's burn, from its power row ("Burns 1 mushroom per 12 s")
    LampCoverage   // #143: the Grow-Lamp's reach and boost ("Plots within 2 tiles grow ×2")
}

/// <summary>
/// The one declared place the build chrome reads its presentation from (#123): the category order, and per
/// PlaceableType its category, display name, description, accent and the stats its detail card lists (#125); and per
/// material its family, description and source line (#126, the STOCK panel and the detail card's material mode).
/// BuildRail, StockPanel and DetailCard paint from this; nothing else in the project holds build display data. A category
/// with no types never appears on the rail.
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
        BuildCategory.Living, BuildCategory.Farming, BuildCategory.Defense, BuildCategory.Power, BuildCategory.Magic,
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
        new Entry(PlaceableType.ImpWheel, BuildCategory.Power, "Imp Wheel",
            "A wheel an imp turns to power conduit. It stops whenever the imp leaves for other work.", new Color(0.95f, 0.80f, 0.30f, 1f),
            BuildStat.PowerOutput, BuildStat.NeedsOperator),
        new Entry(PlaceableType.FurnaceGenerator, BuildCategory.Power, "Furnace Generator",
            "Burns stored mushrooms for steady power, no imp needed. Its chimney breathes warm air.", new Color(0.95f, 0.55f, 0.25f, 1f),
            BuildStat.PowerOutput, BuildStat.FuelUse),
        new Entry(PlaceableType.GrowLamp, BuildCategory.Power, "Grow-Lamp",
            "A warm lamp that speeds up nearby mushroom plots while it has power.", new Color(1.00f, 0.86f, 0.45f, 1f),
            BuildStat.PowerDraw, BuildStat.LampCoverage),
    };

    /// <summary>A verb tile's presentation (#142).</summary>
    public sealed class VerbEntry
    {
        public readonly BuildVerb verb;
        public readonly BuildCategory category;
        public readonly string displayName;
        public readonly string description;
        public readonly string costLine; // The tile's second line, in place of a material cost
        public readonly Color accent;

        public VerbEntry(BuildVerb verb, BuildCategory category, string displayName, string description, string costLine, Color accent)
        {
            this.verb = verb;
            this.category = category;
            this.displayName = displayName;
            this.description = description;
            this.costLine = costLine;
            this.accent = accent;
        }
    }

    // In tile order within a category; a category's verbs come before its types.
    private static readonly VerbEntry[] VerbEntries =
    {
        new VerbEntry(BuildVerb.LayConduit, BuildCategory.Power, "Lay conduit",
            "Drag over explored floor to order conduit; imps wire it. Grids carry power from producers to consumers.",
            "Imp time only", new Color(0.85f, 0.55f, 0.25f, 1f)),
    };

    /// <summary>One material's presentation (#126). Its name and color stay the Stockpile catalog's.</summary>
    public sealed class MaterialEntry
    {
        public readonly MaterialType material;
        public readonly MaterialFamily family;
        public readonly string description;
        public readonly string source; // Where it comes from ("Dug · mid depths")

        public MaterialEntry(MaterialType material, MaterialFamily family, string description, string source)
        {
            this.material = material;
            this.family = family;
            this.description = description;
            this.source = source;
        }
    }

    /// <summary>The STOCK panel's family order, top to bottom.</summary>
    public static readonly MaterialFamily[] FamilyOrder = { MaterialFamily.Stones, MaterialFamily.Other };

    // First-pass copy, Evan-retunable.
    private static readonly MaterialEntry[] MaterialEntries =
    {
        new MaterialEntry(MaterialType.Stone, MaterialFamily.Stones,
            "The dungeon's backbone material, dug from the mid depths.", "Dug · mid depths"),
        new MaterialEntry(MaterialType.Glimmerstone, MaterialFamily.Stones,
            "Shallow stone from near the entrance, faintly magical.", "Dug · entrance shallows"),
        new MaterialEntry(MaterialType.Deepstone, MaterialFamily.Stones,
            "Hard stone from far underground.", "Dug · deep rows"),
    };

    public const string DigLabel = "DIG";
    public const BuildIconShape DigShape = BuildIconShape.Triangle;
    public static readonly Color DigAccent = new Color(0.90f, 0.90f, 0.92f, 1f);

    public const string StockLabel = "STOCK";
    public const BuildIconShape StockShape = BuildIconShape.Stack;
    public static readonly Color StockAccent = new Color(0.78f, 0.70f, 0.55f, 1f); // Warm sandstone: stored goods

    public static bool TryGetMaterial(MaterialType material, out MaterialEntry entry)
    {
        foreach (MaterialEntry e in MaterialEntries)
        {
            if (e.material != material) continue;
            entry = e;
            return true;
        }
        entry = null;
        return false;
    }

    /// <summary>A material's family; Other for a material with no entry.</summary>
    public static MaterialFamily FamilyOf(MaterialType material) => TryGetMaterial(material, out MaterialEntry e) ? e.family : MaterialFamily.Other;

    /// <summary>The panel's group header ("Stones").</summary>
    public static string FamilyName(MaterialFamily family) => family.ToString();

    public static string MaterialDescription(MaterialType material) => TryGetMaterial(material, out MaterialEntry e) ? e.description : "";

    public static string MaterialSource(MaterialType material) => TryGetMaterial(material, out MaterialEntry e) ? e.source : "";

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

    public static bool TryGetVerb(BuildVerb verb, out VerbEntry entry)
    {
        foreach (VerbEntry e in VerbEntries)
        {
            if (e.verb != verb) continue;
            entry = e;
            return true;
        }
        entry = null;
        return false;
    }

    /// <summary>The verb tiles a category's flyout lists, before its types (#142). Empty for most categories.</summary>
    public static List<BuildVerb> VerbsInCategory(BuildCategory category)
    {
        var verbs = new List<BuildVerb>();
        foreach (VerbEntry e in VerbEntries)
            if (e.category == category) verbs.Add(e.verb);
        return verbs;
    }

    /// <summary>The categories the rail shows: CategoryOrder without the empty ones (no types and no verbs).</summary>
    public static List<BuildCategory> VisibleCategories()
    {
        var visible = new List<BuildCategory>();
        foreach (BuildCategory c in CategoryOrder)
            if (TypesInCategory(c).Count > 0 || VerbsInCategory(c).Count > 0) visible.Add(c);
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
            case BuildCategory.Power: return "POWER";
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
            case BuildCategory.Power: return BuildIconShape.Bolt;
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
            case BuildCategory.Power: return new Color(0.95f, 0.80f, 0.30f, 1f);
            default: return new Color(0.62f, 0.50f, 0.88f, 1f);
        }
    }
}
