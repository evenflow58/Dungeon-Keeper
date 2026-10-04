using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mushroom Plot behavior: grows 1 food per growthSecondsPerFood up to capacity, shown as a row of
/// pips on the plot. Consumers take food through TryTakeFood(). Time spent at capacity is discarded
/// (no banking): after a take from a full plot, the next food needs a full fresh interval.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class MushroomPlot : MonoBehaviour
{
    [Header("Growth")]
    [SerializeField] private float growthSecondsPerFood = 30f;
    [SerializeField] private int capacity = 5;

    [Header("Food Pips (Placeholder Art)")]
    [SerializeField] private Color pipColor = new Color(0.95f, 0.90f, 0.70f, 1f); // Light warm white
    [SerializeField] private float pipSize = 0.18f;                               // Fraction of a tile; legible at default zoom
    [SerializeField] private float pipSpacing = 0.20f;                            // 5 pips span ~0.98 of a tile
    [SerializeField] private float pipRowY = -0.28f;                              // Local offset from tile center
    [SerializeField] private int pipSortingOrder = 2;                             // Above the plot body (1)

    public float GrowthSecondsPerFood { get => growthSecondsPerFood; set => growthSecondsPerFood = value; }
    public int Capacity { get => capacity; set => capacity = value; }

    public int FoodCount { get; private set; }

    /// <summary>Progress in seconds toward the next food. Held at 0 while at capacity.</summary>
    public float GrowthElapsed { get; private set; }

    private readonly List<SpriteRenderer> pips = new List<SpriteRenderer>();
    private Sprite pipSprite;

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
    /// Ensures exactly Capacity pips exist in a centered row, with pip i visible iff i &lt; FoodCount.
    /// </summary>
    public void RefreshPips()
    {
        int target = Mathf.Max(0, capacity);

        while (pips.Count > target)
        {
            SpriteRenderer extra = pips[pips.Count - 1];
            pips.RemoveAt(pips.Count - 1);
            if (extra == null) continue;
            if (Application.isPlaying) Destroy(extra.gameObject);
            else DestroyImmediate(extra.gameObject);
        }
        while (pips.Count < target) pips.Add(CreatePip(pips.Count));

        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] == null) pips[i] = CreatePip(i);
            float x = (i - (target - 1) * 0.5f) * pipSpacing;
            pips[i].transform.localPosition = new Vector3(x, pipRowY, -0.15f);
            pips[i].enabled = i < FoodCount;
        }
    }

    /// <summary>Number of pips currently shown (the visible count).</summary>
    public int VisiblePipCount
    {
        get
        {
            int n = 0;
            foreach (SpriteRenderer p in pips) if (p != null && p.enabled) n++;
            return n;
        }
    }

    private SpriteRenderer CreatePip(int index)
    {
        if (pipSprite == null)
        {
            const int res = 16;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.name = "FoodPip_Texture";
            var pixels = new Color[res * res];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            pipSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
            pipSprite.name = "FoodPip_Sprite";
        }

        var go = new GameObject("FoodPip" + index);
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(pipSize, pipSize, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = pipSprite;
        sr.color = pipColor;
        sr.sortingOrder = pipSortingOrder;
        sr.enabled = false;
        return sr;
    }
}
