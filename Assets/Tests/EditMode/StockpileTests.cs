using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>The material catalog and stockpile (#102): materials as data, counts, and the all-or-nothing spend.</summary>
public class StockpileTests
{
    private GameObject go;
    private Stockpile stockpile;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("TestDungeonManager");
        stockpile = go.AddComponent<Stockpile>(); // EditMode: no Awake; the counts seed lazily
    }

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    [Test]
    public void FreshStockpile_StartsWithTheSerializedDefaults_TenStone()
    {
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone));
        Assert.AreEqual(1, stockpile.StartingAmounts.Count);
        Assert.AreEqual(MaterialType.Stone, stockpile.StartingAmounts[0].type);
        Assert.AreEqual(10, stockpile.StartingAmounts[0].amount);
    }

    [Test]
    public void StartingAmounts_AreConfigurable_AndReseed()
    {
        stockpile.Add(MaterialType.Stone, 7);
        stockpile.StartingAmounts = new List<MaterialAmount> { new MaterialAmount(MaterialType.Stone, 3) };
        Assert.AreEqual(3, stockpile.Count(MaterialType.Stone), "A fresh stockpile with the new stock");

        stockpile.StartingAmounts = new List<MaterialAmount>();
        Assert.AreEqual(0, stockpile.Count(MaterialType.Stone), "Nothing stocked reads 0");
    }

    [Test]
    public void Add_Accumulates()
    {
        stockpile.Add(MaterialType.Stone, 3);
        stockpile.Add(MaterialType.Stone, 4);
        Assert.AreEqual(17, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void Add_NonPositive_IsIgnored()
    {
        stockpile.Add(MaterialType.Stone, 0);
        stockpile.Add(MaterialType.Stone, -5);
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void TrySpend_Affordable_DeductsExactly()
    {
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Stone, 4));
        Assert.AreEqual(6, stockpile.Count(MaterialType.Stone));
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Stone, 6), "Spending exactly what's there");
        Assert.AreEqual(0, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void TrySpend_Short_RefusesAndChangesNothing()
    {
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Stone, 11), "All or nothing: no partial spend");
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone));
        stockpile.StartingAmounts = new List<MaterialAmount>();
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Stone, 1), "Empty");
        Assert.AreEqual(0, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void TrySpend_NonPositive_ReturnsFalse_ChangesNothing()
    {
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Stone, 0));
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Stone, -3));
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void Count_OfANeverTouchedMaterial_IsZero()
    {
        stockpile.StartingAmounts = new List<MaterialAmount>();
        Assert.AreEqual(0, stockpile.Count(MaterialType.Stone));
    }

    [Test]
    public void Definitions_StoneResolves_WithANameAndColor()
    {
        Assert.IsTrue(stockpile.TryGetDefinition(MaterialType.Stone, out MaterialDefinition stone));
        Assert.AreEqual("Stone", stone.displayName);
        Assert.Greater(stone.color.a, 0f, "A visible color");
        Assert.AreEqual("Stone", stockpile.DisplayName(MaterialType.Stone));
    }

    [Test]
    public void Definitions_AreConstructibleInCode_AndAMissingDefinitionIsHarmless()
    {
        stockpile.Definitions = new List<MaterialDefinition> { new MaterialDefinition(MaterialType.Stone, "Granite", Color.red) };
        Assert.IsTrue(stockpile.TryGetDefinition(MaterialType.Stone, out MaterialDefinition d));
        Assert.AreEqual("Granite", d.displayName);
        Assert.AreEqual(Color.red, d.color);

        stockpile.Definitions = new List<MaterialDefinition>();
        Assert.IsFalse(stockpile.TryGetDefinition(MaterialType.Stone, out MaterialDefinition none));
        Assert.IsNull(none);
        Assert.AreEqual("Stone", stockpile.DisplayName(MaterialType.Stone), "Falls back to the enum name");
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone), "Counts don't depend on a definition");
    }
}
