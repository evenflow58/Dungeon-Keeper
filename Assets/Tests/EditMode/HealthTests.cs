using NUnit.Framework;
using UnityEngine;

public class HealthTests
{
    private GameObject go;
    private Health health;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("TestHealth");
        health = go.AddComponent<Health>(); // Awake doesn't run in EditMode: exercises the lazy init
    }

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
    }

    [Test]
    public void Defaults_FullHealth_MonsterTeam()
    {
        Assert.AreEqual(10, health.MaxHealth);
        Assert.AreEqual(10, health.CurrentHealth);
        Assert.AreEqual(HealthTeam.Monster, health.Team);
        Assert.IsFalse(health.IsDead);
    }

    [Test]
    public void MaxHealthSetBeforeAnyRead_StartsFullAtNewMax()
    {
        health.MaxHealth = 3;
        Assert.AreEqual(3, health.CurrentHealth);
    }

    [Test]
    public void TakeDamage_Reduces_ClampsAtZero_DeadExactlyAtZero()
    {
        health.MaxHealth = 5;

        health.TakeDamage(2);
        Assert.AreEqual(3, health.CurrentHealth);
        health.TakeDamage(2);
        Assert.AreEqual(1, health.CurrentHealth);
        Assert.IsFalse(health.IsDead, "1 HP is alive");

        health.TakeDamage(7);
        Assert.AreEqual(0, health.CurrentHealth, "Clamped, never negative");
        Assert.IsTrue(health.IsDead);
    }

    [Test]
    public void TakeDamage_ExactlyToZero_IsDead()
    {
        health.MaxHealth = 4;
        health.TakeDamage(4);
        Assert.AreEqual(0, health.CurrentHealth);
        Assert.IsTrue(health.IsDead);
    }

    [Test]
    public void TakeDamage_ZeroOrNegative_IsNoOp()
    {
        health.TakeDamage(0);
        health.TakeDamage(-5);
        Assert.AreEqual(10, health.CurrentHealth);
    }

    [Test]
    public void TakeDamage_WhenDead_IsNoOp()
    {
        health.TakeDamage(10);
        Assert.IsTrue(health.IsDead);

        health.TakeDamage(3);
        Assert.AreEqual(0, health.CurrentHealth);
        Assert.IsTrue(health.IsDead);
    }

    [Test]
    public void MaxHealth_BelowOne_ClampsToOne()
    {
        health.MaxHealth = 0;
        Assert.AreEqual(1, health.MaxHealth);
        health.MaxHealth = -4;
        Assert.AreEqual(1, health.MaxHealth);
        Assert.AreEqual(1, health.CurrentHealth);
    }

    [Test]
    public void MaxHealth_Lowered_ClampsCurrentDown()
    {
        health.TakeDamage(2); // 8 / 10
        health.MaxHealth = 5;

        Assert.AreEqual(5, health.MaxHealth);
        Assert.AreEqual(5, health.CurrentHealth);
    }

    [Test]
    public void MaxHealth_RaisedBeforeDamage_CurrentFollows_AfterDamage_CurrentStays()
    {
        Assert.AreEqual(10, health.CurrentHealth); // initialized
        health.MaxHealth = 20;
        Assert.AreEqual(20, health.CurrentHealth, "Untouched: still full at the new max");

        health.TakeDamage(5); // 15 / 20
        health.MaxHealth = 30;
        Assert.AreEqual(15, health.CurrentHealth, "Raising the max doesn't heal");
    }

    [Test]
    public void Team_RoundTrips()
    {
        health.Team = HealthTeam.Hero;
        Assert.AreEqual(HealthTeam.Hero, health.Team);
        health.Team = HealthTeam.Monster;
        Assert.AreEqual(HealthTeam.Monster, health.Team);
    }

    [Test]
    public void Death_DoesNotDeactivateAnything()
    {
        health.TakeDamage(100);
        Assert.IsTrue(health.IsDead);
        Assert.IsTrue(go.activeSelf, "Health is pure data; responding to death is the owner's job");
    }
}
