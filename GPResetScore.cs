using System.Reflection;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace GPResetScore;

public class GPResetScoreConfig : BasePluginConfig
{
    // Chat commands without "css_": "rs" -> !rs, /rs, css_rs
    [JsonPropertyName("Commands")]
    public List<string> Commands { get; set; } = new() { "rs", "resetscore" };

    // Empty = everyone. Example: "@css/vip" = only subscribers
    [JsonPropertyName("RequiredFlag")]
    public string RequiredFlag { get; set; } = "";

    // Seconds between uses per player (0 = no cooldown)
    [JsonPropertyName("CooldownSeconds")]
    public int CooldownSeconds { get; set; } = 10;

    // true = MVP stars are reset too
    [JsonPropertyName("ResetMVPs")]
    public bool ResetMVPs { get; set; } = true;

    [JsonPropertyName("ChatPrefix")]
    public string ChatPrefix { get; set; } = "{default}[{green}GAMEPOINT{default}] ";

    [JsonPropertyName("MsgDone")]
    public string MsgDone { get; set; } = "{green}Hisobingiz nolga tushirildi.";

    [JsonPropertyName("MsgAlreadyZero")]
    public string MsgAlreadyZero { get; set; } = "{yellow}Hisobingiz allaqachon 0.";

    // {0} = seconds left
    [JsonPropertyName("MsgCooldown")]
    public string MsgCooldown { get; set; } = "{red}{0} soniyadan keyin qayta urinib ko'ring.";

    [JsonPropertyName("MsgNoAccess")]
    public string MsgNoAccess { get; set; } = "{red}Bu buyruq faqat GP PRIME / GP ELITE uchun.";
}

public class GPResetScore : BasePlugin, IPluginConfig<GPResetScoreConfig>
{
    public override string ModuleName => "GPResetScore";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "GAMEPOINT";
    public override string ModuleDescription => "Lightweight !rs score reset";

    public GPResetScoreConfig Config { get; set; } = new();

    // SteamID64 -> last use time
    private readonly Dictionary<ulong, DateTime> _lastUse = new();

    private static readonly Dictionary<string, string> Colors = BuildColors();

    public void OnConfigParsed(GPResetScoreConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        foreach (var raw in Config.Commands.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = raw.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name))
                continue;
            if (!name.StartsWith("css_"))
                name = "css_" + name;

            AddCommand(name, "Reset your score", OnResetCommand);
        }
    }

    private void OnResetCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid || player.IsBot)
            return;

        if (!string.IsNullOrWhiteSpace(Config.RequiredFlag) &&
            !AdminManager.PlayerHasPermissions(player, Config.RequiredFlag))
        {
            Reply(player, Config.MsgNoAccess);
            return;
        }

        if (Config.CooldownSeconds > 0 &&
            _lastUse.TryGetValue(player.SteamID, out var last))
        {
            var left = Config.CooldownSeconds - (int)(DateTime.UtcNow - last).TotalSeconds;
            if (left > 0)
            {
                Reply(player, Config.MsgCooldown.Replace("{0}", left.ToString()));
                return;
            }
        }

        var stats = player.ActionTrackingServices?.MatchStats;
        if (stats is null)
            return;

        bool alreadyZero =
            stats.Kills == 0 && stats.Deaths == 0 && stats.Assists == 0 &&
            stats.Damage == 0 && player.Score == 0 &&
            (!Config.ResetMVPs || player.MVPs == 0);

        if (alreadyZero)
        {
            Reply(player, Config.MsgAlreadyZero);
            return;
        }

        stats.Kills = 0;
        stats.Deaths = 0;
        stats.Assists = 0;
        stats.Damage = 0;
        stats.HeadShotKills = 0;
        player.Score = 0;
        if (Config.ResetMVPs)
            player.MVPs = 0;

        // Push changes to the scoreboard (Tab)
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pActionTrackingServices");
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iScore");
        if (Config.ResetMVPs)
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMVPs");

        _lastUse[player.SteamID] = DateTime.UtcNow;
        Reply(player, Config.MsgDone);
    }

    private void Reply(CCSPlayerController player, string message) =>
        player.PrintToChat(" " + Colorize(Config.ChatPrefix + message));

    private static Dictionary<string, string> BuildColors()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        foreach (var f in typeof(ChatColors).GetFields(flags))
            dict["{" + f.Name + "}"] = f.GetValue(null)?.ToString() ?? "";
        foreach (var p in typeof(ChatColors).GetProperties(flags))
            dict["{" + p.Name + "}"] = p.GetValue(null)?.ToString() ?? "";

        return dict;
    }

    private static string Colorize(string text)
    {
        foreach (var (tag, code) in Colors)
            text = text.Replace(tag, code, StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
