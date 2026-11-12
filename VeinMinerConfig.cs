using System;
using Vintagestory.API.Common;

namespace VeinMiner;

/// <summary>ModConfig/veinminer.json. Field names match the original mod, so old configs keep working.</summary>
public class VeinMinerConfig
{
    public const int HardCap = 4096;

    /// <summary>Most blocks broken by one vein-mine, including the clicked block (1..4096).</summary>
    public int MaxBlocks { get; set; } = 4;

    /// <summary>Only vein-mine while sneaking (the original mod's behavior).</summary>
    public bool RequireSneak { get; set; } = true;

    /// <summary>Also follow edge and corner neighbours (26-neighbourhood). False = faces only.</summary>
    public bool DiagonalNeighbours { get; set; } = true;

    /// <summary>Block code path prefixes that count as ore.</summary>
    public string[] OrePrefixes { get; set; } = { "ore-", "meteorite" };

    public void Sanitize()
    {
        MaxBlocks = Math.Clamp(MaxBlocks, 1, HardCap);
        OrePrefixes ??= new[] { "ore-", "meteorite" };
    }

    public bool Matches(Block block)
    {
        string? path = block.Code?.Path;
        if (path == null) return false;
        string[] prefixes = OrePrefixes;
        for (int i = 0; i < prefixes.Length; i++)
        {
            string p = prefixes[i];
            if (p != null && path.StartsWith(p, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
