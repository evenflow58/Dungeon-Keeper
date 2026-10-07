using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class PropModelTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);

    private GameObject managerGo;
    private GameObject heartGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
    }

    [TearDown]
    public void TearDown()
    {
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Placeable Place(PlaceableType type, int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), ImpTile, out Placeable p));
        return p;
    }

    private static string[] PartNames(GameObject model) =>
        model.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.gameObject.name).ToArray();

    private static float MinY(GameObject go)
    {
        float min = float.MaxValue;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) min = Mathf.Min(min, r.bounds.min.y);
        return min;
    }

    // --- Recipes ---

    [Test]
    public void Cot_IsAFrameMattressAndPillow_InTheCotColors()
    {
        Placeable cot = Place(PlaceableType.LairCot, 22, 14);

        Assert.AreEqual("LairCotModel", cot.Model.name);
        CollectionAssert.AreEqual(new[] { "Frame", "Mattress", "Pillow" }, PartNames(cot.Model));
        Material mattress = cot.Model.transform.Find("Mattress").GetComponent<MeshRenderer>().sharedMaterial;
        Assert.AreSame(CreatureModel.MaterialFor(manager.ColorFor(PlaceableType.LairCot), 0.2f), mattress, "The cot's color is its mattress");
        Assert.AreEqual(0f, MinY(cot.Model) - cot.transform.position.y, 1e-3f, "Stands on the ground point");
        Assert.AreEqual(PropModel.CotSurfaceHeight, cot.SurfaceHeight);
    }

    [Test]
    public void Plot_IsAMoundWithThreeMushrooms_CapsInThePlotColor()
    {
        Placeable plot = Place(PlaceableType.MushroomPlot, 22, 14);

        CollectionAssert.AreEqual(new[] { "Mound", "Stem0", "Cap0", "Stem1", "Cap1", "Stem2", "Cap2" }, PartNames(plot.Model));
        Material cap0 = plot.Model.transform.Find("Cap0").GetComponent<MeshRenderer>().sharedMaterial;
        Material cap2 = plot.Model.transform.Find("Cap2").GetComponent<MeshRenderer>().sharedMaterial;
        Assert.AreSame(cap0, cap2, "Shared per color");
        Assert.AreSame(CreatureModel.MaterialFor(manager.ColorFor(PlaceableType.MushroomPlot), 0.2f), cap0);
    }

    [Test]
    public void Trap_IsAPlateWithAGridOfSpikes()
    {
        Placeable trap = Place(PlaceableType.SpikeTrap, 22, 14);

        CollectionAssert.AreEqual(new[] { "Plate" }, PartNames(trap.Model));
        Transform spikes = trap.GetComponent<SpikeTrap>().Spikes;
        Assert.AreEqual(9, spikes.childCount, "3 x 3 cone spikes");
        Assert.AreEqual(PropModel.TrapPlateHeight, spikes.localPosition.y, 1e-6f, "On the plate's top");
        Assert.AreEqual(CreatureModel.MeshFor(CreatureModel.Shape.Cone), spikes.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
    }

    [Test]
    public void AllPlaceableParts_CastAndReceiveShadows_NoSpritesLeft()
    {
        Placeable cot = Place(PlaceableType.LairCot, 22, 14);
        Placeable plot = Place(PlaceableType.MushroomPlot, 23, 14);
        Placeable trap = Place(PlaceableType.SpikeTrap, 25, 14);

        foreach (Placeable p in new[] { cot, plot, trap })
        {
            Assert.AreEqual(0, p.GetComponentsInChildren<SpriteRenderer>(true).Length, p.Type + ": no sprite body, no ground disc");
            foreach (MeshRenderer r in p.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, p.Type + " " + r.name);
                Assert.IsTrue(r.receiveShadows, p.Type + " " + r.name);
            }
        }
    }

    // --- State: pips follow food, spikes follow armed/spent ---

    [Test]
    public void Pips_AreSpheres_OneShownPerFood_AtZeroMidAndCap()
    {
        Placeable placeable = Place(PlaceableType.MushroomPlot, 22, 14);
        MushroomPlot plot = placeable.GetComponent<MushroomPlot>();

        Assert.AreEqual(0, plot.VisiblePipCount, "Empty");
        plot.Tick(plot.GrowthSecondsPerFood * 2f);
        Assert.AreEqual(2, plot.VisiblePipCount, "Mid");
        plot.Tick(plot.GrowthSecondsPerFood * 10f);
        Assert.AreEqual(5, plot.VisiblePipCount, "At the cap of 5");

        MeshRenderer[] pips = placeable.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("FoodPip")).ToArray();
        Assert.AreEqual(5, pips.Length);
        foreach (MeshRenderer pip in pips)
        {
            Assert.AreEqual(CreatureModel.MeshFor(CreatureModel.Shape.Sphere), pip.GetComponent<MeshFilter>().sharedMesh, pip.name);
            Assert.Less(pip.transform.localPosition.z, 0f, pip.name + " on the mound's front, toward the camera");
            Assert.Greater(pip.transform.localPosition.y, 0f, pip.name + " on top of the mound");
        }
        Assert.AreEqual(5, pips.Select(p => p.transform.localPosition).Distinct().Count(), "Spread out, not stacked");
        float[] xs = pips.Select(p => p.transform.localPosition.x).OrderBy(x => x).ToArray();
        for (int i = 1; i < xs.Length; i++)
            Assert.Greater(xs[i] - xs[i - 1], 0.13f, "A visible gap between neighbors (wider than a pip)");

        Assert.IsTrue(plot.TryTakeFood());
        Assert.AreEqual(4, plot.VisiblePipCount, "Eating takes a pip away");
        Assert.IsFalse(pips[4].enabled, "The last pip is the one that goes");
    }

    [Test]
    public void Spikes_RaisedWhenArmed_RetractedWhenSpent_RaisedAgainOnRearm()
    {
        SpikeTrap trap = Place(PlaceableType.SpikeTrap, 22, 14).GetComponent<SpikeTrap>();

        Assert.IsTrue(trap.IsArmed);
        Assert.AreEqual(1f, trap.Spikes.localScale.y, 1e-6f, "Armed: spikes up");

        Assert.IsTrue(trap.TryTrigger(out _));
        Assert.IsFalse(trap.SpikesVisible);
        Assert.Less(trap.Spikes.localScale.y, 0.2f, "Spent: retracted nearly flat");

        trap.Rearm();
        Assert.IsTrue(trap.SpikesVisible);
        Assert.AreEqual(1f, trap.Spikes.localScale.y, 1e-6f, "Rearmed: up again");
    }

    // --- The Heart ---

    [Test]
    public void Heart_IsAnEmissiveCrystalOnAPlinth()
    {
        heartGo = new GameObject("TestHeart");
        Heart heart = heartGo.AddComponent<Heart>();
        heart.CreateModel();

        Assert.IsNotNull(heart.Model);
        CollectionAssert.AreEqual(new[] { "Plinth", "Crystal" }, PartNames(heart.Model));
        Material crystal = heart.Model.transform.Find("Crystal").GetComponent<MeshRenderer>().sharedMaterial;
        Assert.IsTrue(crystal.IsKeywordEnabled("_EMISSION"), "Glows");
        Color emission = crystal.GetColor("_EmissionColor");
        Assert.Greater(emission.r, 0.1f, "It glows");
        Assert.Greater(emission.r, emission.g * 3f, "Red-dominant glow");
        Assert.AreEqual(CreatureModel.MeshFor(CreatureModel.Shape.Octahedron), heart.Model.transform.Find("Crystal").GetComponent<MeshFilter>().sharedMesh);
        Assert.IsFalse(heart.Model.transform.Find("Plinth").GetComponent<MeshRenderer>().sharedMaterial.IsKeywordEnabled("_EMISSION"), "Only the crystal glows");

        Bounds b = heart.Model.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach (Renderer r in heart.Model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        Assert.AreEqual(0f, b.min.y, 1e-3f, "On the ground");
        Assert.AreEqual(1.2f, b.max.y, 0.02f, "About 1.2 tiles tall");
        Assert.AreEqual(0, heartGo.GetComponentsInChildren<SpriteRenderer>(true).Length, "No sprite, no disc");

        GameObject first = heart.Model;
        heart.CreateModel();
        Assert.AreSame(first, heart.Model, "Built once");
    }

    [Test]
    public void Octahedron_FacesOutward_BaseAtItsOrigin()
    {
        Mesh mesh = CreatureModel.MeshFor(CreatureModel.Shape.Octahedron);
        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        int[] t = mesh.triangles;
        Assert.AreEqual(8, t.Length / 3, "Eight facets");
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Vector3 centroid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
            Assert.Greater(Vector3.Dot(cross, n[t[i]]), 0f, $"Facet {i / 3} normal matches its winding");
            Assert.Greater(Vector3.Dot(n[t[i]], centroid - new Vector3(0f, 0.5f, 0f)), 0f, $"Facet {i / 3} faces outward");
        }
        Assert.AreEqual(0f, mesh.bounds.min.y, 1e-5f);
        Assert.AreEqual(1f, mesh.bounds.max.y, 1e-5f);
    }
}
