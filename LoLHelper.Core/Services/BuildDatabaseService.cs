using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class ChampionSpellInfo
{
    public int Slot { get; set; } // 1=Q, 2=W, 3=E, 4=R
    public string Key { get; set; } = string.Empty; // "Q", "W", "E", "R"
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
}

public class ChampionInfo
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty; // DDragon key e.g. "MonkeyKing"
    public string Name { get; set; } = string.Empty; // Display name e.g. "Wukong"
    public string IconUrl { get; set; } = string.Empty;
    public List<ChampionSpellInfo> Spells { get; set; } = new();
}

public class ChampionRoleStats
{
    public string Role { get; set; } = "TOP";
    public string Patch { get; set; } = "26.22";
    public bool IsMostPopular { get; set; }
    public int TierNumber { get; set; } = 3;
    public string TierLabel => TierNumber switch
    {
        1 => "S",
        2 => "A",
        3 => "B",
        4 => "C",
        _ => "D"
    };
    public double WinRate { get; set; } = 0.5;
    public double WinRateChange { get; set; } = 0.0;
    public double PickRate { get; set; } = 0.0;
    public double BanRate { get; set; } = 0.0;
    public int Matches { get; set; } = 0;
}

public class OtpPlayerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Region { get; set; } = "KR";
    public string RankTier { get; set; } = "MASTER";
    public string RankDivision { get; set; } = "I";
    public int Lp { get; set; }
    public int OverallGames { get; set; }
    public double OverallWinRate { get; set; }

    public string CleanRegion => Region.ToUpperInvariant() switch
    {
        "EUW1" => "EUW",
        "EUN1" => "EUNE",
        "NA1" => "NA",
        "BR1" => "BR",
        "JP1" => "JP",
        "LA1" => "LAN",
        "LA2" => "LAS",
        "OC1" => "OCE",
        "TR1" => "TR",
        "VN2" => "VN",
        "TW2" => "TW",
        "SG2" => "SG",
        _ => Region.ToUpperInvariant()
    };

    public string DisplayRiotId => string.IsNullOrEmpty(Tag) ? Name : $"{Name} #{Tag}";

    public string FormattedRank
    {
        get
        {
            string tier = RankTier.ToUpperInvariant();
            if (tier is "MASTER" or "GRANDMASTER" or "CHALLENGER")
                return $"{tier} {Lp} LP";
            return $"{tier} {RankDivision}";
        }
    }
}

public class SummonerSpellOption
{
    public int Spell1Id { get; set; } = 4;
    public int Spell2Id { get; set; } = 14;
    public double WinRate { get; set; } = 0.5;
    public int Matches { get; set; } = 0;
}

public class RuneOptionInfo
{
    public RunePreset RunePreset { get; set; } = new();
    public RunePreset? WinRateRunePreset { get; set; }
    public RunePreset? CommonRunePreset { get; set; }
    public double WinRate { get; set; } = 0.5;
    public int Matches { get; set; } = 0;

    public string KeystoneIconUrl => RunePreset.SelectedPerkIds.Length > 0 ? RuneService.Instance.GetImageUrl(RunePreset.SelectedPerkIds[0]) : "";
    public string SubStyleIconUrl => RuneService.Instance.GetImageUrl(RunePreset.SubStyleId);
}

public class SkillOrderInfo
{
    public List<int> Order { get; set; } = new() { 1, 3, 2, 1, 1, 4, 1, 3, 1, 3, 4, 3, 3, 2, 2, 4, 2, 2 };
    public double WinRate { get; set; } = 0.55;
    public int Matches { get; set; } = 1000;

    public List<int> GetPriority()
    {
        // Determine order in which Q(1), W(2), E(3) reach 5 points (or highest count)
        var counts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 } };
        var maxedOrder = new List<int>();
        foreach (int s in Order)
        {
            if (s is >= 1 and <= 3)
            {
                counts[s]++;
                if (counts[s] >= 5 && !maxedOrder.Contains(s))
                    maxedOrder.Add(s);
            }
        }
        foreach (var kv in counts.OrderByDescending(k => k.Value))
        {
            if (!maxedOrder.Contains(kv.Key))
                maxedOrder.Add(kv.Key);
        }
        return maxedOrder;
    }
}

public class ChampionBuildPreset
{
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string BuildName { get; set; } = "Standard";
    public bool IsOtp { get; set; }
    public OtpPlayerInfo? OtpPlayer { get; set; }
    public bool IsMatchup { get; set; }
    public int MatchupChampionId { get; set; }
    public string MatchupChampionName { get; set; } = string.Empty;
    public string MatchupChampionIconUrl { get; set; } = string.Empty;
    public double WinRate { get; set; } = 0.5;
    public int Games { get; set; } = 0;

    public RunePreset RunePreset { get; set; } = new();
    public double KeystoneWinRate { get; set; } = 0.5;
    public int KeystoneMatches { get; set; } = 0;
    public List<RuneOptionInfo> RuneOptions { get; set; } = new();

    // Categorized Item Blocks
    public List<int> StartingItemIds { get; set; } = new();
    public List<int> CoreBuildOrderIds { get; set; } = new();
    public List<int> CoreFinalBuildIds { get; set; } = new();
    public List<int> SituationalItemIds { get; set; } = new();

    public int Spell1Id { get; set; } = 4; // Default Flash
    public int Spell2Id { get; set; } = 14; // Default Ignite
    public List<SummonerSpellOption> SpellOptions { get; set; } = new();
    public SkillOrderInfo SkillOrder { get; set; } = new();

    // UI Helper Properties
    public string KeystoneIconUrl => RunePreset.SelectedPerkIds.Length > 0 ? RuneService.Instance.GetImageUrl(RunePreset.SelectedPerkIds[0]) : "";
    public string SubStyleIconUrl => RuneService.Instance.GetImageUrl(RunePreset.SubStyleId);
    public string CoreItem1IconUrl { get; set; } = string.Empty;
    public string CoreItem2IconUrl { get; set; } = string.Empty;
    public string WinRateDisplay => $"{Math.Round(WinRate * 100):0}%";
    public string GamesDisplay => $"{Games:N0} GAMES";
}

public class ChampionFullData
{
    public ChampionInfo Champion { get; set; } = new();
    public bool IsFromOnline { get; set; }
    public Dictionary<string, ChampionRoleStats> RoleStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ChampionBuildPreset>> RoleBuilds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultRole { get; set; } = "TOP";
}

public class BuildDatabaseService
{
    public static BuildDatabaseService Instance { get; } = new BuildDatabaseService();

    private static readonly HashSet<int> BootIds = new()
    {
        1001, 3005, 3006, 3009, 3010, 3013, 3020, 3047, 3111, 3117, 3158, 3170, 3171, 3172, 3173, 3174, 3175
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string _latestVersion = "14.1.1";
    private bool _isInitialized = false;

    public List<ChampionInfo> AllChampions { get; private set; } = new();
    private readonly Dictionary<int, ChampionInfo> _champById = new();
    private readonly Dictionary<string, ChampionInfo> _champByCleanName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ChampionFullData> _cache = new();

    private JsonElement _crawlerRoot = default;
    private Dictionary<string, JsonElement>? _crawlerDict;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await RuneService.Instance.InitializeAsync();
        await ItemService.Instance.InitializeAsync();
        _latestVersion = ItemService.Instance.LatestVersion;

        try
        {
            string champsUrl = $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/data/en_US/champion.json";
            var champsJson = await _http.GetStringAsync(champsUrl);
            using var doc = JsonDocument.Parse(champsJson);
            var data = doc.RootElement.GetProperty("data");

            var list = new List<ChampionInfo>();
            foreach (var champ in data.EnumerateObject())
            {
                var prop = champ.Value;
                int id = int.Parse(prop.GetProperty("key").GetString() ?? "0");
                string key = prop.GetProperty("id").GetString() ?? champ.Name;
                string name = prop.GetProperty("name").GetString() ?? key;
                string iconUrl = $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/champion/{key}.png";

                var cInfo = new ChampionInfo
                {
                    Id = id,
                    Key = key,
                    Name = name,
                    IconUrl = iconUrl
                };
                list.Add(cInfo);
                _champById[id] = cInfo;
                _champByCleanName[CleanName(name)] = cInfo;
                _champByCleanName[CleanName(key)] = cInfo;
            }

            AllChampions = list.OrderBy(c => c.Name).ToList();
            _isInitialized = true;
        }
        catch { }
    }

    public ChampionInfo? GetChampionInfo(int champId)
    {
        return _champById.TryGetValue(champId, out var info) ? info : null;
    }

    private async Task EnsureChampionSpellsLoadedAsync(ChampionInfo champ)
    {
        if (champ.Spells.Count == 4) return;

        try
        {
            string url = $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/data/en_US/champion/{champ.Key}.json";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var champObj = doc.RootElement.GetProperty("data").GetProperty(champ.Key);
            if (champObj.TryGetProperty("spells", out var spellsArr) && spellsArr.ValueKind == JsonValueKind.Array)
            {
                string[] keys = { "Q", "W", "E", "R" };
                int idx = 0;
                var spells = new List<ChampionSpellInfo>();
                foreach (var sp in spellsArr.EnumerateArray())
                {
                    if (idx >= 4) break;
                    string spName = sp.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? keys[idx] : keys[idx];
                    string imgFull = sp.TryGetProperty("image", out var iProp) && iProp.TryGetProperty("full", out var fProp)
                        ? fProp.GetString() ?? ""
                        : "";
                    spells.Add(new ChampionSpellInfo
                    {
                        Slot = idx + 1,
                        Key = keys[idx],
                        Name = spName,
                        IconUrl = string.IsNullOrEmpty(imgFull) ? "" : $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/spell/{imgFull}"
                    });
                    idx++;
                }
                if (spells.Count == 4)
                    champ.Spells = spells;
            }
        }
        catch
        {
            if (champ.Spells.Count == 0)
            {
                string[] keys = { "Q", "W", "E", "R" };
                for (int i = 0; i < 4; i++)
                {
                    champ.Spells.Add(new ChampionSpellInfo
                    {
                        Slot = i + 1,
                        Key = keys[i],
                        Name = $"{champ.Name} {keys[i]}",
                        IconUrl = $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/spell/{champ.Key}{keys[i]}.png"
                    });
                }
            }
        }
    }

