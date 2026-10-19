using UnityEngine;

/// <summary>
/// Where a fueled producer draws its fuel (#143): global stock, never hauled. Fuel is named by an id from the power
/// table's row, so a new fuel is a new row, not new code. PlacementManager is the game's store.
/// </summary>
public interface IFuelStore
{
    /// <summary>How many units of this fuel are on hand now. 0 for a fuel the store doesn't know.</summary>
    int FuelCount(string fuelId);

    /// <summary>Spends one unit of this fuel. False (and nothing spent) when there's none.</summary>
    bool TrySpendFuel(string fuelId);
}

/// <summary>
/// A fueled producer (#143, the Furnace Generator): it supplies its PowerNode's watts while it burns, and it burns in
/// paid intervals. One unit of fuel from the global store buys secondsPerUnit of burn. When the paid time runs out it
/// buys the next interval on the spot, so a stocked furnace burns without a gap; when the store is empty it goes out
/// (supplies nothing) and retries every retrySeconds until a unit is there. A disabled furnace buys nothing and its paid
/// time doesn't drain: re-enabling resumes what's left. Spent fuel is gone (no refunds). It needs no imp.
/// Tick-driven like MushroomPlot: Update calls Tick with scaled time; tests call it directly.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Placeable))]
public class FurnaceGenerator : MonoBehaviour, IPowerSupplyGate
{
    [Header("Fuel")]
    [SerializeField] private string fuelId = "";            // The fuel store's key ("mushrooms"); set from the power row
    [SerializeField] private float secondsPerUnit = 12f;    // Burn bought by one unit
    [SerializeField] private float retrySeconds = 1f;       // While out of fuel, how often it tries to buy again

    public string FuelId => fuelId;
    public float SecondsPerUnit => secondsPerUnit;
    public float RetrySeconds => retrySeconds;

    /// <summary>The store fuel comes from; null = never any fuel.</summary>
    public IFuelStore Store { get; private set; }

    /// <summary>Paid burn time left in the current interval, in game seconds. 0 when out.</summary>
    public float PaidSecondsRemaining { get; private set; }

    /// <summary>Units this furnace has bought since it was placed (for the card and tests).</summary>
    public int UnitsBurned { get; private set; }

    public bool IsBuilt => GetComponent<Placeable>().IsBuilt;

    /// <summary>Enabled per its PowerNode (#143 toggle); true with no node.</summary>
    public bool IsEnabled => !TryGetComponent(out PowerNode node) || node.Enabled;

    /// <summary>Burning: built, enabled, and inside a paid interval. Its node supplies exactly then.</summary>
    public bool IsBurning => IsBuilt && IsEnabled && PaidSecondsRemaining > 0f;

    public bool IsSupplying => IsBurning;

    /// <summary>Fuel units in the store right now (0 with no store).</summary>
    public int FuelOnHand => Store != null ? Store.FuelCount(fuelId) : 0;

    /// <summary>Game minutes of burn this furnace has left from its paid time plus the store, if it alone drew on it.</summary>
    public float MinutesRemaining => BurnMinutes(FuelOnHand, secondsPerUnit, PaidSecondsRemaining);

    private float retryTimer;

    /// <summary>Called once by PlacementManager right after Placeable.Initialize.</summary>
    public void Initialize(IFuelStore store, string fuel, float seconds, float retry)
    {
        Store = store;
        fuelId = fuel ?? "";
        secondsPerUnit = Mathf.Max(0.01f, seconds);
        retrySeconds = Mathf.Max(0f, retry);
        PaidSecondsRemaining = 0f;
        UnitsBurned = 0;
        retryTimer = 0f;
    }

    /// <summary>Placeable.CompleteConstruction calls this: the finished furnace lights at once if there's fuel.</summary>
    public void OnConstructed()
    {
        PaidSecondsRemaining = 0f;
        retryTimer = 0f;
        if (IsEnabled) TryBuy();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Advances the burn by deltaTime (scaled). A site or a disabled furnace does nothing (paid time is kept). Burning,
    /// the paid time drains and, the moment it's spent, the next unit is bought (carrying any overshoot); out of fuel, a
    /// purchase is retried every retrySeconds.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsBuilt || !IsEnabled || deltaTime <= 0f) return;

        if (PaidSecondsRemaining <= 0f)
        {
            retryTimer -= deltaTime;
            if (retryTimer > 0f) return;
            if (!TryBuy()) retryTimer = retrySeconds;
            return;
        }

        PaidSecondsRemaining -= deltaTime;
        while (PaidSecondsRemaining <= 0f)
        {
            float overshoot = -PaidSecondsRemaining;
            PaidSecondsRemaining = 0f;
            if (!TryBuy())
            {
                retryTimer = retrySeconds; // Out: supply stops now; try again after the cadence
                return;
            }
            PaidSecondsRemaining -= overshoot; // The new interval started when the old one ended
        }
    }

    // Spends one unit for one interval. False when the store has none (or there's no store).
    private bool TryBuy()
    {
        if (Store == null || !Store.TrySpendFuel(fuelId)) return false;
        PaidSecondsRemaining += secondsPerUnit;
        UnitsBurned++;
        retryTimer = 0f;
        return true;
    }

    /// <summary>Minutes of burn from a store of fuel at secondsPerUnit, plus what's already paid for.</summary>
    public static float BurnMinutes(int fuelOnHand, float secondsPerUnit, float paidSeconds) =>
        (Mathf.Max(0, fuelOnHand) * Mathf.Max(0f, secondsPerUnit) + Mathf.Max(0f, paidSeconds)) / 60f;
}
