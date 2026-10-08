using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A building material. Stone only for now (the materials epic adds more).</summary>
public enum MaterialType
{
    Stone
}

/// <summary>How a material reads to the player: its display name and color.</summary>
[Serializable]
public class MaterialDefinition
{
    public MaterialType type;
    public string displayName;
    public Color color;

    public MaterialDefinition(MaterialType type, string displayName, Color color)
    {
        this.type = type;
        this.displayName = displayName;
        this.color = color;
    }
}

/// <summary>A material and an amount of it (the serialized starting stock).</summary>
[Serializable]
public class MaterialAmount
{
    public MaterialType type;
    public int amount;

    public MaterialAmount(MaterialType type, int amount)
    {
        this.type = type;
        this.amount = amount;
    }
}

/// <summary>
/// The dungeon's global stockpile of materials (#102), on the DungeonManager. Holds the material catalog (one
/// serialized definition list: name and color per material) and the per-material counts, seeded from serialized
/// starting amounts. Digging will earn into it and building will spend from it (later stories); nothing does yet.
/// Counts are independent of definitions: a material with no definition still counts, and anything never added
/// reads 0.
/// </summary>
[DisallowMultipleComponent]
public class Stockpile : MonoBehaviour
{
    [Header("Material Catalog")]
    [SerializeField] private List<MaterialDefinition> definitions = new List<MaterialDefinition>
    {
        new MaterialDefinition(MaterialType.Stone, "Stone", new Color(0.62f, 0.62f, 0.66f, 1f)), // Cool gray
    };

    [Header("Starting Stock")]
    [SerializeField] private List<MaterialAmount> startingAmounts = new List<MaterialAmount>
    {
        new MaterialAmount(MaterialType.Stone, 10),
    };

    // Seeded from startingAmounts on first use (lazily, so EditMode tests see it without Awake).
    private Dictionary<MaterialType, int> counts;

    /// <summary>The catalog (replaceable in code, e.g. by tests).</summary>
    public List<MaterialDefinition> Definitions { get => definitions; set => definitions = value ?? new List<MaterialDefinition>(); }

    /// <summary>
    /// Starting amounts. Setting them re-seeds the counts (a fresh stockpile with that stock), so tests and staging
    /// can configure it before play.
    /// </summary>
    public List<MaterialAmount> StartingAmounts
    {
        get => startingAmounts;
        set
        {
            startingAmounts = value ?? new List<MaterialAmount>();
            counts = null;
        }
    }

    private void Awake()
    {
        EnsureSeeded();
    }

    /// <summary>The current amount of a material; 0 for anything never stocked.</summary>
    public int Count(MaterialType type)
    {
        EnsureSeeded();
        return counts.TryGetValue(type, out int n) ? n : 0;
    }

    /// <summary>Adds amount of a material. Non-positive amounts are ignored.</summary>
    public void Add(MaterialType type, int amount)
    {
        if (amount <= 0) return;
        EnsureSeeded();
        counts[type] = Count(type) + amount;
    }

    /// <summary>
    /// All-or-nothing: if the stock covers amount, deducts it and returns true; otherwise changes nothing and
    /// returns false. Non-positive amounts return false.
    /// </summary>
    public bool TrySpend(MaterialType type, int amount)
    {
        if (amount <= 0) return false;
        int have = Count(type);
        if (have < amount) return false;
        counts[type] = have - amount;
        return true;
    }

    /// <summary>The catalog entry for a material; false (and null) when it has none.</summary>
    public bool TryGetDefinition(MaterialType type, out MaterialDefinition definition)
    {
        if (definitions != null)
        {
            foreach (MaterialDefinition d in definitions)
            {
                if (d != null && d.type == type)
                {
                    definition = d;
                    return true;
                }
            }
        }
        definition = null;
        return false;
    }

    /// <summary>A material's display name: its definition's, else the enum name.</summary>
    public string DisplayName(MaterialType type) =>
        TryGetDefinition(type, out MaterialDefinition d) && !string.IsNullOrEmpty(d.displayName) ? d.displayName : type.ToString();

    private void EnsureSeeded()
    {
        if (counts != null) return;
        counts = new Dictionary<MaterialType, int>();
        if (startingAmounts == null) return;
        foreach (MaterialAmount a in startingAmounts)
        {
            if (a != null && a.amount > 0) counts[a.type] = (counts.TryGetValue(a.type, out int n) ? n : 0) + a.amount;
        }
    }
}