    public async Task<ChampionFullData> GetChampionFullDataAsync(
        int champId,
        string tier = "EMERALD%2B",
        string region = "WORLD",
        int? matchupChampId = null,
        bool forceRefresh = false)
    {
        if (!_isInitialized)
            await InitializeAsync();

        string cacheKey = $"{champId}_{tier}_{region}_{matchupChampId ?? 0}";
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (!_champById.TryGetValue(champId, out var champInfo))
        {
            champInfo = new ChampionInfo { Id = champId, Key = "Annie", Name = $"Champion {champId}" };
        }

        await EnsureChampionSpellsLoadedAsync(champInfo);

        // 1. Try Online Blitz API first
        var onlineData = await TryFetchOnlineBlitzDataAsync(champInfo, tier, region, matchupChampId);
        if (onlineData != null && onlineData.RoleBuilds.Values.Any(b => b.Count > 0))
        {
            _cache[cacheKey] = onlineData;
            return onlineData;
        }

        // 2. Safety Fallback: Use Crawler Data (blitz_champions_data.json)
        var fallbackData = LoadFromCrawlerFallback(champInfo, tier, matchupChampId);
        _cache[cacheKey] = fallbackData;
        return fallbackData;
    }

    private static string BuildStatsUrl(int champId, string tier, string region)
    {
        return $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats" +
               $"?queue_id=RANKED_SOLO_5X5&region={region}&tier={tier}&champion_id={champId}" +
               $"&columns=all_roles_total_num_games%2Cban_num_games%2Cchampion_ban_rate%2Cchampion_id" +
               $"%2Cchampion_role_pickrate%2Cchampion_role_tier%2Cchampion_role_tier_filtered%2Cgames" +
               $"%2Cindividual_position%2Cis_most_popular_role%2Cpatch%2Cpick_rate%2Cpostmatch_damage" +
               $"%2Cprevious_patch_stats%2Cqueue_id%2Cregion%2Crole_champion_pickrate%2Crole_pct_games" +
               $"%2Crole_total_num_games%2Cstrong_against%2Ctier%2Ctotal_num_games%2Cweak_against%2Cwin_rate";
    }

    private static Dictionary<string, ChampionRoleStats> ParseRoleStatsFromJson(string json)
    {
        var dict = new Dictionary<string, ChampionRoleStats>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(json)) return dict;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
                return dict;

            foreach (var r in dataArr.EnumerateArray())
            {
                string pos = r.TryGetProperty("individual_position", out var pProp) ? pProp.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(pos) || pos == "ALL") continue;

                int games = r.TryGetProperty("games", out var gProp) && gProp.ValueKind == JsonValueKind.Number ? gProp.GetInt32() : 0;
                if (games <= 0) continue;

                // Keep the entry with the highest game count if multiple rows exist for the same position
                if (dict.TryGetValue(pos, out var existing) && existing.Matches >= games)
                    continue;

                double wr = r.TryGetProperty("win_rate", out var wrProp) && wrProp.ValueKind == JsonValueKind.Number ? wrProp.GetDouble() : 0.5;
                double prevWr = wr;
                if (r.TryGetProperty("previous_patch_stats", out var prevProp) && prevProp.ValueKind == JsonValueKind.Object &&
                    prevProp.TryGetProperty("win_rate", out var pWrProp) && pWrProp.ValueKind == JsonValueKind.Number)
                {
                    prevWr = pWrProp.GetDouble();
                }

                string rawPatch = r.TryGetProperty("patch", out var patchProp) ? patchProp.GetString() ?? "26.22" : "26.22";
                bool isMostPop = r.TryGetProperty("is_most_popular_role", out var mpProp) && mpProp.ValueKind == JsonValueKind.True;
                int tierNum = r.TryGetProperty("champion_role_tier_filtered", out var tfProp) && tfProp.ValueKind == JsonValueKind.Number
                    ? tfProp.GetInt32()
                    : (r.TryGetProperty("champion_role_tier", out var tProp) && tProp.ValueKind == JsonValueKind.Number ? tProp.GetInt32() : 3);
                double pickRate = r.TryGetProperty("champion_role_pickrate", out var cprProp) && cprProp.ValueKind == JsonValueKind.Number
                    ? cprProp.GetDouble()
                    : (r.TryGetProperty("pick_rate", out var prProp) && prProp.ValueKind == JsonValueKind.Number ? prProp.GetDouble() : 0.0);
                double banRate = r.TryGetProperty("champion_ban_rate", out var brProp) && brProp.ValueKind == JsonValueKind.Number ? brProp.GetDouble() : 0.0;

