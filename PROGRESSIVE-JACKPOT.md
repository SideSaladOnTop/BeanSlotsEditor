# BeanSlots progressive jackpot (v1.1.20 / Editor v2.3)

- Mark selected prizes as `Jackpot: true` in the prize editor. Their `Chance` is a **relative weight within the jackpot-only pool**, not a regular win percentage.
- A normal spin first rolls against the standalone jackpot probability, then (if the jackpot roll fails) rolls against the ordinary-prize chance. Jackpot items are never part of the ordinary pool.
- `ServerWidePity.Enabled` must be `true` to allow natural jackpot wins; disabled jackpots remain available via permission-protected forced test spins.
- `ServerWideJackpotStreak` in oxide/data/BeanSlots.json increases on each real non-jackpot spin, regardless of whether it wins an ordinary prize. Only a successfully granted jackpot resets the jackpot streak. Admin forced tests do not change either streak.
- `ServerWideLossStreak` separately tracks consecutive real losses for ordinary pity, and resets after wins depending on `ResetStreakOnAnyWin`.
- The jackpot chance formula is `min(MaximumJackpotChance, JackpotBaseChance + ServerWideJackpotStreak * JackpotChanceIncreasePerSpin)` in percent (also capped to 100).
- If a jackpot wins, individual jackpot item chance = the global jackpot hit chance multiplied by item weight / total eligible jackpot weight.
- `ShowLossStreak` and `ShowJackpotChance` optionally display these server-wide values in the spin UI.
- The editor's slider predicts jackpots from the current selected items and settings; it doesn't change the live server counter.
- Existing deprecated jackpot weight settings are removed when you save the config in this editor.
