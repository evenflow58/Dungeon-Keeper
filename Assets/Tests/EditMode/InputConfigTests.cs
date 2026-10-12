using System;
using NUnit.Framework;
using UnityEngine;

public class InputConfigTests
{
    private const string TestKey = "DungeonKeeper.BindingOverrides.EditModeTest";
    private string savedKey;

    [SetUp]
    public void SetUp()
    {
        // Never touch the editor's real stored bindings: point InputConfig at a test-only key.
        savedKey = InputConfig.PrefsKey;
        InputConfig.PrefsKey = TestKey;
        PlayerPrefs.DeleteKey(TestKey);
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.DeleteKey(TestKey);
        PlayerPrefs.Save();
        InputConfig.PrefsKey = savedKey;
    }

    private static readonly string[] PanDefaults =
    {
        "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d",
        "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow",
    };

    private static void AssertDefaultTable(InputConfig.Actions a)
    {
        CollectionAssert.AreEqual(PanDefaults, InputConfig.EffectivePaths(a.Pan), "Pan: WASD and arrow keys");
        CollectionAssert.AreEqual(new[] { "<Mouse>/middleButton" }, InputConfig.EffectivePaths(a.PanDrag));
        CollectionAssert.AreEqual(new[] { "<Mouse>/scroll" }, InputConfig.EffectivePaths(a.Zoom));
        CollectionAssert.AreEqual(new[] { "<Mouse>/position" }, InputConfig.EffectivePaths(a.PointerPosition));
        CollectionAssert.AreEqual(new[] { "<Mouse>/leftButton" }, InputConfig.EffectivePaths(a.Designate));
        CollectionAssert.AreEqual(new[] { "<Mouse>/rightButton" }, InputConfig.EffectivePaths(a.ClearOrAbort));
        CollectionAssert.AreEqual(new[] { "<Keyboard>/escape" }, InputConfig.EffectivePaths(a.Abort));
        CollectionAssert.AreEqual(new[] { "<Keyboard>/space" }, InputConfig.EffectivePaths(a.TogglePause)); // #124
        for (int i = 0; i < 6; i++) // #123: the build rail's digits
            CollectionAssert.AreEqual(new[] { $"<Keyboard>/{i + 1}" }, InputConfig.EffectivePaths(RailActions(a)[i]), $"Rail{i + 1}");
    }

    private static UnityEngine.InputSystem.InputAction[] RailActions(InputConfig.Actions a) =>
        new[] { a.Rail1, a.Rail2, a.Rail3, a.Rail4, a.Rail5, a.Rail6 };

    [Test]
    public void TogglePause_ExistsByName_DefaultsToSpace_WithAStableId()
    {
        Assert.AreEqual("TogglePause", InputConfig.TogglePause);
        using (var a = InputConfig.CreateActions())
        using (var b = InputConfig.CreateActions())
        {
            Assert.AreSame(a.Map[InputConfig.TogglePause], a.TogglePause);
            Assert.AreEqual(UnityEngine.InputSystem.InputActionType.Button, a.TogglePause.type);
            CollectionAssert.AreEqual(new[] { "<Keyboard>/space" }, InputConfig.EffectivePaths(a.TogglePause));
            Assert.AreEqual(a.TogglePause.bindings[0].id, b.TogglePause.bindings[0].id, "Same id in both builds");
            Assert.AreNotEqual(a.TogglePause.bindings[0].id, a.Abort.bindings[0].id);
        }

        InputConfig.SetOverride(InputConfig.TogglePause, "<Keyboard>/p");
        using (var a = InputConfig.CreateActions())
            CollectionAssert.AreEqual(new[] { "<Keyboard>/p" }, InputConfig.EffectivePaths(a.TogglePause));
    }

    [Test]
    public void RailDigits_ExistByName_WithStableDistinctIds_AndRebindLikeAnyOther()
    {
        string[] names = { InputConfig.Rail1, InputConfig.Rail2, InputConfig.Rail3, InputConfig.Rail4, InputConfig.Rail5, InputConfig.Rail6 };
        using (var a = InputConfig.CreateActions())
        using (var b = InputConfig.CreateActions())
        {
            var ids = new System.Collections.Generic.HashSet<Guid>();
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual($"Rail{i + 1}", names[i]);
                Assert.AreSame(a.Map[names[i]], RailActions(a)[i]);
                Assert.AreEqual(UnityEngine.InputSystem.InputActionType.Button, RailActions(a)[i].type);
                Assert.AreEqual(RailActions(a)[i].bindings[0].id, RailActions(b)[i].bindings[0].id, $"Rail{i + 1}: same id in both builds");
                Assert.IsTrue(ids.Add(RailActions(a)[i].bindings[0].id), "Distinct per digit");
            }
            Assert.IsFalse(ids.Contains(a.Abort.bindings[0].id));
        }

