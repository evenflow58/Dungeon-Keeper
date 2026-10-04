using UnityEngine;

public enum HealthTeam
{
    Monster,
    Hero
}

/// <summary>
/// Hit points and team for anything that can be fought: goblins now, the hero and the Heart in Epic #5.
/// Pure data: Health never deactivates anything and fires no events. Responding to death is the owner's
/// job (Goblin deactivates itself; the Heart must stay visible for game-over logic to see it die).
/// Targeting reads Team, never component types.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [SerializeField] private int maxHealth = 10;
    [SerializeField] private HealthTeam team = HealthTeam.Monster;

    // CurrentHealth starts at MaxHealth. It's initialized lazily as well as in Awake because Awake doesn't
    // run for components added in EditMode tests; until the first hit, MaxHealth changes carry through.
    private int currentHealth;
    private bool initialized;
    private bool damaged;

    /// <summary>At least 1. Lowering it clamps CurrentHealth down; before any damage, CurrentHealth follows it.</summary>
    public int MaxHealth
    {
        get => maxHealth;
        set
        {
            maxHealth = Mathf.Max(1, value);
            if (!initialized) return; // The lazy init will pick up the new max
            currentHealth = damaged ? Mathf.Min(currentHealth, maxHealth) : maxHealth;
        }
    }

    public HealthTeam Team { get => team; set => team = value; }

    public int CurrentHealth
    {
        get
        {
            EnsureInitialized();
            return currentHealth;
        }
    }

    public bool IsDead => CurrentHealth <= 0;

    private void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>Reduces CurrentHealth, clamped at 0. No-op for amount ≤ 0 or when already dead.</summary>
    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead) return;
        damaged = true;
        currentHealth = Mathf.Max(0, currentHealth - amount);
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        currentHealth = maxHealth;
    }
}
