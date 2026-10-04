using UnityEngine;

/// <summary>
/// Spike Trap behavior: Armed or Spent. An armed trap fires once when a hero steps on its tile
/// (TryTrigger), dealing Damage and becoming Spent. A spent trap never re-fires until the imp rearms it.
/// Placeholder visual: a steel diamond ("spikes up") shown iff Armed; Spent shows the flat plate only.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class SpikeTrap : MonoBehaviour
{
    [Header("Trap")]
    [SerializeField] private int damage = 1; // Provisional: hero HP lands with Epic #5

    [Header("Spikes (Placeholder Art)")]
    [SerializeField] private Color spikeColor = new Color(0.85f, 0.87f, 0.90f, 1f); // Light steel
    [SerializeField] private float spikeSize = 0.45f;                               // Fraction of a tile
    [SerializeField] private float spikeRotation = 45f;                             // Square turned into a diamond
    [SerializeField] private Vector3 spikeOffset = new Vector3(0f, 0.02f, -0.15f);   // Local offset from tile center
    [SerializeField] private int spikeSortingOrder = 2;                             // Above the trap body (1)

    public int Damage { get => damage; set => damage = value; }

    public Vector2Int Tile => GetComponent<Placeable>().Tile;

    public bool IsArmed { get; private set; }

    /// <summary>True while the spike sprite is shown (Armed).</summary>
    public bool SpikesVisible => spikes != null && spikes.enabled;

    private SpriteRenderer spikes;

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(int spikeDamage)
    {
        damage = spikeDamage;
        IsArmed = true;
        RefreshVisual();
    }

    /// <summary>
    /// The step-on seam a hero calls when it enters this tile. Armed: deals Damage, becomes Spent, returns true.
    /// Spent: deals nothing, returns false.
    /// </summary>
    public bool TryTrigger(out int damageDealt)
    {
        if (!IsArmed)
        {
            damageDealt = 0;
            return false;
        }

        damageDealt = damage;
        IsArmed = false;
        RefreshVisual();
        return true;
    }

    /// <summary>Returns the trap to Armed. Idempotent.</summary>
    public void Rearm()
    {
        IsArmed = true;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (spikes == null) spikes = CreateSpikes();
        spikes.enabled = IsArmed;
    }

    private SpriteRenderer CreateSpikes()
    {
        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "Spikes_Texture";
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "Spikes_Sprite";

        var go = new GameObject("Spikes");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = spikeOffset;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, spikeRotation);
        go.transform.localScale = new Vector3(spikeSize, spikeSize, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = spikeColor;
        sr.sortingOrder = spikeSortingOrder;
        return sr;
    }
}
