# VeinMiner (Vintage Story 1.22)

Sneak-break an ore block with a pickaxe and the connected vein of the same block breaks too.

A compiled rewrite of the source-only VeinMiner 1.0.5 mod (which the game compiled with Roslyn at every load). Same mod id (`veinminer`), same behavior class name, same `ModConfig/veinminer.json`, so it is a drop-in replacement.

## Config (`ModConfig/veinminer.json`, server side)
| Key | Default | Meaning |
|---|---|---|
| MaxBlocks | 4 | Most blocks per vein-mine, including the clicked one (1 to 4096) |
| RequireSneak | true | Only vein-mine while sneaking |
| DiagonalNeighbours | true | Follow edges and corners (26 neighbours); false = faces only |
| OrePrefixes | ["ore-", "meteorite"] | Block code prefixes that count as ore |

Commands (controlserver privilege): `/veinminer` shows settings, `/veinminer limit <n>` sets MaxBlocks.

## Behavior
- Server-authoritative; the client keeps vanilla prediction for the clicked block.
- Breadth-first from the clicked block, so the nearest ore goes first. Only the exact same block (same id) is followed.
- Each extra block checks land-claim access for the player.
- Tool takes 1 durability per block when it is damaged by block breaking (vanilla rule); mining stops when the tool breaks.
- Drops use the normal block drop path, with the tool's drop multiplier passed through.

## Performance
Pooled queue and visited set (static, reused), neighbour reads through one reused `BlockPos`, no LINQ, no closures in the hot path. The queue is capped at MaxBlocks entries because every queued block will be broken. The only per-block allocation is the `BlockPos` handed to `BreakBlock`.

## Build
`dotnet build -c Release -p:VSDir=<game folder>` (defaults to `%APPDATA%\Vintagestory`). Zip `modinfo.json`, `bin/Release/VeinMiner.dll` and `assets/`.

MIT license.
