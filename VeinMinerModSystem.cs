using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VeinMiner;

public sealed class VeinMinerModSystem : ModSystem
{
    public const string ConfigFile = "veinminer.json";

    /// <summary>Server-side settings. The client never reads it.</summary>
    internal static VeinMinerConfig Config = new();

    private ICoreServerAPI? sapi;

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass("VeinMinerPickaxeBehavior", typeof(VeinMinerPickaxeBehavior));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Config = LoadConfig(api);

        api.ChatCommands.GetOrCreate("veinminer")
            .WithDescription(Lang.Get("veinminer:cmd-desc"))
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(_ => TextCommandResult.Success(Lang.Get("veinminer:cmd-show",
                Config.MaxBlocks, Config.RequireSneak, Config.DiagonalNeighbours, string.Join(", ", Config.OrePrefixes))))
            .BeginSubCommand("limit")
                .WithDescription(Lang.Get("veinminer:cmd-limit-desc"))
                .WithArgs(api.ChatCommands.Parsers.Int("limit"))
                .HandleWith(OnLimit)
            .EndSubCommand();
    }

    private TextCommandResult OnLimit(TextCommandCallingArgs args)
    {
        int limit = (int)args[0];
        if (limit <= 0 || limit > VeinMinerConfig.HardCap)
            return TextCommandResult.Error(Lang.Get("veinminer:cmd-limit-error", VeinMinerConfig.HardCap));

        Config.MaxBlocks = limit;
        sapi!.StoreModConfig(Config, ConfigFile);
        return TextCommandResult.Success(Lang.Get("veinminer:cmd-limit-success", limit));
    }

    private static VeinMinerConfig LoadConfig(ICoreServerAPI api)
    {
        VeinMinerConfig? cfg = null;
        try
        {
            cfg = api.LoadModConfig<VeinMinerConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[veinminer] Could not read {0}, using defaults: {1}", ConfigFile, e.Message);
        }
        cfg ??= new VeinMinerConfig();
        cfg.Sanitize();
        api.StoreModConfig(cfg, ConfigFile); // writes any fields added since the file was made
        return cfg;
    }

    public override void Dispose()
    {
        Config = new VeinMinerConfig();
        sapi = null;
    }
}