                dict[pos] = new ChampionRoleStats
                {
                    Role = pos,
                    Patch = FormatPatch(rawPatch),
                    IsMostPopular = isMostPop,
                    TierNumber = tierNum,
                    WinRate = wr,
                    WinRateChange = wr - prevWr,
                    PickRate = pickRate,
                    BanRate = banRate,
                    Matches = games
                };
            }
        }
        catch { }

        return dict;
    }

    private static double GetTierSampleMultiplier(string tier)
    {
        return tier.ToUpperInvariant() switch
        {
            "CHALLENGER" => 0.0025,
            "GRANDMASTER" => 0.010,
            "MASTER%2B" or "MASTER+" or "MASTER" => 0.168,
            "DIAMOND%2B" or "DIAMOND+" or "DIAMOND" => 0.429,
            "EMERALD%2B" or "EMERALD+" or "EMERALD" => 1.0,
            "PLATINUM%2B" or "PLATINUM+" or "PLATINUM" => 1.688,
            "GOLD" => 0.635,
            "SILVER" => 0.441,
            "BRONZE" => 0.269,
            "IRON" => 0.088,
            _ => 1.0
        };
    }

    private static double GetTierWrOffset(string tier)
    {
        return tier.ToUpperInvariant() switch
        {
            "CHALLENGER" => 0.012,
            "GRANDMASTER" => 0.009,
            "MASTER%2B" or "MASTER+" or "MASTER" => 0.005,
            "DIAMOND%2B" or "DIAMOND+" or "DIAMOND" => 0.002,
            "EMERALD%2B" or "EMERALD+" or "EMERALD" => 0.0,
            "PLATINUM%2B" or "PLATINUM+" or "PLATINUM" => -0.001,
            "GOLD" => -0.004,
            "SILVER" => -0.008,
            "BRONZE" => -0.013,
            "IRON" => -0.022,
            _ => 0.0
        };
    }

    private async Task<ChampionFullData?> TryFetchOnlineBlitzDataAsync(
        ChampionInfo champ,
        string tier,
        string region,
        int? matchupChampId)
    {
        try
        {
            bool isBaseEmerald = string.Equals(tier, "EMERALD%2B", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tier, "EMERALD+", StringComparison.OrdinalIgnoreCase);

            var tTierStats = SafeGetStringAsync(BuildStatsUrl(champ.Id, tier, region));
            var tBaseStats = isBaseEmerald ? tTierStats : SafeGetStringAsync(BuildStatsUrl(champ.Id, "EMERALD%2B", region));
            await Task.WhenAll(tTierStats, tBaseStats);

            var tierRoleStats = ParseRoleStatsFromJson(tTierStats.Result);
            var baseRoleStats = isBaseEmerald ? tierRoleStats : ParseRoleStatsFromJson(tBaseStats.Result);

            if (tierRoleStats.Count == 0 && baseRoleStats.Count == 0)
                return null;

            var fullData = new ChampionFullData
            {
                Champion = champ,
                IsFromOnline = true
            };

            // Combine all valid roles from both selected tier and baseline Emerald+ so switching to high ranks (e.g. Challenger) never drops roles
            var allRoles = baseRoleStats.Keys.Union(tierRoleStats.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            double tierMult = GetTierSampleMultiplier(tier);
            double tierWrOff = GetTierWrOffset(tier);

            foreach (string pos in allRoles)
            {
                if (tierRoleStats.TryGetValue(pos, out var tStat))
                {
                    // Preserve IsMostPopular from baseline if tier stats didn't mark any role
                    if (baseRoleStats.TryGetValue(pos, out var bStat) && bStat.IsMostPopular && !tierRoleStats.Values.Any(v => v.IsMostPopular))
                        tStat.IsMostPopular = true;
                    fullData.RoleStats[pos] = tStat;
                }
                else if (baseRoleStats.TryGetValue(pos, out var bStat))
                {
                    fullData.RoleStats[pos] = new ChampionRoleStats
                    {
                        Role = pos,
                        Patch = bStat.Patch,
                        IsMostPopular = bStat.IsMostPopular,
                        TierNumber = bStat.TierNumber,
                        WinRate = Math.Clamp(bStat.WinRate + tierWrOff, 0.35, 0.65),
                        WinRateChange = bStat.WinRateChange,
                        PickRate = bStat.PickRate,
                        BanRate = bStat.BanRate,
                        Matches = Math.Max(1, (int)Math.Round(bStat.Matches * tierMult))
                    };
                }
            }

            if (fullData.RoleStats.Count == 0)
                return null;

            fullData.DefaultRole = fullData.RoleStats.Values
                .OrderByDescending(s => s.IsMostPopular)
                .ThenByDescending(s => s.Matches)
                .First().Role;

            // Fetch Standard + Matchup + OTP builds concurrently for all active roles
            var roleTasks = allRoles.Select(async role =>
            {
                fullData.RoleStats.TryGetValue(role, out var tStat);
                baseRoleStats.TryGetValue(role, out var bStat);
                var builds = await FetchOnlineRoleBuildsAsync(champ, role, fullData.DefaultRole, tier, isBaseEmerald, tStat, bStat, matchupChampId);
                return (role, builds);
            });

            var results = await Task.WhenAll(roleTasks);
            foreach (var (role, builds) in results)
            {
                if (builds.Count > 0)
                    fullData.RoleBuilds[role] = builds;
            }

            return fullData;
        }
        catch
        {
            return null;
        }
    }

    private static (int Games, double WinRate)? ParseMatchupStatForEnemy(string json, int enemyChampId)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;

            int bestGames = 0;
            double bestWr = 0.5;
            foreach (var el in arr.EnumerateArray())
            {
                int mId = el.TryGetProperty("matchup_champion_id", out var mp) && mp.ValueKind == JsonValueKind.Number
                    ? mp.GetInt32()
                    : (el.TryGetProperty("match_up_champion_id", out var mp2) && mp2.ValueKind == JsonValueKind.Number ? mp2.GetInt32() : 0);
                if (mId != enemyChampId) continue;

                int g = el.TryGetProperty("games", out var gp) && gp.ValueKind == JsonValueKind.Number ? gp.GetInt32() : 0;
                double wr = el.TryGetProperty("win_rate", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetDouble() : 0.5;
                if (g > bestGames)
                {
                    bestGames = g;
                    bestWr = wr;
                }
            }

            if (bestGames > 0)
                return (bestGames, bestWr);
        }
        catch { }
        return null;
    }

    private static void ScalePresetForTier(ChampionBuildPreset preset, double gamesScale, double wrDelta)
    {
        if (Math.Abs(gamesScale - 1.0) < 0.001 && Math.Abs(wrDelta) < 0.0005)
            return;

        preset.Games = Math.Max(1, (int)Math.Round(preset.Games * gamesScale));
        preset.WinRate = Math.Clamp(preset.WinRate + wrDelta, 0.32, 0.72);
        preset.KeystoneMatches = Math.Max(1, (int)Math.Round(preset.KeystoneMatches * gamesScale));
        preset.KeystoneWinRate = Math.Clamp(preset.KeystoneWinRate + wrDelta, 0.32, 0.72);

        if (preset.RuneOptions != null)
        {
            foreach (var ro in preset.RuneOptions)
            {
                ro.Matches = Math.Max(1, (int)Math.Round(ro.Matches * gamesScale));
                ro.WinRate = Math.Clamp(ro.WinRate + wrDelta, 0.32, 0.72);
            }
        }

        if (preset.SpellOptions != null)
        {
            foreach (var sp in preset.SpellOptions)
            {
                sp.Matches = Math.Max(1, (int)Math.Round(sp.Matches * gamesScale));
                sp.WinRate = Math.Clamp(sp.WinRate + wrDelta, 0.32, 0.72);
            }
        }

        if (preset.SkillOrder != null)
        {
            preset.SkillOrder.Matches = Math.Max(1, (int)Math.Round(preset.SkillOrder.Matches * gamesScale));
            preset.SkillOrder.WinRate = Math.Clamp(preset.SkillOrder.WinRate + wrDelta, 0.32, 0.72);
        }
    }

    private static ChampionBuildPreset CloneAsMatchupPreset(ChampionBuildPreset src, ChampionInfo enemyInfo, int games, double winRate)
    {
        double scale = src.Games > 0 ? (double)games / src.Games : 0.1;
        double wrDiff = winRate - src.WinRate;

        var clonedRunes = src.RuneOptions.Select(ro => new RuneOptionInfo
        {
            RunePreset = new RunePreset
            {
                Name = ro.RunePreset.Name,
                Description = ro.RunePreset.Description,
                PrimaryStyleId = ro.RunePreset.PrimaryStyleId,
                SubStyleId = ro.RunePreset.SubStyleId,
                SelectedPerkIds = ro.RunePreset.SelectedPerkIds.ToArray()
            },
            WinRateRunePreset = ro.WinRateRunePreset == null ? null : new RunePreset
            {
                Name = ro.WinRateRunePreset.Name,
                Description = ro.WinRateRunePreset.Description,
                PrimaryStyleId = ro.WinRateRunePreset.PrimaryStyleId,
                SubStyleId = ro.WinRateRunePreset.SubStyleId,
                SelectedPerkIds = ro.WinRateRunePreset.SelectedPerkIds.ToArray()
            },
            CommonRunePreset = ro.CommonRunePreset == null ? null : new RunePreset
            {
                Name = ro.CommonRunePreset.Name,
                Description = ro.CommonRunePreset.Description,
                PrimaryStyleId = ro.CommonRunePreset.PrimaryStyleId,
                SubStyleId = ro.CommonRunePreset.SubStyleId,
                SelectedPerkIds = ro.CommonRunePreset.SelectedPerkIds.ToArray()
            },
            WinRate = Math.Clamp(ro.WinRate + wrDiff, 0.32, 0.72),
            Matches = Math.Max(1, (int)Math.Round(ro.Matches * scale))
        }).ToList();

        var clonedSpells = src.SpellOptions.Select(sp => new SummonerSpellOption
        {
            Spell1Id = sp.Spell1Id,
            Spell2Id = sp.Spell2Id,
            WinRate = Math.Clamp(sp.WinRate + wrDiff, 0.32, 0.72),
            Matches = Math.Max(1, (int)Math.Round(sp.Matches * scale))
        }).ToList();

        return new ChampionBuildPreset
        {
            ChampionId = src.ChampionId,
            ChampionName = src.ChampionName,
            Role = src.Role,
            BuildName = src.BuildName,
            IsOtp = false,
            IsMatchup = true,
            MatchupChampionId = enemyInfo.Id,
            MatchupChampionName = enemyInfo.Name,
            MatchupChampionIconUrl = enemyInfo.IconUrl,
            WinRate = Math.Clamp(winRate, 0.32, 0.72),
            Games = Math.Max(1, games),
            RunePreset = new RunePreset
            {
                Name = $"{src.ChampionName} vs {enemyInfo.Name}",
                Description = $"{src.Role} | vs {enemyInfo.Name}",
                PrimaryStyleId = src.RunePreset.PrimaryStyleId,
                SubStyleId = src.RunePreset.SubStyleId,
                SelectedPerkIds = src.RunePreset.SelectedPerkIds.ToArray()
            },
            KeystoneWinRate = clonedRunes.FirstOrDefault()?.WinRate ?? Math.Clamp(winRate, 0.32, 0.72),
            KeystoneMatches = clonedRunes.FirstOrDefault()?.Matches ?? Math.Max(1, games),
            RuneOptions = clonedRunes,
            StartingItemIds = src.StartingItemIds.ToList(),
            CoreBuildOrderIds = src.CoreBuildOrderIds.ToList(),
            CoreFinalBuildIds = src.CoreFinalBuildIds.ToList(),
            SituationalItemIds = src.SituationalItemIds.ToList(),
            Spell1Id = src.Spell1Id,
            Spell2Id = src.Spell2Id,
            SpellOptions = clonedSpells,
            SkillOrder = new SkillOrderInfo
            {
                Order = src.SkillOrder.Order.ToList(),
                WinRate = Math.Clamp(src.SkillOrder.WinRate + wrDiff, 0.32, 0.72),
                Matches = Math.Max(1, (int)Math.Round(src.SkillOrder.Matches * scale))
            },
            CoreItem1IconUrl = src.CoreItem1IconUrl,
            CoreItem2IconUrl = src.CoreItem2IconUrl
        };
    }

    private async Task<List<ChampionBuildPreset>> FetchOnlineRoleBuildsAsync(
        ChampionInfo champ,
        string role,
        string defaultRole,
        string tier,
        bool isBaseEmerald,
        ChampionRoleStats? tierRoleStats,
        ChampionRoleStats? baseRoleStats,
        int? matchupChampId)
    {
        var result = new List<ChampionBuildPreset>();
        string urlStd = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position={role}";
        string urlOtp = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&is_otp=true&individual_position={role}";

        Task<string> tStd = SafeGetStringAsync(urlStd);
        Task<string> tOtp = SafeGetStringAsync(urlOtp);
        Task<string> tMatchup = Task.FromResult(string.Empty);
        Task<string> tMatchupStatsTier = Task.FromResult(string.Empty);
        Task<string> tMatchupStatsBase = Task.FromResult(string.Empty);

        bool hasMatchup = matchupChampId.HasValue && matchupChampId.Value > 0;
        if (hasMatchup)
        {
            int mId = matchupChampId!.Value;
            string urlMatchup = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position={role}&match_up_champion_id={mId}";
            string urlMStatsTier = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_matchups_stats?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&tier={tier}&region=WORLD&individual_position={role}";
            tMatchup = SafeGetStringAsync(urlMatchup);
            tMatchupStatsTier = SafeGetStringAsync(urlMStatsTier);
            tMatchupStatsBase = isBaseEmerald
                ? tMatchupStatsTier
                : SafeGetStringAsync($"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_matchups_stats?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&tier=EMERALD%2B&region=WORLD&individual_position={role}");
        }

        await Task.WhenAll(tStd, tOtp, tMatchup, tMatchupStatsTier, tMatchupStatsBase);

        var usedOtpPuuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        double roleGamesScale = 1.0;
        double roleWrDelta = 0.0;
        if (!isBaseEmerald)
        {
            if (tierRoleStats != null && baseRoleStats != null && baseRoleStats.Matches > 0)
            {
                roleGamesScale = (double)tierRoleStats.Matches / baseRoleStats.Matches;
                roleWrDelta = tierRoleStats.WinRate - baseRoleStats.WinRate;
            }
            else
            {
                roleGamesScale = GetTierSampleMultiplier(tier);
                roleWrDelta = GetTierWrOffset(tier);
            }
        }

        // 1. Standard Builds
        var stdBuilds = new List<ChampionBuildPreset>();
        if (!string.IsNullOrEmpty(tStd.Result))
        {
            try
            {
                using var doc = JsonDocument.Parse(tStd.Result);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var bdata in arr.EnumerateArray())
                    {
                        var preset = ParseOnlineBuildElement(champ, role, bdata, false, usedOtpPuuids);
                        if (preset != null)
                        {
                            if (!isBaseEmerald)
                                ScalePresetForTier(preset, roleGamesScale, roleWrDelta);
                            stdBuilds.Add(preset);
                        }
                    }
                }
            }
            catch { }
        }

        stdBuilds = stdBuilds.OrderByDescending(b => b.Games).ToList();
        result.AddRange(stdBuilds);

        // 2. Matchup Builds (Placed directly under Standard Builds)
        if (hasMatchup)
        {
            int mId = matchupChampId!.Value;
            if (!_champById.TryGetValue(mId, out var enemyInfo))
                enemyInfo = new ChampionInfo { Id = mId, Key = "Annie", Name = $"Champion {mId}" };

            var matchupBuilds = new List<ChampionBuildPreset>();
            string matchupJson = tMatchup.Result;

            // If current role had no specific matchup build tags, also check default role
            if ((string.IsNullOrEmpty(matchupJson) || !matchupJson.Contains("\"games\"")) && !string.Equals(role, defaultRole, StringComparison.OrdinalIgnoreCase))
            {
                string fallbackMatchupUrl = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id={champ.Id}&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position={defaultRole}&match_up_champion_id={mId}";
                matchupJson = await SafeGetStringAsync(fallbackMatchupUrl);
            }

            var tierMStat = ParseMatchupStatForEnemy(tMatchupStatsTier.Result, mId);
            var baseMStat = isBaseEmerald ? tierMStat : ParseMatchupStatForEnemy(tMatchupStatsBase.Result, mId);

            double matchupGamesScale = roleGamesScale;
            double matchupWrDelta = roleWrDelta;
            if (!isBaseEmerald && tierMStat.HasValue && baseMStat.HasValue && baseMStat.Value.Games > 0)
            {
                matchupGamesScale = (double)tierMStat.Value.Games / baseMStat.Value.Games;
                matchupWrDelta = tierMStat.Value.WinRate - baseMStat.Value.WinRate;
            }

            if (!string.IsNullOrEmpty(matchupJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(matchupJson);
                    if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bdata in arr.EnumerateArray())
                        {
                            var preset = ParseOnlineBuildElement(champ, role, bdata, false, usedOtpPuuids);
                            if (preset != null)
                            {
                                preset.IsMatchup = true;
                                preset.MatchupChampionId = enemyInfo.Id;
                                preset.MatchupChampionName = enemyInfo.Name;
                                preset.MatchupChampionIconUrl = enemyInfo.IconUrl;
                                preset.RunePreset.Name = $"{champ.Name} vs {enemyInfo.Name} ({preset.BuildName})";
                                if (!isBaseEmerald)
                                    ScalePresetForTier(preset, matchupGamesScale, matchupWrDelta);
                                matchupBuilds.Add(preset);
                            }
                        }
                    }
                }
                catch { }
            }

            // If Blitz API has 0 tag builds for an uncommon matchup, synthesize a matchup build from the top standard build + matchup stats
            if (matchupBuilds.Count == 0 && stdBuilds.Count > 0)
            {
                var topStd = stdBuilds[0];
                int synthGames = tierMStat?.Games ?? Math.Max(5, (int)Math.Round(topStd.Games * 0.04));
                double synthWr = tierMStat?.WinRate ?? topStd.WinRate;
                matchupBuilds.Add(CloneAsMatchupPreset(topStd, enemyInfo, synthGames, synthWr));
            }

            result.AddRange(matchupBuilds.OrderByDescending(b => b.Games).Take(2));
        }

        // 3. OTP Builds (Top 2 unique OTP players, matching Blitz.gg)
        if (!string.IsNullOrEmpty(tOtp.Result))
        {
            try
            {
                using var doc = JsonDocument.Parse(tOtp.Result);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var otpBuilds = new List<ChampionBuildPreset>();
                    foreach (var bdata in arr.EnumerateArray())
                    {
                        var preset = ParseOnlineBuildElement(champ, role, bdata, true, usedOtpPuuids);
                        if (preset != null) otpBuilds.Add(preset);
                    }
                    result.AddRange(otpBuilds.OrderByDescending(b => b.Games).Take(2));
                }
            }
            catch { }
        }

        return result;
    }

    private async Task<string> SafeGetStringAsync(string url)
    {
        try
        {
            return await _http.GetStringAsync(url);
        }
        catch
        {
            return string.Empty;
        }
    }

    private ChampionBuildPreset? ParseOnlineBuildElement(
        ChampionInfo champ,
        string role,
        JsonElement bdata,
        bool isOtp,
        HashSet<string> usedOtpPuuids)
    {
        try
        {
            double rawWr = bdata.TryGetProperty("win_rate", out var wrProp) && wrProp.ValueKind == JsonValueKind.Number ? wrProp.GetDouble() : 0.5;
            double adjWr = bdata.TryGetProperty("adjusted_win_rate", out var awrProp) && awrProp.ValueKind == JsonValueKind.Number ? awrProp.GetDouble() : rawWr;
            double wrOffset = rawWr - adjWr;
            int games = bdata.TryGetProperty("games", out var gProp) && gProp.ValueKind == JsonValueKind.Number ? gProp.GetInt32() : 0;

            string buildName = "Standard";
            OtpPlayerInfo? otpPlayer = null;

            if (isOtp)
            {
                if (bdata.TryGetProperty("otp_players", out var otpArr) && otpArr.ValueKind == JsonValueKind.Array)
                {
                    var candidates = new List<OtpPlayerInfo>();
                    var scores = new Dictionary<OtpPlayerInfo, double>();
                    var puuids = new Dictionary<OtpPlayerInfo, string>();

                    foreach (var p in otpArr.EnumerateArray())
                    {
                        var pInfo = new OtpPlayerInfo
                        {
                            Name = p.TryGetProperty("name", out var nP) ? nP.GetString() ?? "OTP" : "OTP",
                            Tag = p.TryGetProperty("tag", out var tP) ? tP.GetString() ?? "" : "",
                            Region = p.TryGetProperty("region", out var rP) ? rP.GetString() ?? "KR" : "KR",
                            RankTier = p.TryGetProperty("rank_tier", out var rtP) ? rtP.GetString() ?? "MASTER" : "MASTER",
                            RankDivision = p.TryGetProperty("rank_division", out var rdP) ? rdP.GetString() ?? "I" : "I",
                            Lp = p.TryGetProperty("lp", out var lpP) && lpP.ValueKind == JsonValueKind.Number ? lpP.GetInt32() : 0,
                            OverallGames = p.TryGetProperty("overall_games", out var ogP) && ogP.ValueKind == JsonValueKind.Number ? ogP.GetInt32() : games,
                            OverallWinRate = p.TryGetProperty("overall_wr", out var owP) && owP.ValueKind == JsonValueKind.Number ? owP.GetDouble() : adjWr
                        };
                        double score = p.TryGetProperty("score", out var scP) && scP.ValueKind == JsonValueKind.Number ? scP.GetDouble() : 0.0;
                        string puuid = p.TryGetProperty("participant_puuid", out var puP) ? puP.GetString() ?? pInfo.DisplayRiotId : pInfo.DisplayRiotId;
                        candidates.Add(pInfo);
                        scores[pInfo] = score;
                        puuids[pInfo] = puuid;
                    }

                    var sorted = candidates
                        .OrderByDescending(c => c.Region is "KR" or "EUW1" or "NA1" ? 1 : 0)
                        .ThenByDescending(c => scores[c])
                        .ThenByDescending(c => c.OverallGames)
                        .ToList();

                    otpPlayer = sorted.FirstOrDefault(c => usedOtpPuuids.Add(puuids[c])) ?? sorted.FirstOrDefault();
                    if (otpPlayer != null)
                        buildName = otpPlayer.DisplayRiotId;
                }
            }
            else
            {
                if (bdata.TryGetProperty("name_tags", out var tagsArr) && tagsArr.ValueKind == JsonValueKind.Array)
                {
                    var tags = tagsArr.EnumerateArray().Select(t => t.GetString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    if (tags.Count > 0)
                        buildName = string.Join(" / ", tags);
                }
            }

            // Starting Items
            var starts = new List<int>();
            if (bdata.TryGetProperty("startingItems", out var stArr) && stArr.ValueKind == JsonValueKind.Array)
            {
                var topStart = stArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var sg) && sg.ValueKind == JsonValueKind.Number ? sg.GetInt32() : 0)
                    .FirstOrDefault();
                if (topStart.ValueKind == JsonValueKind.Object && topStart.TryGetProperty("itemIds", out var sIds))
                {
                    starts.AddRange(ParseItemIdsElement(sIds));
                }
            }
            EnsureDefaultStartingItems(starts, role);

            // Core Items & Build Order
            var coreIds = new List<int>();
            var buildOrderIds = new List<int>();
            if (bdata.TryGetProperty("coreItems", out var coreArr) && coreArr.ValueKind == JsonValueKind.Array)
            {
                var topCore = coreArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var cg) && cg.ValueKind == JsonValueKind.Number ? cg.GetInt32() : 0)
                    .FirstOrDefault();
                if (topCore.ValueKind == JsonValueKind.Object && topCore.TryGetProperty("itemIds", out var cIds))
                {
                    coreIds.AddRange(ParseItemIdsElement(cIds));
                }
            }

            JsonElement itemOrdersObj = default;
            bool hasItemOrders = bdata.TryGetProperty("itemOrders", out itemOrdersObj) && itemOrdersObj.ValueKind == JsonValueKind.Object;

            foreach (int cid in coreIds)
            {
                bool addedFromOrder = false;
                if (hasItemOrders && itemOrdersObj.TryGetProperty(cid.ToString(), out var orderArr) && orderArr.ValueKind == JsonValueKind.Array)
                {
                    var topPath = orderArr.EnumerateArray()
                        .OrderByDescending(x => x.TryGetProperty("games", out var og) && og.ValueKind == JsonValueKind.Number ? og.GetInt32() : 0)
                        .FirstOrDefault();
                    if (topPath.ValueKind == JsonValueKind.Object && topPath.TryGetProperty("items_order", out var ioArr) && ioArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var comp in ioArr.EnumerateArray())
                        {
                            if (comp.ValueKind == JsonValueKind.Number && comp.TryGetInt32(out int compId))
                            {
                                buildOrderIds.Add(compId);
                                addedFromOrder = true;
                            }
                        }
                    }
                }
                if (!addedFromOrder)
                {
                    var gItem = ItemService.Instance.GetItem(cid);
                    if (gItem?.From != null)
                        buildOrderIds.AddRange(gItem.From);
                }
                buildOrderIds.Add(cid);
            }

            // Completed Items (6 items) & Situational Items (7 items)
            var completedIds = new List<int>(coreIds);
            bool hasBoots = completedIds.Any(id => BootIds.Contains(id));
            var sitIds = new List<int>();

            if (bdata.TryGetProperty("situationalItems", out var sitArr) && sitArr.ValueKind == JsonValueKind.Array)
            {
                var sortedSit = sitArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var sg) && sg.ValueKind == JsonValueKind.Number ? sg.GetInt32() : 0)
                    .ToList();

                foreach (var si in sortedSit)
                {
                    if (!si.TryGetProperty("itemId", out var idProp)) continue;
                    int iid = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32() : (int.TryParse(idProp.GetString(), out int p) ? p : 0);
                    if (iid <= 0 || completedIds.Contains(iid) || sitIds.Contains(iid)) continue;

                    if (hasBoots && BootIds.Contains(iid)) continue;
                    if (BootIds.Contains(iid)) hasBoots = true;

                    if (completedIds.Count < 6)
                        completedIds.Add(iid);
                    else if (sitIds.Count < 7)
                        sitIds.Add(iid);
                }
            }

            // Shards
            int[] shardDefaults = { 5008, 5008, 5001 };
            string[] shardKeys = { "offenseShard", "flexShard", "defenseShard" };
            if (bdata.TryGetProperty("shards", out var shardsObj) && shardsObj.ValueKind == JsonValueKind.Object)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (shardsObj.TryGetProperty(shardKeys[i], out var sArr) && sArr.ValueKind == JsonValueKind.Array)
                    {
                        var topShard = sArr.EnumerateArray()
                            .OrderByDescending(x => x.TryGetProperty("games", out var sg) ? sg.GetInt32() : 0)
                            .FirstOrDefault();
                        if (topShard.ValueKind == JsonValueKind.Object &&
                            topShard.TryGetProperty("shard_id", out var sidProp) &&
                            int.TryParse(sidProp.GetString(), out int parsedShard))
                        {
                            shardDefaults[i] = parsedShard;
                        }
                    }
                }
            }

            // Runes & Selectable Rune Paths (1 to 3 based on Blitz.gg's >= 5% build pick rate rule)
            var runeOptions = new List<RuneOptionInfo>();
            var runeElements = bdata.TryGetProperty("runes", out var runesArr) && runesArr.ValueKind == JsonValueKind.Array
                ? runesArr.EnumerateArray().ToList()
                : new List<JsonElement>();

            if (bdata.TryGetProperty("keystone", out var ksArr) && ksArr.ValueKind == JsonValueKind.Array)
            {
                int minKsGames = Math.Max(1, (int)Math.Ceiling(games * 0.05));
                var allSortedKs = ksArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var kg) && kg.ValueKind == JsonValueKind.Number ? kg.GetInt32() : 0)
                    .ToList();

                var topKeystones = allSortedKs
                    .Where(x => (x.TryGetProperty("games", out var kg) && kg.ValueKind == JsonValueKind.Number ? kg.GetInt32() : 0) >= minKsGames)
                    .Take(3)
                    .ToList();

                if (topKeystones.Count == 0 && allSortedKs.Count > 0)
                    topKeystones.Add(allSortedKs[0]);

                int mainPrimaryTree = 8000;
                if (topKeystones.Count > 0)
                {
                    int firstKsId = topKeystones[0].TryGetProperty("keystone_id", out var fkp) && fkp.ValueKind == JsonValueKind.Number ? fkp.GetInt32() : 8010;
                    mainPrimaryTree = topKeystones[0].TryGetProperty("treeId", out var ftp) && ftp.ValueKind == JsonValueKind.Number && ftp.GetInt32() > 0
                        ? ftp.GetInt32()
                        : (RuneService.Instance.GetTreeIdByPerkId(firstKsId) ?? 8000);
                }

                foreach (var ksEl in topKeystones)
                {
                    int ksId = ksEl.TryGetProperty("keystone_id", out var kidProp) && kidProp.ValueKind == JsonValueKind.Number
                        ? kidProp.GetInt32()
                        : 8010;
                    int pStyle = ksEl.TryGetProperty("treeId", out var tidProp) && tidProp.ValueKind == JsonValueKind.Number && tidProp.GetInt32() > 0
                        ? tidProp.GetInt32()
                        : (RuneService.Instance.GetTreeIdByPerkId(ksId) ?? 8000);
                    int optGames = ksEl.TryGetProperty("games", out var kgmProp) && kgmProp.ValueKind == JsonValueKind.Number
                        ? kgmProp.GetInt32()
                        : games;
                    double rawKsWr = ksEl.TryGetProperty("win_rate", out var kwrProp) && kwrProp.ValueKind == JsonValueKind.Number
                        ? kwrProp.GetDouble()
                        : rawWr;
                    double optWr = Math.Clamp(0.50 + (rawKsWr - wrOffset - 0.50) * (optGames / (optGames + 150.0)), 0.0, 1.0);

                    var commonPerks = BuildPerkListForKeystone(ksId, pStyle, mainPrimaryTree, games, runeElements, shardDefaults, false, out int commonSubStyle);
                    var wrPerks = BuildPerkListForKeystone(ksId, pStyle, mainPrimaryTree, games, runeElements, shardDefaults, true, out int wrSubStyle);

                    var commonPreset = new RunePreset
                    {
                        Name = $"{champ.Name} - {buildName}",
                        Description = $"{role} | {buildName}",
                        PrimaryStyleId = pStyle,
                        SubStyleId = commonSubStyle,
                        SelectedPerkIds = commonPerks.Take(9).ToArray()
                    };
                    var wrPreset = new RunePreset
                    {
                        Name = $"{champ.Name} - {buildName}",
                        Description = $"{role} | {buildName}",
                        PrimaryStyleId = pStyle,
                        SubStyleId = wrSubStyle,
                        SelectedPerkIds = wrPerks.Take(9).ToArray()
                    };

                    runeOptions.Add(new RuneOptionInfo
                    {
                        RunePreset = new RunePreset
                        {
                            Name = wrPreset.Name,
                            Description = wrPreset.Description,
                            PrimaryStyleId = wrPreset.PrimaryStyleId,
                            SubStyleId = wrPreset.SubStyleId,
                            SelectedPerkIds = wrPreset.SelectedPerkIds.ToArray()
                        },
                        WinRateRunePreset = wrPreset,
                        CommonRunePreset = commonPreset,
                        WinRate = optWr,
                        Matches = optGames
                    });
                }
            }

            if (runeOptions.Count == 0)
            {
                var fallbackPerks = new List<int> { 8010 };
                EnsureValidRunesCount(fallbackPerks, 8000, 8400);
                fallbackPerks.AddRange(shardDefaults);
                runeOptions.Add(new RuneOptionInfo
                {
                    RunePreset = new RunePreset
                    {
                        Name = $"{champ.Name} - {buildName}",
                        Description = $"{role} | {buildName}",
                        PrimaryStyleId = 8000,
                        SubStyleId = 8400,
                        SelectedPerkIds = fallbackPerks.Take(9).ToArray()
                    },
                    WinRate = adjWr,
                    Matches = games
                });
            }

            var firstRuneOpt = runeOptions[0];
            int primaryStyle = firstRuneOpt.RunePreset.PrimaryStyleId;
            int subStyle = firstRuneOpt.RunePreset.SubStyleId;
            var perkIds = firstRuneOpt.RunePreset.SelectedPerkIds.ToList();
            double ksWr = firstRuneOpt.WinRate;
            int ksGames = firstRuneOpt.Matches;

            // Summoner Spells
            int spell1 = 4;
            int spell2 = role == "JUNGLE" ? 11 : (role == "TOP" ? 12 : (role == "BOTTOM" ? 7 : 14));
            var spellOptions = new List<SummonerSpellOption>();

            if (bdata.TryGetProperty("summonerSpells", out var spArr) && spArr.ValueKind == JsonValueKind.Array)
            {
                var sortedSpells = spArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var sg) ? sg.GetInt32() : 0)
                    .Take(2)
                    .ToList();

                foreach (var spEntry in sortedSpells)
                {
                    if (spEntry.TryGetProperty("summonerSpellIds", out var idsArr) && idsArr.ValueKind == JsonValueKind.Array)
                    {
                        var ids = idsArr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList();
                        if (ids.Count >= 2)
                        {
                            double spWr = spEntry.TryGetProperty("win_rate", out var swr) && swr.ValueKind == JsonValueKind.Number
                                ? Math.Clamp(swr.GetDouble() - wrOffset, 0.0, 1.0)
                                : adjWr;
                            int spGames = spEntry.TryGetProperty("games", out var sgm) && sgm.ValueKind == JsonValueKind.Number ? sgm.GetInt32() : 0;
                            spellOptions.Add(new SummonerSpellOption
                            {
                                Spell1Id = ids[0],
                                Spell2Id = ids[1],
                                WinRate = spWr,
                                Matches = spGames
                            });
                        }
                    }
                }
            }

            if (spellOptions.Count > 0)
            {
                spell1 = spellOptions[0].Spell1Id;
                spell2 = spellOptions[0].Spell2Id;
            }
            else
            {
                spellOptions.Add(new SummonerSpellOption { Spell1Id = spell1, Spell2Id = spell2, WinRate = adjWr, Matches = games });
            }

            // Skill Order
            var skillOrder = new SkillOrderInfo();
            if (bdata.TryGetProperty("skillOrders", out var soArr) && soArr.ValueKind == JsonValueKind.Array)
            {
                var topSo = soArr.EnumerateArray()
                    .OrderByDescending(x => x.TryGetProperty("games", out var sg) ? sg.GetInt32() : 0)
                    .FirstOrDefault();
                if (topSo.ValueKind == JsonValueKind.Object)
                {
                    if (topSo.TryGetProperty("skillOrder", out var ordArr) && ordArr.ValueKind == JsonValueKind.Array)
                    {
                        var list = ordArr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList();
                        if (list.Count > 0) skillOrder.Order = list;
                    }
                    if (topSo.TryGetProperty("win_rate", out var sowr) && sowr.ValueKind == JsonValueKind.Number)
                        skillOrder.WinRate = sowr.GetDouble();
                    if (topSo.TryGetProperty("games", out var sogm) && sogm.ValueKind == JsonValueKind.Number)
                        skillOrder.Matches = sogm.GetInt32();
                }
            }

            return FinalizePreset(
                champ.Id, champ.Name, role, buildName, isOtp, otpPlayer,
                adjWr, games, primaryStyle, subStyle, perkIds.Take(9).ToArray(),
                ksWr, ksGames, starts, buildOrderIds, completedIds, sitIds,
                spell1, spell2, spellOptions, skillOrder, runeOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<int> BuildPerkListForKeystone(
        int keystoneId,
        int primaryStyle,
        int mainPrimaryStyle,
        int totalBuildGames,
        List<JsonElement> runeElements,
        int[] shardDefaults,
        bool sortByWinRate,
        out int subStyle)
    {
        var perkIds = new List<int> { keystoneId };
        subStyle = primaryStyle == 8400 ? 8000 : 8400;

        if (runeElements.Count > 0)
        {
            // Aggregate rune stats across all indices by runeId
            var aggByRune = new Dictionary<int, (int TreeId, int Games, double WeightedWins)>();
            foreach (var r in runeElements)
            {
                int rId = r.TryGetProperty("runeId", out var rp) && rp.ValueKind == JsonValueKind.Number ? rp.GetInt32() : 0;
                int tId = r.TryGetProperty("treeId", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 0;
                int g = r.TryGetProperty("games", out var gp) && gp.ValueKind == JsonValueKind.Number ? gp.GetInt32() : 0;
                double w = r.TryGetProperty("win_rate", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetDouble() : 0.5;
                if (rId <= 0 || g <= 0) continue;
                if (tId <= 0) tId = RuneService.Instance.GetTreeIdByPerkId(rId) ?? 0;
                if (tId <= 0) continue;

                if (aggByRune.TryGetValue(rId, out var prev))
                    aggByRune[rId] = (tId, prev.Games + g, prev.WeightedWins + g * w);
                else
                    aggByRune[rId] = (tId, g, g * w);
            }

            int minPrimaryGames = sortByWinRate ? Math.Max(3, (int)(totalBuildGames * 0.01)) : 1;

            // 1. Primary minor runes (Slots 1, 2, 3 of primaryStyle)
            var pTree = RuneService.Instance.AllTrees.FirstOrDefault(t => t.Id == primaryStyle);
            if (pTree != null)
            {
                for (int slotIdx = 1; slotIdx <= 3 && slotIdx < pTree.Slots.Count; slotIdx++)
                {
                    var slotRuneIds = pTree.Slots[slotIdx].Runes.Select(x => x.Id).ToHashSet();
                    var candidates = aggByRune
                        .Where(kv => slotRuneIds.Contains(kv.Key) && kv.Value.Games >= minPrimaryGames)
                        .ToList();
                    if (candidates.Count == 0)
                        candidates = aggByRune.Where(kv => slotRuneIds.Contains(kv.Key)).ToList();

                    if (candidates.Count > 0)
                    {
                        int chosen = sortByWinRate
                            ? candidates.OrderByDescending(kv => kv.Value.WeightedWins / kv.Value.Games).ThenByDescending(kv => kv.Value.Games).First().Key
                            : candidates.OrderByDescending(kv => kv.Value.Games).First().Key;
                        perkIds.Add(chosen);
                    }
                }
            }

            // 2. Secondary tree & 2 minor runes from different slots
            if (primaryStyle != mainPrimaryStyle && mainPrimaryStyle > 0 && aggByRune.Values.Any(v => v.TreeId == mainPrimaryStyle))
            {
                // Secondary keystone option (e.g., Phase Rush or Electrocute when main build tree is Precision)
                subStyle = mainPrimaryStyle;
            }
            else if (sortByWinRate)
            {
                int minTreeGames = Math.Max(5, (int)(totalBuildGames * 0.05));
                int minSecRuneGames = Math.Max(5, (int)(totalBuildGames * 0.02));

                var validSecTrees = aggByRune
                    .Where(kv => kv.Value.TreeId != primaryStyle && kv.Value.Games >= minTreeGames)
                    .Select(kv => kv.Value.TreeId)
                    .Distinct()
                    .ToList();

                double bestPairWr = -1;
                int bestSubTree = subStyle;

                foreach (int candTreeId in validSecTrees)
                {
                    var sTreeObj = RuneService.Instance.AllTrees.FirstOrDefault(t => t.Id == candTreeId);
                    if (sTreeObj == null) continue;

                    var slotBest = new List<(int Slot, int RuneId, double Wr)>();
                    for (int sIdx = 1; sIdx < sTreeObj.Slots.Count; sIdx++)
                    {
                        var slotIds = sTreeObj.Slots[sIdx].Runes.Select(x => x.Id).ToHashSet();
                        var slotCands = aggByRune
                            .Where(kv => slotIds.Contains(kv.Key) && kv.Value.Games >= minSecRuneGames)
                            .OrderByDescending(kv => kv.Value.WeightedWins / kv.Value.Games)
                            .ToList();
                        if (slotCands.Count > 0)
                        {
                            var top = slotCands[0];
                            slotBest.Add((sIdx, top.Key, top.Value.WeightedWins / top.Value.Games));
                        }
                    }

                    if (slotBest.Count >= 2)
                    {
                        var top2 = slotBest.OrderByDescending(x => x.Wr).Take(2).ToList();
                        double avgWr = (top2[0].Wr + top2[1].Wr) / 2.0;
                        if (avgWr > bestPairWr)
                        {
                            bestPairWr = avgWr;
                            bestSubTree = candTreeId;
                        }
                    }
                }
                subStyle = bestSubTree;
            }
            else
            {
                var bestSec1 = runeElements
                    .Where(r => r.TryGetProperty("index", out var ip) && ip.GetInt32() == 3 &&
                                r.TryGetProperty("treeId", out var tp) && tp.GetInt32() > 0 && tp.GetInt32() != primaryStyle)
                    .OrderByDescending(r => r.TryGetProperty("games", out var gp) ? gp.GetInt32() : 0)
                    .FirstOrDefault();

                if (bestSec1.ValueKind == JsonValueKind.Object && bestSec1.TryGetProperty("treeId", out var stProp))
                    subStyle = stProp.GetInt32();
            }

            int targetSubStyle = subStyle;
            var chosenSecTree = RuneService.Instance.AllTrees.FirstOrDefault(t => t.Id == targetSubStyle);
            if (chosenSecTree != null)
            {
                int minSecGames = sortByWinRate ? Math.Max(3, (int)(totalBuildGames * 0.02)) : 1;
                var slotPicks = new List<(int Slot, int RuneId, double Score)>();

                for (int sIdx = 1; sIdx < chosenSecTree.Slots.Count; sIdx++)
                {
                    var slotIds = chosenSecTree.Slots[sIdx].Runes.Select(x => x.Id).ToHashSet();
                    var cands = aggByRune
                        .Where(kv => slotIds.Contains(kv.Key) && kv.Value.Games >= minSecGames)
                        .ToList();
                    if (cands.Count == 0)
                        cands = aggByRune.Where(kv => slotIds.Contains(kv.Key)).ToList();

                    if (cands.Count > 0)
                    {
                        var bestInSlot = sortByWinRate
                            ? cands.OrderByDescending(kv => kv.Value.WeightedWins / kv.Value.Games).ThenByDescending(kv => kv.Value.Games).First()
                            : cands.OrderByDescending(kv => kv.Value.Games).First();
                        double score = sortByWinRate
                            ? bestInSlot.Value.WeightedWins / bestInSlot.Value.Games
                            : bestInSlot.Value.Games;
                        slotPicks.Add((sIdx, bestInSlot.Key, score));
                    }
                }

                foreach (var pick in slotPicks.OrderByDescending(x => x.Score).Take(2).OrderBy(x => x.Slot))
                {
                    perkIds.Add(pick.RuneId);
                }
            }
        }

        EnsureValidRunesCount(perkIds, primaryStyle, subStyle);
        perkIds.AddRange(shardDefaults);
        return perkIds;
    }

    private void EnsureLazyCrawlerLoaded()
    {
        if (_crawlerDict != null) return;
        _crawlerDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        string[] possiblePaths =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "crawler", "blitz_champions_data.json"),
            @"C:\Users\ufo\Downloads\TP\LoL_Helper_app\crawler\blitz_champions_data.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blitz_champions_data.json"),
            @"C:\Users\ufo\Downloads\TP\crawler\blitz_champions_data.json"
        };

        string blitzPath = possiblePaths.FirstOrDefault(File.Exists) ?? "";
        if (!string.IsNullOrEmpty(blitzPath))
        {
            try
            {
                var json = File.ReadAllText(blitzPath);
                using var doc = JsonDocument.Parse(json);
                _crawlerRoot = doc.RootElement.Clone();
                if (_crawlerRoot.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in _crawlerRoot.EnumerateObject())
                    {
                        _crawlerDict[CleanName(prop.Name)] = prop.Value;
                    }
                }
            }
            catch { }
        }
    }

    private ChampionFullData LoadFromCrawlerFallback(ChampionInfo champ, string tier, int? matchupChampId)
    {
        EnsureLazyCrawlerLoaded();

        var fullData = new ChampionFullData
        {
            Champion = champ,
            IsFromOnline = false
        };

        bool isBaseEmerald = string.Equals(tier, "EMERALD%2B", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tier, "EMERALD+", StringComparison.OrdinalIgnoreCase);
        double tierMult = isBaseEmerald ? 1.0 : GetTierSampleMultiplier(tier);
        double tierWrOff = isBaseEmerald ? 0.0 : GetTierWrOffset(tier);

        ChampionInfo? enemyInfo = null;
        if (matchupChampId.HasValue && matchupChampId.Value > 0)
        {
            if (!_champById.TryGetValue(matchupChampId.Value, out enemyInfo))
                enemyInfo = new ChampionInfo { Id = matchupChampId.Value, Key = "Annie", Name = $"Champion {matchupChampId.Value}" };
        }

        string cleanKey = CleanName(champ.Name);
        if (_crawlerDict != null && _crawlerDict.TryGetValue(cleanKey, out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var roleProp in rolesElement.EnumerateObject())
            {
                string role = roleProp.Name;
                var rVal = roleProp.Value;

                int rawMatches = rVal.TryGetProperty("Matches", out var mProp) && mProp.ValueKind == JsonValueKind.Number ? mProp.GetInt32() : 0;
                double rawWr = rVal.TryGetProperty("Win Rate", out var wrProp) && wrProp.ValueKind == JsonValueKind.Number ? wrProp.GetDouble() : 0.5;

                var stats = new ChampionRoleStats
                {
                    Role = role,
                    Patch = rVal.TryGetProperty("Patch", out var pProp) ? pProp.GetString() ?? "26.22" : "26.22",
                    IsMostPopular = rVal.TryGetProperty("IsMostPopular", out var mpProp) && mpProp.ValueKind == JsonValueKind.True,
                    TierNumber = rVal.TryGetProperty("Tier", out var tProp) && tProp.ValueKind == JsonValueKind.Number ? tProp.GetInt32() : 3,
                    WinRate = Math.Clamp(rawWr + tierWrOff, 0.35, 0.65),
                    WinRateChange = rVal.TryGetProperty("Win Rate Change", out var wrcProp) && wrcProp.ValueKind == JsonValueKind.Number ? wrcProp.GetDouble() : 0.0,
                    PickRate = rVal.TryGetProperty("Pick Rate", out var prProp) && prProp.ValueKind == JsonValueKind.Number ? prProp.GetDouble() : 0.0,
                    BanRate = rVal.TryGetProperty("Ban Rate", out var brProp) && brProp.ValueKind == JsonValueKind.Number ? brProp.GetDouble() : 0.0,
                    Matches = Math.Max(1, (int)Math.Round(rawMatches * tierMult))
                };
                fullData.RoleStats[role] = stats;

                if (!rVal.TryGetProperty("Builds", out var buildsArr) || buildsArr.ValueKind != JsonValueKind.Array)
                    continue;

                var stdBuilds = new List<ChampionBuildPreset>();
                var otpBuilds = new List<ChampionBuildPreset>();
                int bIdx = 1;
                foreach (var buildData in buildsArr.EnumerateArray())
                {
                    var preset = ParseCrawlerBuildElement(champ, role, buildData, bIdx++);
                    if (preset != null)
                    {
                        if (!preset.IsOtp && !isBaseEmerald)
                            ScalePresetForTier(preset, tierMult, tierWrOff);
                        if (preset.IsOtp)
                            otpBuilds.Add(preset);
                        else
                            stdBuilds.Add(preset);
                    }
                }

                stdBuilds = stdBuilds.OrderByDescending(b => b.Games).ToList();
                var roleBuilds = new List<ChampionBuildPreset>(stdBuilds);

                if (enemyInfo != null)
                {
                    var matchupBuilds = new List<ChampionBuildPreset>();
                    if (rVal.TryGetProperty("Matchups", out var matchupsObj) &&
                        matchupsObj.ValueKind == JsonValueKind.Object &&
                        matchupsObj.TryGetProperty(enemyInfo.Name, out var mBuildsArr) &&
                        mBuildsArr.ValueKind == JsonValueKind.Array)
                    {
                        int mIdx = 1;
                        foreach (var mBuildData in mBuildsArr.EnumerateArray())
                        {
                            var mp = ParseCrawlerBuildElement(champ, role, mBuildData, mIdx++);
                            if (mp != null && !mp.IsOtp)
                            {
                                mp.IsMatchup = true;
                                mp.MatchupChampionId = enemyInfo.Id;
                                mp.MatchupChampionName = enemyInfo.Name;
                                mp.MatchupChampionIconUrl = enemyInfo.IconUrl;
                                // Strip any trailing " (vs ...)" from build name so card title is clean like "Bruiser"
                                int vsIdx = mp.BuildName.IndexOf(" (vs ", StringComparison.OrdinalIgnoreCase);
                                if (vsIdx > 0)
                                    mp.BuildName = mp.BuildName.Substring(0, vsIdx);
                                if (!isBaseEmerald)
                                    ScalePresetForTier(mp, tierMult, tierWrOff);
                                matchupBuilds.Add(mp);
                            }
                        }
                    }

                    if (matchupBuilds.Count == 0 && stdBuilds.Count > 0)
                    {
                        var topStd = stdBuilds[0];
                        matchupBuilds.Add(CloneAsMatchupPreset(topStd, enemyInfo, Math.Max(5, (int)Math.Round(topStd.Games * 0.04)), topStd.WinRate));
                    }

                    roleBuilds.AddRange(matchupBuilds.OrderByDescending(b => b.Games).Take(2));
                }

                roleBuilds.AddRange(otpBuilds.OrderByDescending(b => b.Games).Take(2));
                fullData.RoleBuilds[role] = roleBuilds;
            }
        }

        if (fullData.RoleStats.Count == 0)
        {
            fullData.RoleStats["TOP"] = new ChampionRoleStats { Role = "TOP", Matches = Math.Max(1, (int)Math.Round(1000 * tierMult)), WinRate = 0.5 + tierWrOff };
            var defPreset = FinalizePreset(
                champ.Id, champ.Name, "TOP", "Standard", false, null,
                0.5 + tierWrOff, Math.Max(1, (int)Math.Round(1000 * tierMult)), 8000, 8400, new[] { 8010, 9111, 9105, 8299, 8473, 8453, 5008, 5008, 5001 },
                0.5 + tierWrOff, Math.Max(1, (int)Math.Round(1000 * tierMult)), new List<int> { 1055, 2003 }, new List<int> { 3133, 1037, 6692, 1001, 3047, 2021, 6610 },
                new List<int> { 6692, 3047, 6610, 6333, 3161, 3071 }, new List<int> { 3156, 3053, 3065, 3026 },
                4, 14, new List<SummonerSpellOption> { new() { Spell1Id = 4, Spell2Id = 14, WinRate = 0.5 + tierWrOff, Matches = Math.Max(1, (int)Math.Round(1000 * tierMult)) } },
                new SkillOrderInfo());
            var list = new List<ChampionBuildPreset> { defPreset };
            if (enemyInfo != null)
                list.Add(CloneAsMatchupPreset(defPreset, enemyInfo, Math.Max(5, defPreset.Games / 20), defPreset.WinRate));
            fullData.RoleBuilds["TOP"] = list;
        }

        fullData.DefaultRole = fullData.RoleStats.Values
            .OrderByDescending(s => s.IsMostPopular)
            .ThenByDescending(s => s.Matches)
            .First().Role;

        return fullData;
    }

    private ChampionBuildPreset? ParseCrawlerBuildElement(ChampionInfo champ, string role, JsonElement buildData, int buildIndex)
    {
        try
        {
            string buildName = buildData.TryGetProperty("build_name", out var bnProp) ? bnProp.GetString() ?? $"Build #{buildIndex}" : $"Build #{buildIndex}";
            bool isOtp = (buildData.TryGetProperty("is_otp", out var otpProp) && otpProp.ValueKind == JsonValueKind.True) ||
                         buildName.StartsWith("OTP", StringComparison.OrdinalIgnoreCase);
            double winRate = buildData.TryGetProperty("overall_win_rate", out var wrProp) && wrProp.ValueKind == JsonValueKind.Number ? wrProp.GetDouble() : 0.5;
            int games = buildData.TryGetProperty("overall_matches", out var gmProp) && gmProp.ValueKind == JsonValueKind.Number ? gmProp.GetInt32() : 0;

            OtpPlayerInfo? otpPlayer = null;
            if (buildData.TryGetProperty("otp_player", out var otpObj) && otpObj.ValueKind == JsonValueKind.Object)
            {
                otpPlayer = new OtpPlayerInfo
                {
                    Name = otpObj.TryGetProperty("name", out var np) ? np.GetString() ?? buildName : buildName,
                    Tag = otpObj.TryGetProperty("tag", out var tp) ? tp.GetString() ?? "" : "",
                    Region = otpObj.TryGetProperty("region", out var rp) ? rp.GetString() ?? "KR" : "KR",
                    RankTier = otpObj.TryGetProperty("rank_tier", out var rtp) ? rtp.GetString() ?? "MASTER" : "MASTER",
                    RankDivision = otpObj.TryGetProperty("rank_division", out var rdp) ? rdp.GetString() ?? "I" : "I",
                    Lp = otpObj.TryGetProperty("lp", out var lpp) && lpp.ValueKind == JsonValueKind.Number ? lpp.GetInt32() : 0,
                    OverallGames = otpObj.TryGetProperty("overall_games", out var ogp) && ogp.ValueKind == JsonValueKind.Number ? ogp.GetInt32() : games,
                    OverallWinRate = otpObj.TryGetProperty("overall_wr", out var owp) && owp.ValueKind == JsonValueKind.Number ? owp.GetDouble() : winRate
                };
            }
            else if (isOtp)
            {
                otpPlayer = new OtpPlayerInfo
                {
                    Name = buildName,
                    Tag = "OTP",
                    Region = "KR",
                    RankTier = "MASTER",
                    RankDivision = "I",
                    Lp = 400,
                    OverallGames = games,
                    OverallWinRate = winRate
                };
            }

            // Starting items
            var starts = new List<int>();
            if (buildData.TryGetProperty("starting_item_ids", out var stIdsProp) && stIdsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in stIdsProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Number) starts.Add(el.GetInt32());
            }
            else if (buildData.TryGetProperty("starting_items", out var startsProp) && startsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in startsProp.EnumerateArray())
                {
                    var id = ItemService.Instance.GetItemIdByName(item.GetString() ?? "");
                    if (id.HasValue) starts.Add(id.Value);
                }
            }
            EnsureDefaultStartingItems(starts, role);

            // Core items
            var cores = new List<int>();
            if (buildData.TryGetProperty("core_item_ids", out var cIdsProp) && cIdsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in cIdsProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Number) cores.Add(el.GetInt32());
            }
            else if (buildData.TryGetProperty("core_items", out var coresProp) && coresProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in coresProp.EnumerateArray())
                {
                    var id = ItemService.Instance.GetItemIdByName(item.GetString() ?? "");
                    if (id.HasValue) cores.Add(id.Value);
                }
            }

            // Build order (with components)
            var buildOrder = new List<int>();
            if (buildData.TryGetProperty("build_order_ids", out var boProp) && boProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in boProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Number) buildOrder.Add(el.GetInt32());
            }
            else if (buildData.TryGetProperty("core_items_components", out var compObj) && compObj.ValueKind == JsonValueKind.Object)
            {
                foreach (int cid in cores)
                {
                    var gItem = ItemService.Instance.GetItem(cid);
                    if (gItem != null && compObj.TryGetProperty(gItem.Name, out var cArr) && cArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cName in cArr.EnumerateArray())
                        {
                            var compId = ItemService.Instance.GetItemIdByName(cName.GetString() ?? "");
                            if (compId.HasValue) buildOrder.Add(compId.Value);
                        }
                    }
                    else if (gItem?.From != null)
                    {
                        buildOrder.AddRange(gItem.From);
                    }
                    buildOrder.Add(cid);
                }
            }
            else
            {
                foreach (int cid in cores)
                {
                    var gItem = ItemService.Instance.GetItem(cid);
                    if (gItem?.From != null) buildOrder.AddRange(gItem.From);
                    buildOrder.Add(cid);
                }
            }

            // Completed items & Situational items
            var completed = new List<int>();
            if (buildData.TryGetProperty("completed_item_ids", out var compIdsProp) && compIdsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in compIdsProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Number) completed.Add(el.GetInt32());
            }
            else
            {
                completed.AddRange(cores);
            }

            var sitIds = new List<int>();
            if (buildData.TryGetProperty("situational_items", out var sitProp) && sitProp.ValueKind == JsonValueKind.Array)
            {
                bool hasBoots = completed.Any(id => BootIds.Contains(id));
                foreach (var sitObj in sitProp.EnumerateArray())
                {
                    int? id = null;
                    if (sitObj.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                        id = idEl.GetInt32();
                    else if (sitObj.TryGetProperty("item", out var itemNameProp))
                        id = ItemService.Instance.GetItemIdByName(itemNameProp.GetString() ?? "");

                    if (!id.HasValue || completed.Contains(id.Value) || sitIds.Contains(id.Value)) continue;
                    if (hasBoots && BootIds.Contains(id.Value)) continue;
                    if (BootIds.Contains(id.Value)) hasBoots = true;

                    if (completed.Count < 6)
                        completed.Add(id.Value);
                    else
                        sitIds.Add(id.Value);
                }
            }

            // Runes
            var perkIds = new List<int>();
            int pStyle = 8000, sStyle = 8400;
            double ksWr = winRate;
            int ksGames = games;

            if (buildData.TryGetProperty("runes", out var runesData) && runesData.ValueKind == JsonValueKind.Object)
            {
                if (runesData.TryGetProperty("keystone_win_rate", out var kwr) && kwr.ValueKind == JsonValueKind.Number)
                    ksWr = kwr.GetDouble();
                if (runesData.TryGetProperty("keystone_matches", out var kgm) && kgm.ValueKind == JsonValueKind.Number)
                    ksGames = kgm.GetInt32();

                if (runesData.TryGetProperty("perk_ids", out var pIdsArr) && pIdsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in pIdsArr.EnumerateArray())
                        if (el.ValueKind == JsonValueKind.Number) perkIds.Add(el.GetInt32());
                }

                if (runesData.TryGetProperty("primary_style_id", out var psProp) && psProp.ValueKind == JsonValueKind.Number)
                    pStyle = psProp.GetInt32();
                if (runesData.TryGetProperty("sub_style_id", out var ssProp) && ssProp.ValueKind == JsonValueKind.Number)
                    sStyle = ssProp.GetInt32();

                if (perkIds.Count == 0)
                {
                    if (runesData.TryGetProperty("keystone", out var ksProp))
                    {
                        var ksId = RuneService.Instance.GetPerkIdByName(ksProp.GetString() ?? "");
                        if (ksId.HasValue) perkIds.Add(ksId.Value);
                    }
                    if (runesData.TryGetProperty("secondary_and_other", out var secProp) && secProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sec in secProp.EnumerateArray())
                        {
                            var id = RuneService.Instance.GetPerkIdByName(sec.GetString() ?? "");
                            if (id.HasValue) perkIds.Add(id.Value);
                        }
                    }
                    if (perkIds.Count > 0)
                        pStyle = RuneService.Instance.GetTreeIdByPerkId(perkIds[0]) ?? 8000;
                    if (perkIds.Count > 4)
                        sStyle = RuneService.Instance.GetTreeIdByPerkId(perkIds[4]) ?? (pStyle == 8400 ? 8000 : 8400);

                    EnsureValidRunesCount(perkIds, pStyle, sStyle);
                    perkIds.AddRange(new[] { 5008, 5008, 5001 });
                }
            }

            if (perkIds.Count < 9)
            {
                EnsureValidRunesCount(perkIds, pStyle, sStyle);
                while (perkIds.Count < 9) perkIds.Add(5008);
            }

            // Summoner spells
            int spell1 = 4;
            int spell2 = role == "JUNGLE" ? 11 : (role == "TOP" ? 12 : (role == "BOTTOM" ? 7 : 14));
            var spellOptions = new List<SummonerSpellOption>();

            if (buildData.TryGetProperty("summoner_spells_options", out var sOptArr) && sOptArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in sOptArr.EnumerateArray())
                {
                    if (opt.TryGetProperty("ids", out var idsArr) && idsArr.ValueKind == JsonValueKind.Array)
                    {
                        var ids = idsArr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();
                        if (ids.Count >= 2)
                        {
                            spellOptions.Add(new SummonerSpellOption
                            {
                                Spell1Id = ids[0],
                                Spell2Id = ids[1],
                                WinRate = opt.TryGetProperty("win_rate", out var owr) && owr.ValueKind == JsonValueKind.Number ? owr.GetDouble() : winRate,
                                Matches = opt.TryGetProperty("matches", out var om) && om.ValueKind == JsonValueKind.Number ? om.GetInt32() : games
                            });
                        }
                    }
                }
            }

            if (spellOptions.Count == 0 && buildData.TryGetProperty("summoner_spells", out var spellsProp) && spellsProp.ValueKind == JsonValueKind.Array)
            {
                var sArr = spellsProp.EnumerateArray().ToArray();
                if (sArr.Length >= 2)
                {
                    spell1 = GetSpellId(sArr[0].GetString());
                    spell2 = GetSpellId(sArr[1].GetString());
                    double spWr = buildData.TryGetProperty("summoner_spells_win_rate", out var swr) && swr.ValueKind == JsonValueKind.Number ? swr.GetDouble() : winRate;
                    spellOptions.Add(new SummonerSpellOption { Spell1Id = spell1, Spell2Id = spell2, WinRate = spWr, Matches = games });
                }
            }

            if (spellOptions.Count > 0)
            {
                spell1 = spellOptions[0].Spell1Id;
                spell2 = spellOptions[0].Spell2Id;
            }
            else
            {
                spellOptions.Add(new SummonerSpellOption { Spell1Id = spell1, Spell2Id = spell2, WinRate = winRate, Matches = games });
            }

            // Skill order
            var skillOrder = new SkillOrderInfo { Matches = games };
            if (buildData.TryGetProperty("skill_order", out var soObj) && soObj.ValueKind == JsonValueKind.Object)
            {
                if (soObj.TryGetProperty("order", out var ordArr) && ordArr.ValueKind == JsonValueKind.Array)
                {
                    var list = ordArr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList();
                    if (list.Count > 0) skillOrder.Order = list;
                }
                if (soObj.TryGetProperty("win_rate", out var sowr) && sowr.ValueKind == JsonValueKind.Number)
                    skillOrder.WinRate = sowr.GetDouble();
                if (soObj.TryGetProperty("matches", out var som) && som.ValueKind == JsonValueKind.Number)
                    skillOrder.Matches = som.GetInt32();
            }

            // Optional multiple rune_options from crawler fallback JSON
            var crawlerRuneOptions = new List<RuneOptionInfo>();
            if (buildData.TryGetProperty("rune_options", out var roArr) && roArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var ro in roArr.EnumerateArray())
                {
                    var roPerks = new List<int>();
                    if (ro.TryGetProperty("perk_ids", out var roPIds) && roPIds.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in roPIds.EnumerateArray())
                            if (el.ValueKind == JsonValueKind.Number) roPerks.Add(el.GetInt32());
                    }
                    int roPStyle = ro.TryGetProperty("primary_style_id", out var rps) && rps.ValueKind == JsonValueKind.Number ? rps.GetInt32() : pStyle;
                    int roSStyle = ro.TryGetProperty("sub_style_id", out var rss) && rss.ValueKind == JsonValueKind.Number ? rss.GetInt32() : sStyle;
                    double roWr = ro.TryGetProperty("keystone_win_rate", out var rwr) && rwr.ValueKind == JsonValueKind.Number ? rwr.GetDouble() : ksWr;
                    int roGames = ro.TryGetProperty("keystone_matches", out var rgm) && rgm.ValueKind == JsonValueKind.Number ? rgm.GetInt32() : ksGames;
                    if (roPerks.Count < 9)
                    {
                        EnsureValidRunesCount(roPerks, roPStyle, roSStyle);
                        while (roPerks.Count < 9) roPerks.Add(5008);
                    }
                    crawlerRuneOptions.Add(new RuneOptionInfo
                    {
                        RunePreset = new RunePreset
                        {
                            Name = $"{champ.Name} - {buildName}",
                            Description = $"{role} | {buildName}",
                            PrimaryStyleId = roPStyle,
                            SubStyleId = roSStyle,
                            SelectedPerkIds = roPerks.Take(9).ToArray()
                        },
                        WinRate = roWr,
                        Matches = roGames
                    });
                }
            }

            return FinalizePreset(
                champ.Id, champ.Name, role, buildName, isOtp, otpPlayer,
                winRate, games, pStyle, sStyle, perkIds.Take(9).ToArray(),
                ksWr, ksGames, starts, buildOrder, completed, sitIds,
                spell1, spell2, spellOptions, skillOrder, crawlerRuneOptions);
        }
        catch
        {
            return null;
        }
    }

    private ChampionBuildPreset FinalizePreset(
        int champId, string champName, string role, string buildName, bool isOtp, OtpPlayerInfo? otpPlayer,
        double winRate, int games, int pStyle, int sStyle, int[] perks,
        double ksWr, int ksGames, List<int> starts, List<int> buildOrder, List<int> completed, List<int> sitIds,
        int spell1, int spell2, List<SummonerSpellOption> spellOptions, SkillOrderInfo skillOrder,
        List<RuneOptionInfo>? runeOptions = null)
    {
        // Pad completed items to 6 if fewer
        if (completed.Count < 6)
        {
            int[] defaults = { 6692, 3047, 6610, 6333, 3161, 3071, 3026 };
            foreach (int d in defaults)
            {
                if (!completed.Contains(d)) completed.Add(d);
                if (completed.Count >= 6) break;
            }
        }

        // First 2 non-boot completed items for the left sidebar build card icons
        var nonBootCompleted = completed.Where(id => !BootIds.Contains(id)).ToList();
        string icon1 = nonBootCompleted.Count > 0 ? ItemService.Instance.GetItem(nonBootCompleted[0])?.IconUrl ?? "" : "";
        string icon2 = nonBootCompleted.Count > 1 ? ItemService.Instance.GetItem(nonBootCompleted[1])?.IconUrl ?? "" : "";

        var defaultRunePreset = new RunePreset
        {
            Name = $"{champName} - {buildName}",
            Description = $"{role} | {buildName}",
            PrimaryStyleId = pStyle,
            SubStyleId = sStyle,
            SelectedPerkIds = perks
        };

        var finalRuneOptions = runeOptions != null && runeOptions.Count > 0
            ? runeOptions
            : new List<RuneOptionInfo>
            {
                new RuneOptionInfo
                {
                    RunePreset = defaultRunePreset,
                    WinRate = ksWr,
                    Matches = ksGames
                }
            };

        return new ChampionBuildPreset
        {
            ChampionId = champId,
            ChampionName = champName,
            Role = role,
            BuildName = buildName,
            IsOtp = isOtp,
            OtpPlayer = otpPlayer,
            WinRate = winRate,
            Games = games,
            RunePreset = defaultRunePreset,
            KeystoneWinRate = ksWr,
            KeystoneMatches = ksGames,
            RuneOptions = finalRuneOptions,
            StartingItemIds = starts,
            CoreBuildOrderIds = buildOrder,
            CoreFinalBuildIds = completed,
            SituationalItemIds = sitIds,
            Spell1Id = spell1,
            Spell2Id = spell2,
            SpellOptions = spellOptions,
            SkillOrder = skillOrder,
            CoreItem1IconUrl = icon1,
            CoreItem2IconUrl = icon2
        };
    }

    private static void EnsureDefaultStartingItems(List<int> starts, string role)
    {
        if (starts.Count == 0)
        {
            if (role == "JUNGLE") { starts.Add(1101); starts.Add(2003); }
            else if (role == "UTILITY") { starts.Add(3865); starts.Add(2003); }
            else { starts.Add(2003); starts.Add(1055); }
        }
    }

    private static void EnsureValidRunesCount(List<int> perkIds, int primaryStyle, int subStyle)
    {
        var pTree = RuneService.Instance.AllTrees.FirstOrDefault(t => t.Id == primaryStyle) ?? RuneService.Instance.AllTrees.FirstOrDefault();
        if (pTree != null)
        {
            for (int i = 0; i < 4 && i < pTree.Slots.Count; i++)
            {
                if (perkIds.Count <= i)
                    perkIds.Add(pTree.Slots[i].Runes[0].Id);
            }
        }
        var sTree = RuneService.Instance.AllTrees.FirstOrDefault(t => t.Id == subStyle) ?? RuneService.Instance.AllTrees.LastOrDefault();
        if (sTree != null)
        {
            if (perkIds.Count <= 4 && sTree.Slots.Count > 1)
                perkIds.Add(sTree.Slots[1].Runes[0].Id);
            if (perkIds.Count <= 5 && sTree.Slots.Count > 2)
                perkIds.Add(sTree.Slots[2].Runes[0].Id);
        }
        while (perkIds.Count < 6)
            perkIds.Add(8008);
    }

    private static List<int> ParseItemIdsElement(JsonElement el)
    {
        var list = new List<int>();
        if (el.ValueKind == JsonValueKind.String)
        {
            var str = el.GetString() ?? "";
            foreach (var part in str.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int id)) list.Add(id);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int id))
                    list.Add(id);
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out int sid))
                    list.Add(sid);
            }
        }
        return list;
    }

    private static string FormatPatch(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "26.22";
        var parts = raw.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out int major))
        {
            if (major == 16) major = 26;
            return $"{major}.{parts[1]}";
        }
        return raw;
    }

    private static string CleanName(string s)
    {
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static int GetSpellId(string? name)
    {
        if (name == null) return 4;
        return name.ToLowerInvariant() switch
        {
            "cleanse" => 1,
            "exhaust" => 3,
            "flash" => 4,
            "ghost" => 6,
            "heal" => 7,
            "smite" => 11,
            "teleport" => 12,
            "ignite" => 14,
            "barrier" => 21,
            _ => 4
        };
    }
}
