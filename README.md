# PlayerCorpse-Forked

Fork of [PlayerCorpse](https://github.com/DArkHekRoMaNT/PlayerCorpse) by DArkHekRoMaNT, updated for Vintage Story 1.22 / .NET 10.

### Links: [ModDB](https://mods.vintagestory.at/playercorpseforked)

## Configuration

The server writes `ModConfig/playercorpseforked.json` on first start. Edit it and restart the server; clients need nothing installed beyond the mod itself.

A config file written by an older version that used CommonLib is converted automatically on first load (a `.bak` copy is kept next to it).

| Option | Default | Description |
| --- | --- | --- |
| `CanFired` | `false` | Corpse burns in fire/lava. |
| `HasHealth` | `false` | Corpse has 100 hp and can be broken by another player. |
| `CreateCorpse` | `true` | If `false`, items are dropped at the death location instead of creating a corpse. |
| `SaveInventoryTypes` | hotbar, backpack, craftinggrid, mouse, character | Player inventory class names whose contents go into the corpse. |
| `NeedPrivilegeForReturnThings` | `gamemode` | Privilege required to use `/returnthings`. |
| `MaxDeathContentSavedPerPlayer` | `10` | How many death inventories are kept on disk per player for `/returnthings`. `0` disables saving. |
| `DebugMode` | `false` | Also broadcast corpse events to chat. |
| `FreeCorpseAfterTime` | `240` | Corpse becomes available to everyone after this many in-game hours. `0` = always free, below zero = never. |
| `CorpseCollectionTime` | `1` | Seconds the owner must hold right-click to collect a corpse. |
| `CorpseCompassEnabled` | `true` | If `false`, the compass item and recipe are disabled. Existing compasses turn into unknown items. |
| `DropArmorOnDeath` | `Vanilla` | `Vanilla` follows the game's own rules, `Armor` always puts worn armor into the corpse, `ArmorAndCloth` also includes clothing. |

## Corpse compass

The compass finds your corpses anywhere in the world; they do not need to be in loaded chunks. The server keeps a list of live corpses in the savegame and drops entries when a corpse is collected or destroyed.

- Right-click targets your nearest corpse. Shift + right-click cycles to the next one by distance.
- While the compass is in either hand, a line at the top of the screen shows whose corpse it is, the distance, the direction, and how far above or below you it lies.
- In the off-hand it refreshes its target every 10 seconds and streams guiding particles.
- The needle in the model turns toward the target when held.
- In creative mode it finds every player's corpses.

## Commands

- `/returnthings list <player>` lists the saved death inventories of an online player.
- `/returnthings get <player> <give to player> [id]` gives the saved inventory with the given index (0 = most recent) to an online player and deletes the saved file.
