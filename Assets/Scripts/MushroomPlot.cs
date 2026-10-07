using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mushroom Plot behavior: grows 1 food per growthSecondsPerFood up to capacity, shown as a row of small
/// sphere pips across the front of the plot's mound (ticket #77; pip i shows exactly when FoodCount > i). Consumers take food through TryTakeFood(). Time spent at capacity is discarded
/// (no banking): after a take from a full plot, the next food needs a full fresh interval.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class MushroomPlot : MonoBehaviour
{
    [Header("Growth")]
    [SerializeField] private float growthSecondsPerFood = 30f;
    [SerializeField] private int capacity = 5;

    [Header("Food Pips (code-built spheres, ticket #77)")]
    [SerializeField] private Color pipColor = new Color(0.95f, 0.90f, 0.70f, 1f); // Light warm white
    [SerializeField] private float pipDiameter = 0.13f;                           // Tiles
    [SerializeField] private float pipSpacing = 0.165f;                           // Center to center, tiles: a clear gap between pips
    [SerializeField] private float pipRowFront = 0.45f;                           // Row depth: 0 = mound center, 1 = its front rim
    [SerializeField] private float pipSmoothness = 0.35f;                         // A little shine so they pop off the soil

    public float GrowthSecondsPerFood { get => growthSecondsPerFood; set => growthSecondsPerFood = value; }
    public int Capacity { get => capacity; set => capacity = value; }

    public Vector2Int Tile => GetComponent<Placeable>().Tile;

    public int FoodCount { get; private set; }

    /// <summary>Progress in seconds toward the next food. Held at 0 while at capacity.</summary>
    public float GrowthElapsed { get; private set; }

    private readonly List<MeshRenderer> pips = new List<MeshRenderer>();

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(float growthSeconds, int cap)
    {
        growthSecondsPerFood = growthSeconds;
        capacity = cap;
        FoodCount = 0;
        GrowthElapsed = 0f;
        RefreshPips();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances growth by deltaTime (scaled time). Called from Update; public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (FoodCount >= capacity)
        {
            GrowthElapsed = 0f; // No banking while full
            return;
        }

        GrowthElapsed += deltaTime;
        int before = FoodCount;
        while (GrowthElapsed >= growthSecondsPerFood && FoodCount < capacity)
        {
            FoodCount++;
            GrowthElapsed -= growthSecondsPerFood;
        }
        if (FoodCount >= capacity) GrowthElapsed = 0f; // Leftover past the cap is discarded

        if (FoodCount != before) RefreshPips();
    }

    /// <summary>Takes one food. False when empty. Doesn't touch growth progress.</summary>
    public bool TryTakeFood()
    {
        if (FoodCount == 0) return false;
        FoodCount--;
        RefreshPips();
        return true;
    }

    /// <summary>
    /// Ensures exactly Capacity pips exist in a row across the mound's front, with pip i visible iff i &lt; FoodCount.
    /// </summary>
    public void RefreshPips()
    {
        int target = Mathf.Max(0, capacity);

        while (pips.Count > target)
        {
            MeshRenderer extra = pips[pips.Count - 1];
            pips.RemoveAt(pips.Count - 1);
            if (extra == null) continue;
            if (Application.isPlaying) Destroy(extra.gameObject);
            else DestroyImmediate(extra.gameObject);
        }
        while (pips.Count < target) pips.Add(CreatePip(pips.Count));

        // Spread along the mound built at the placeable's size (a full tile when there's no Placeable, e.g. tests).
        float size = TryGetComponent(out Placeable placeable) && placeable.Size > 0f ? placeable.Size : 1f;

        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] == null) pips[i] = CreatePip(i);
            pips[i].transform.localPosition = PropModel.PipPosition(i, target, size, pipRowFront, pipDiameter, pipSpacing);
            pips[i].enabled = i < FoodCount;
        }
    }

    /// <summary>Number of pips currently shown (the visible count).</summary>
    public int VisiblePipCount
    {
        get
        {
            int n = 0;
            foreach (MeshRenderer p in pips) if (p != null && p.enabled) n++;
            return n;
        }
    }

    private MeshRenderer CreatePip(int index)
    {
        var part = new CreatureModel.Part("FoodPip" + index, CreatureModel.Shape.Sphere, Vector3.zero,
            Vector3.one * pipDiameter, pipColor);
        MeshRenderer pip = CreatureModel.CreatePart(transform, part, pipSmoothness);
        pip.enabled = false;
        return pip;
    }
}
