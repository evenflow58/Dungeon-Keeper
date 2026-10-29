using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the board is drawn (#144). Normal is the everyday view; every other mode is an overlay that recolors tile views
/// at color-resolution time (BoardRenderer asks the mode for an override before falling back to the normal color), so a
/// later overlay (magic, heat) is a new value here plus its own color path.
/// </summary>
public enum ViewMode
{
    Normal,
    Power,
    Magic, // #145: mana by type, the vent, pipes and stores
    Room   // #165: rooms tinted by type, everything else dimmed
}

/// <summary>How a built power node reads in the power overlay (#144).</summary>
public enum NodeLook
{
    ActiveProducer,    // Making watts now (a tended wheel, a burning furnace)
    IdleProducer,      // Connected but not making watts (untended, out of fuel)
    PoweredConsumer,   // Its grid covers it
    UnpoweredConsumer, // Its grid is short: the fault color
    Disabled,          // Switched off by the player (#143): greyed
    Unconnected        // No conduit under it: on no grid
}

/// <summary>
/// The power overlay's rules over plain data (#144), apart from any view so tests drive them: a node's look from its
/// flags, a grid's hue from the palette, the dimming of everything off the network, and the per-tile color resolution
/// that leaves Normal mode untouched.
/// </summary>
public static class PowerOverlay
{
    /// <summary>
    /// A node's look. Disabled wins (off by choice, whatever else holds), then unconnected (on no grid, so neither
    /// producing nor powered means anything), then by role: a producer by whether it's active, a consumer by whether
    /// it's powered.
    /// </summary>
    public static NodeLook LookOf(PowerRole role, bool enabled, bool connected, bool active, bool powered)
    {
        if (!enabled) return NodeLook.Disabled;
        if (!connected) return NodeLook.Unconnected;
        if (role == PowerRole.Producer) return active ? NodeLook.ActiveProducer : NodeLook.IdleProducer;
        return powered ? NodeLook.PoweredConsumer : NodeLook.UnpoweredConsumer;
    }

    /// <summary>
    /// A grid's hue: palette[gridId % count], so the same id always gets the same hue and two neighboring ids differ
    /// whenever the palette has two or more entries. The fallback covers an empty palette or a negative id.
    /// </summary>
    public static Color GridHue(IReadOnlyList<Color> palette, int gridId, Color fallback) =>
        palette == null || palette.Count == 0 || gridId < 0 ? fallback : palette[gridId % palette.Count];

    /// <summary>A color dimmed toward black by factor (clamped 0–1; 1 leaves it as is). Alpha is kept.</summary>
    public static Color Dim(Color color, float factor)
    {
        float f = Mathf.Clamp01(factor);
        return new Color(color.r * f, color.g * f, color.b * f, color.a);
    }

    /// <summary>
    /// Whether a placeable goes see-through: in the power overlay, everything that isn't a power node (a cot, a plot), so
    /// the conduit under it shows. Power nodes stay solid; their tiles carry their state. Normal mode never changes models.
    /// </summary>
    public static bool SeesThrough(ViewMode mode, bool isPowerNode) => SeesThrough(mode, isPowerNode, false);

    /// <summary>
    /// The see-through rule across overlays (#145): in Power everything but power nodes goes see-through; in Magic
    /// everything but mana stores (so pipe under a cot shows). Normal never changes models.
    /// </summary>
    public static bool SeesThrough(ViewMode mode, bool isPowerNode, bool isManaStore) =>
        mode == ViewMode.Power ? !isPowerNode : mode == ViewMode.Magic && !isManaStore;

    /// <summary>
    /// A tile's displayed color. Normal mode is the identity: the normal color, always. In an overlay an unexplored tile
    /// keeps its normal (fog) color, so nothing shows through the fog; an explored tile takes the overlay's override when
    /// it has one, else its normal color dimmed (dimmed = false keeps it as is, e.g. a designation).
    /// </summary>
    public static Color Resolve(ViewMode mode, bool explored, Color normal, Color? overlay, bool dimmed, float dimFactor)
    {
        if (mode == ViewMode.Normal || !explored) return normal;
        if (overlay is Color o) return o;
        return dimmed ? Dim(normal, dimFactor) : normal;
    }
}