        InputConfig.SetOverride(InputConfig.Rail2, "<Keyboard>/b");
        using (var a = InputConfig.CreateActions())
        {
            CollectionAssert.AreEqual(new[] { "<Keyboard>/b" }, InputConfig.EffectivePaths(a.Rail2));
            CollectionAssert.AreEqual(new[] { "<Keyboard>/1" }, InputConfig.EffectivePaths(a.Rail1), "The others keep their digits");
        }
    }

    [Test]
    public void Defaults_MatchTheTicketTable()
    {
        using (var a = InputConfig.CreateActions())
        {
            AssertDefaultTable(a);
            Assert.AreEqual(2, CountComposites(a.Pan), "Pan holds two 2DVector composites");
        }
    }

    private static int CountComposites(UnityEngine.InputSystem.InputAction action)
    {
        int n = 0;
        foreach (var b in action.bindings)
            if (b.isComposite) { n++; Assert.AreEqual("2DVector", b.path); }
        return n;
    }

    [Test]
    public void Override_ChangesThatBindingOnly()
    {
        InputConfig.SetOverride(InputConfig.Abort, "<Keyboard>/q");

        using (var a = InputConfig.CreateActions())
        {
            CollectionAssert.AreEqual(new[] { "<Keyboard>/q" }, InputConfig.EffectivePaths(a.Abort));
            CollectionAssert.AreEqual(new[] { "<Mouse>/rightButton" }, InputConfig.EffectivePaths(a.ClearOrAbort));
            CollectionAssert.AreEqual(new[] { "<Mouse>/leftButton" }, InputConfig.EffectivePaths(a.Designate));
            CollectionAssert.AreEqual(PanDefaults, InputConfig.EffectivePaths(a.Pan));
        }
    }

    [Test]
    public void Override_CompositePart()
    {
        InputConfig.SetOverride(InputConfig.Pan, "<Keyboard>/i", bindingIndex: 1); // WASD composite's Up part

        using (var a = InputConfig.CreateActions())
        {
            var paths = InputConfig.EffectivePaths(a.Pan);
            Assert.AreEqual("<Keyboard>/i", paths[0], "W rebound");
            CollectionAssert.AreEqual(new[] { "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d" }, paths.GetRange(1, 3));
            CollectionAssert.AreEqual(new[] { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" },
                paths.GetRange(4, 4), "Arrow keys untouched");
        }
    }

    [Test]
    public void Persistence_OverrideIsStoredInPlayerPrefs_AndReloads()
    {
        InputConfig.SetOverride(InputConfig.Abort, "<Keyboard>/q");

        string json = PlayerPrefs.GetString(TestKey, "");
        Assert.IsNotEmpty(json, "Saved to PlayerPrefs");
        StringAssert.Contains("<Keyboard>/q", json);

        // Nothing is cached in memory: every new set loads from PlayerPrefs. Two later "sessions" both see it.
        using (var first = InputConfig.CreateActions())
        using (var second = InputConfig.CreateActions())
        {
            Assert.AreEqual("<Keyboard>/q", first.Abort.bindings[0].effectivePath);
            Assert.AreEqual("<Keyboard>/q", second.Abort.bindings[0].effectivePath);
        }
    }

    [Test]
    public void BindingIds_AreStableAcrossCreations()
    {
        // Saved overrides match bindings by id, so fresh sets must reuse the same ids.
        using (var a = InputConfig.CreateActions())
        using (var b = InputConfig.CreateActions())
        {
            for (int i = 0; i < a.Pan.bindings.Count; i++)
                Assert.AreEqual(a.Pan.bindings[i].id, b.Pan.bindings[i].id);
            Assert.AreEqual(a.Abort.bindings[0].id, b.Abort.bindings[0].id);
            Assert.AreNotEqual(a.Abort.bindings[0].id, a.Designate.bindings[0].id, "Distinct per binding");
        }
    }

    [Test]
    public void ResetToDefaults_RestoresTheTable()
    {
        InputConfig.SetOverride(InputConfig.Abort, "<Keyboard>/q");
        InputConfig.SetOverride(InputConfig.Designate, "<Mouse>/forwardButton");

        InputConfig.ResetToDefaults();

        Assert.IsFalse(PlayerPrefs.HasKey(TestKey));
        using (var a = InputConfig.CreateActions()) AssertDefaultTable(a);
    }

    [Test]
    public void TwoConsumers_GetTheSameOverrides_IndependentSets()
    {
        InputConfig.SetOverride(InputConfig.Abort, "<Keyboard>/q");

        using (var dig = InputConfig.CreateActions())
        using (var place = InputConfig.CreateActions())
        {
            Assert.AreEqual("<Keyboard>/q", dig.Abort.bindings[0].effectivePath);
            Assert.AreEqual("<Keyboard>/q", place.Abort.bindings[0].effectivePath);

            // Per-consumer enable/disable stays per-consumer (DigDesignator vs PlacementController arbitration).
            dig.Enable();
            Assert.IsTrue(dig.Abort.enabled);
            Assert.IsFalse(place.Abort.enabled);
            dig.Disable();
        }
    }

    [Test]
    public void Overrides_AccumulateAcrossCalls()
    {
        InputConfig.SetOverride(InputConfig.Abort, "<Keyboard>/q");
        InputConfig.SetOverride(InputConfig.ClearOrAbort, "<Mouse>/backButton");

        using (var a = InputConfig.CreateActions())
        {
            Assert.AreEqual("<Keyboard>/q", a.Abort.bindings[0].effectivePath, "First override kept");
            Assert.AreEqual("<Mouse>/backButton", a.ClearOrAbort.bindings[0].effectivePath);
        }
    }

    [Test]
    public void UnknownAction_Throws()
    {
        Assert.Throws<ArgumentException>(() => InputConfig.SetOverride("NoSuchAction", "<Keyboard>/q"));
        Assert.IsFalse(PlayerPrefs.HasKey(TestKey));
    }
}
