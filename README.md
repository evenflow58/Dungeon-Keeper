# Dungeon-Keeper

## Accessibility

Accessibility is cheaper to design in than to retrofit, so this project treats it as a coding input, not a final polish pass. The audit is tracked in #31. When writing or reviewing code, follow these guidelines:

- **Never encode information in color alone.** Tile states, warnings, and selections need a second channel — shape, pattern, icon, or text — so the game stays readable with color-vision deficiency. Check new colors against the existing palette for distinguishability, not just looks.
- **Route all player input through the shared binding configuration** (see #30). Never make a hardcoded key or button the only way to perform an action.
- **Keep UI text legible and scalable.** Avoid tiny, fixed-size text; new UI should tolerate larger text settings when the UI shell lands.
- **No rapid flashing or strobing effects**, and keep screen shake and other strong motion effects optional — juice included.
- **Pair critical cues across senses.** Important sounds need a visual counterpart; important visual events shouldn't rely on audio alone to be noticed.
- **Forgive imprecise input.** Where a feature demands tile-accurate pointing today, note it rather than raising the bar — alternatives are audit follow-ups.
