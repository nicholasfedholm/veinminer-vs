using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VeinMiner;

/// <summary>
/// Pickaxe behavior: sneak-break an ore block to break the connected vein of the same block.
/// Server-authoritative. BFS over the 26-neighbourhood with pooled buffers: after warm-up the
/// search allocates nothing except one BlockPos per block actually broken (BreakBlock may keep it).
/// </summary>
public sealed class VeinMinerPickaxeBehavior : CollectibleBehavior
{
    // Neighbour offsets (dx, dy, dz): 6 faces, then 12 edges, then 8 corners.
    private static readonly int[] Offsets26 = BuildOffsets(true);
    private static readonly int[] Offsets6 = BuildOffsets(false);

    // Pooled search state. Block breaking runs on the server main thread only; the busy flag
    // rejects re-entry (a block whose OnBlockBroken ends up calling back into here).
    private static int[] queue = new int[3 * 64];
    private static readonly HashSet<long> queued = new(64);
    private static bool busy;

    private const int PackBias = 1 << 20;

    // Client tooltip cache, rebuilt only when the sneak key mapping changes.
    private string? tooltipLine;
    private int tooltipSig;

    public VeinMinerPickaxeBehavior(CollectibleObject collObj) : base(collObj) { }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (world.Api is not ICoreClientAPI capi) return;

        KeyCombination? map = capi.Input.GetHotKeyByCode("sneak")?.CurrentMapping;
        int sig = map == null ? 0 : HashCode.Combine(map.KeyCode, map.SecondKeyCode, map.Ctrl, map.Alt, map.Shift);
        if (tooltipLine == null || sig != tooltipSig)
        {
            string key = map?.ToString() ?? "Shift";
            tooltipLine = "\n<font color=\"#aaddff\">" + Lang.Get("veinminer:tooltip-info", key) + "</font>";
            tooltipSig = sig;
        }
        dsc.AppendLine(tooltipLine);
    }

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot,
        BlockSelection blockSel, float dropQuantityMultiplier, ref EnumHandling bhHandling)
    {
        // Client keeps vanilla prediction for the clicked block; the server syncs the rest.
        if (world.Side != EnumAppSide.Server || blockSel == null || busy) return true;
        if (byEntity is not EntityPlayer ep || ep.Player is not IServerPlayer player) return true;

        VeinMinerConfig cfg = VeinMinerModSystem.Config;
        if (cfg.RequireSneak && !ep.Controls.Sneak) return true;

        BlockPos start = blockSel.Position;
        Block startBlock = world.BlockAccessor.GetBlock(start);
        if (startBlock.Id == 0 || !cfg.Matches(startBlock)) return true;

        bhHandling = EnumHandling.PreventDefault;
        busy = true;
        try
        {
            MineVein(world, byEntity, player, itemslot, start, startBlock, dropQuantityMultiplier, cfg);
        }
        finally
        {
            queued.Clear();
            busy = false;
        }
        return true;
    }

    private void MineVein(IWorldAccessor world, Entity byEntity, IServerPlayer player, ItemSlot slot,
        BlockPos start, Block startBlock, float dropMul, VeinMinerConfig cfg)
    {
        IBlockAccessor ba = world.BlockAccessor;
        ILandClaimAPI claims = world.Claims;
        int targetId = startBlock.Id;
        int maxBlocks = cfg.MaxBlocks;
        int[] offsets = cfg.DiagonalNeighbours ? Offsets26 : Offsets6;
        bool damages = DamagesOnBreak(collObj);

        // Every queued position holds the target block and will be broken, so the queue never
        // needs more than maxBlocks entries. Positions are absolute ints; the set key is the
        // offset from start, packed into a long (BFS never strays more than maxBlocks away).
        int need = 3 * maxBlocks;
        if (queue.Length < need) queue = new int[Math.Max(need, queue.Length * 2)];
        int[] q = queue;

        int sx = start.X, sy = start.Y, sz = start.Z;
        q[0] = sx; q[1] = sy; q[2] = sz;
        int head = 0, tail = 3, enqueued = 1, broken = 0;
        queued.Add(Pack(0, 0, 0));

        BlockPos probe = start.Copy(); // reused for every neighbour read; keeps the dimension

        while (head < tail && broken < maxBlocks)
        {
            int x = q[head], y = q[head + 1], z = q[head + 2];
            head += 3;

            if (broken == 0)
            {
                // Clicked block: same call vanilla makes in CollectibleObject.OnBlockBrokenWith.
                startBlock.OnBlockBroken(world, start, player, dropMul);
            }
            else
            {
                probe.Set(x, y, z);
                if (ba.GetBlockId(probe) != targetId) continue;
                ba.BreakBlock(probe.Copy(), player, dropMul);
            }
            broken++;

            if (damages)
            {
                ItemStack? stack = slot.Itemstack;
                if (stack == null) break;
                stack.Collectible.DamageItem(world, byEntity, slot, 1);
                if (slot.Itemstack == null) break; // tool broke
            }

            if (enqueued >= maxBlocks) continue; // enough already queued to reach the cap

            for (int i = 0; i < offsets.Length; i += 3)
            {
                int nx = x + offsets[i], ny = y + offsets[i + 1], nz = z + offsets[i + 2];
                probe.Set(nx, ny, nz);
                if (ba.GetBlockId(probe) != targetId) continue;
                if (!queued.Add(Pack(nx - sx, ny - sy, nz - sz))) continue;
                if (claims.TestAccess(player, probe, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted) continue;

                q[tail] = nx; q[tail + 1] = ny; q[tail + 2] = nz;
                tail += 3;
                if (++enqueued >= maxBlocks) break;
            }
        }
    }

    private static bool DamagesOnBreak(CollectibleObject coll)
    {
        EnumItemDamageSource[]? by = coll.DamagedBy;
        return by != null && Array.IndexOf(by, EnumItemDamageSource.BlockBreaking) >= 0;
    }

    private static long Pack(int dx, int dy, int dz)
        => ((long)(dx + PackBias) << 42) | ((long)(dy + PackBias) << 21) | (long)(dz + PackBias);

    private static int[] BuildOffsets(bool diagonal)
    {
        var list = new List<int>(78);
        for (int pass = 1; pass <= 3; pass++) // by number of non-zero axes: faces, edges, corners
        {
            if (pass > 1 && !diagonal) break;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nonZero = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
                        if (nonZero != pass) continue;
                        list.Add(dx); list.Add(dy); list.Add(dz);
                    }
        }
        return list.ToArray();
    }
}
