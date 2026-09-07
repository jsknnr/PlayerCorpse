# Changelog

## 1.15.1 (2026-09-07)

- Runs on Vintage Story 1.22.0 and newer again. 1.15.0 declared 1.22.7 as its minimum, which the code never actually needed.
- Fixed corpses that were visible but could not be clicked until a relog. The game only lists an entity in its chunk when the chunk is already loaded on the client, and a corpse that arrived a moment too early was drawn but invisible to the crosshair. Corpses now re-list themselves.
- Right-clicking a corpse with the compass in hand collects the corpse instead of starting a search.

## 1.15.0 (2026-09-07)

Requires Vintage Story 1.22.7 or newer. CommonLib is no longer needed.

Developed by jsknnr with Claude Fable 5.1 (Anthropic).

### Corpses

- A corpse now spawns the moment you die instead of when you respawn. Quitting to the menu, a server restart, or a crash while you are dead no longer loses your items.
- Being revived by another player collects your corpse back into your inventory automatically. Respawning leaves the corpse where you died.
- Items that only partly fit in your inventory while collecting are dropped at your feet instead of vanishing.
- Corpses are placed in the correct dimension.
- A player entity dying after its owner disconnected no longer throws an error.

### Corpse compass

- Finds your corpses anywhere in the world, including unloaded chunks.
- Shows whose corpse it is, the distance, the direction, and how far above or below you it lies while the compass is in either hand.
- Shift + right-click cycles through your corpses when you have more than one.
- The needle turns toward the target.
- The compass stays raised in your hand instead of dropping to your side.
- The off-hand no longer spams "No corpses found" in chat.
- Guiding particles always point at the latest result.

### /returnthings

- Works for players who are offline, as long as the server has seen them before.
- Two deaths within the same second no longer overwrite each other's saved inventory.
- Partial stacks are dropped rather than lost.

### Under the hood

- The CommonLib dependency is gone. An existing config file is converted to the new flat layout on first start and a `.bak` copy is kept beside it.
- The Harmony patch on revive is replaced by a vanilla entity behavior.
- The collection progress ring no longer registers one renderer per corpse.
- The compass no longer floods the server log with warnings about unloaded chunks.

## 1.14.1

- Fixed corpses that fell into water being impossible to retrieve.

## 1.14.0

- Removed the waypoint system; it conflicted with the game's own death waypoint and the Terminus teleporter.
- Added support for the vanilla revive feature.
- Fixed `/returnthings` being usable to duplicate items.

## 1.13.1

- First release of the fork for Vintage Story 1.22.
