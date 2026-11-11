using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class ChampionOverviewStat
{
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionKey { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string Role { get; set; } = "TOP";
    public string RoleLabel => Role.ToUpperInvariant() switch
    {
        "TOP" => "Top",
        "JUNGLE" => "Jungle",
        "MIDDLE" or "MID" => "Mid",
        "BOTTOM" or "BOT" or "ADC" => "ADC",
        "UTILITY" or "SUPPORT" => "Support",
        _ => Role
    };
    public int TierNumber { get; set; } = 3;
    public string TierLabel => TierNumber switch
    {
        1 => "S+",
        2 => "S",
        3 => "A",
        4 => "B",
        5 => "C",
        _ => "D"
    };
    public double WinRate { get; set; } = 0.5;
    public double PickRate { get; set; } = 0.05;
    public double BanRate { get; set; } = 0.05;
    public int Matches { get; set; } = 10000;
    public bool IsMostPopular { get; set; }
    public double MetaScore { get; set; }
}

public class ChampionSuggestionItem
{
    public int Rank { get; set; } // 1 to 10
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionKey { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string Role { get; set; } = "TOP";
    public string RoleLabel { get; set; } = "Top";
    public double WinRate { get; set; } = 0.5;
    public string WinRateFormatted => $"{WinRate * 100:F1}%";
    public double PickRate { get; set; } = 0.05;
    public string PickRateFormatted => $"{PickRate * 100:F1}%";
    public double BanRate { get; set; } = 0.05;
    public string BanRateFormatted => $"{BanRate * 100:F1}%";
    public int Matches { get; set; } = 10000;
    public string MatchesFormatted => Matches >= 1000 ? $"{Matches / 1000.0:F1}k" : Matches.ToString("N0");
    public string TierLabel { get; set; } = "S";
    public int MasteryLevel { get; set; } = 0;
    public long MasteryPoints { get; set; } = 0;
    public bool IsPersonalizedMastery { get; set; } = false;
    public string BadgeText { get; set; } = string.Empty;
    public string BadgeColorHex { get; set; } = "#2CE5B5";
    public double EnemyWinRateAgainstYou { get; set; } = 0.0;
    public string EnemyWinRateFormatted => $"{EnemyWinRateAgainstYou * 100:F1}%";
    public string ReasonDescription { get; set; } = string.Empty;
}

public class SuggestionService
{
    public static SuggestionService Instance { get; } = new SuggestionService();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<string, List<ChampionOverviewStat>> _overviewCache = new();
    private readonly Dictionary<int, (int Level, long Points)> _playerMasteryDict = new();
    private bool _isMasteryLoaded = false;
    private bool _isInitialized = false;

    private SuggestionService() { }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        await BuildDatabaseService.Instance.InitializeAsync();
        await RefreshPlayerMasteryAsync();
        _isInitialized = true;
    }

    public async Task RefreshPlayerMasteryAsync()
    {
        if (LcuApiClient.Instance.IsConnected)
        {
            try
            {
                string json = await LcuApiClient.Instance.GetTopChampionMasteryAsync();
                if (!string.IsNullOrEmpty(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    JsonElement arr = root.ValueKind == JsonValueKind.Array
                        ? root
                        : (root.TryGetProperty("masteries", out var mProp) && mProp.ValueKind == JsonValueKind.Array ? mProp : default);

                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        _playerMasteryDict.Clear();
                        foreach (var item in arr.EnumerateArray())
                        {
                            int cId = item.TryGetProperty("championId", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : 0;
                            int lvl = item.TryGetProperty("championLevel", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetInt32() : 1;
                            long pts = item.TryGetProperty("championPoints", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetInt64() : 0;
                            if (cId > 0)
                            {
                                _playerMasteryDict[cId] = (lvl, pts);
                            }
                        }
                        _isMasteryLoaded = _playerMasteryDict.Count > 0;
                        return;
                    }
                }
            }
            catch { }
        }

        // Demo fallback mastery if LCU is not connected
        if (!_isMasteryLoaded)
        {
            var pool = AppSettingsService.Instance.Settings.MasteryPoolChampIds;
            long basePts = 450000;
            for (int i = 0; i < pool.Count; i++)
            {
                int id = pool[i];
                int lvl = Math.Max(5, 7 - (i / 3));
                _playerMasteryDict[id] = (lvl, basePts - (i * 35000));
            }
            _isMasteryLoaded = true;
        }
    }

    // ==================== 1. PICK SUGGESTIONS (BLITZ OVERVIEW + PERSONALIZED MASTERY) ====================

    public async Task<List<ChampionSuggestionItem>> GetPickSuggestionsAsync(string role = "ALL", bool? forcePersonalized = null)
    {
        await InitializeAsync();

        string normRole = NormalizeRole(role);
        bool personalized = forcePersonalized ?? AppSettingsService.Instance.Settings.PickSuggestionPersonalizedMode;
        int minMatches = Math.Max(1000, AppSettingsService.Instance.Settings.PickSuggestionMinMatches);

        var overviewList = await GetOverviewStatsListAsync();

        // 1. Filter by Role
        IEnumerable<ChampionOverviewStat> roleCandidates;
        if (normRole == "ALL")
        {
            // Pick each champion's most popular role to avoid duplicates
            roleCandidates = overviewList
                .GroupBy(c => c.ChampionId)
                .Select(g => g.OrderByDescending(x => x.IsMostPopular).ThenByDescending(x => x.Matches).First());
        }
        else
        {
            roleCandidates = overviewList.Where(c => string.Equals(c.Role, normRole, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Filter by 10k Matches requirement
        var filteredList = roleCandidates.Where(c => c.Matches >= minMatches).ToList();
        if (filteredList.Count < 10)
        {
            // Graceful threshold relaxation if early patch has fewer 10k games
            filteredList = roleCandidates.OrderByDescending(c => c.Matches).Take(40).ToList();
        }

        // 3. Sort candidates by meta strength / tier / win rate * pick rate descending
        var topMetaCandidates = filteredList
            .OrderByDescending(c => c.MetaScore)
            .ThenByDescending(c => c.WinRate)
            .Take(30)
            .ToList();

        var finalPicks = new List<ChampionSuggestionItem>();
        var addedIds = new HashSet<int>();

        // 4. Personalized Mode Injection:
        // "if there is champion from your mastery in top 30 and that champion have bigger mastery than 4
        // it will place him first and move the rest of the line back if there is more champions with mastery
        // bigger than 4 place first with biggest mastery than second with second biggest ect... it will be able to place max 5 champs"
        if (personalized && _playerMasteryDict.Count > 0)
        {
            var masteryCandidates = topMetaCandidates
                .Where(c => _playerMasteryDict.TryGetValue(c.ChampionId, out var m) && m.Level > 4)
                .Select(c =>
                {
                    var m = _playerMasteryDict[c.ChampionId];
                    return new
                    {
                        Stat = c,
                        MasteryLevel = m.Level,
                        MasteryPoints = m.Points
                    };
                })
                .OrderByDescending(x => x.MasteryPoints)
                .ThenByDescending(x => x.MasteryLevel)
                .Take(Math.Clamp(AppSettingsService.Instance.Settings.PickSuggestionMaxMasteryPicks, 1, 5))
                .ToList();

            foreach (var mc in masteryCandidates)
            {
                var c = mc.Stat;
                addedIds.Add(c.ChampionId);
                finalPicks.Add(new ChampionSuggestionItem
                {
                    ChampionId = c.ChampionId,
                    ChampionName = c.ChampionName,
                    ChampionKey = c.ChampionKey,
                    IconUrl = c.IconUrl,
                    Role = c.Role,
                    RoleLabel = c.RoleLabel,
                    WinRate = c.WinRate,
                    PickRate = c.PickRate,
                    BanRate = c.BanRate,
                    Matches = c.Matches,
                    TierLabel = c.TierLabel,
                    MasteryLevel = mc.MasteryLevel,
                    MasteryPoints = mc.MasteryPoints,
                    IsPersonalizedMastery = true,
                    BadgeText = $"⭐ MASTERY PRIORITY (M{mc.MasteryLevel})",
                    BadgeColorHex = "#F0B232",
                    ReasonDescription = $"Top 30 Meta + Your Main ({mc.MasteryPoints / 1000:N0}k Mastery pts)"
                });
            }
        }

        // 5. Fill remaining slots from Top Meta candidates (max 10 total)
        foreach (var c in topMetaCandidates)
        {
            if (finalPicks.Count >= 10) break;
            if (addedIds.Contains(c.ChampionId)) continue;

            addedIds.Add(c.ChampionId);
            int mLvl = _playerMasteryDict.TryGetValue(c.ChampionId, out var mInfo) ? mInfo.Level : 0;
            long mPts = _playerMasteryDict.TryGetValue(c.ChampionId, out mInfo) ? mInfo.Points : 0;

            finalPicks.Add(new ChampionSuggestionItem
            {
                ChampionId = c.ChampionId,
                ChampionName = c.ChampionName,
                ChampionKey = c.ChampionKey,
                IconUrl = c.IconUrl,
                Role = c.Role,
                RoleLabel = c.RoleLabel,
                WinRate = c.WinRate,
                PickRate = c.PickRate,
                BanRate = c.BanRate,
                Matches = c.Matches,
                TierLabel = c.TierLabel,
                MasteryLevel = mLvl,
                MasteryPoints = mPts,
                IsPersonalizedMastery = false,
                BadgeText = c.TierNumber <= 2 ? $"🔥 TIER {c.TierLabel} META" : $"⚡ {c.RoleLabel.ToUpperInvariant()} META",
                BadgeColorHex = c.TierNumber == 1 ? "#2CE5B5" : (c.TierNumber == 2 ? "#5383E8" : "#8E92A4"),
                ReasonDescription = $"{c.TierLabel}-Tier Meta ({c.Matches / 1000.0:F1}k games, {c.WinRate * 100:F1}% WR)"
            });
        }

        // Ensure strictly max 10
        finalPicks = finalPicks.Take(10).ToList();

        // Assign Rank numbers 1 to 10
        for (int i = 0; i < finalPicks.Count; i++)
        {
            finalPicks[i].Rank = i + 1;
        }

        return finalPicks;
    }

    // ==================== 2. BAN SUGGESTIONS (LANE COUNTERS VS PREPICK OR 10K+ THREATS) ====================

    public async Task<List<ChampionSuggestionItem>> GetBanSuggestionsAsync(int? myChampId = null, string? role = null)
    {
        await InitializeAsync();

        string normRole = NormalizeRole(role);
        if (normRole == "ALL") normRole = "MIDDLE";

        var result = new List<ChampionSuggestionItem>();

        // Mode A: You have prepicked / selected a champion -> Show top 10 lane counters to your champion!
        if (myChampId.HasValue && myChampId.Value > 0)
        {
            var myInfo = BuildDatabaseService.Instance.GetChampionInfo(myChampId.Value);
            string myName = myInfo?.Name ?? "Selected Champion";

            var counters = await BuildDatabaseService.Instance.GetBiggestCountersToMyChampAsync(myChampId.Value, normRole);

            int rank = 1;
            foreach (var counter in counters.Take(10))
            {
                var oppInfo = BuildDatabaseService.Instance.GetChampionInfo(counter.OpponentChampionId);
                double enemyWr = counter.OpponentWinRateAgainstMe;
                int mLvl = _playerMasteryDict.TryGetValue(counter.OpponentChampionId, out var m) ? m.Level : 0;
                long mPts = _playerMasteryDict.TryGetValue(counter.OpponentChampionId, out m) ? m.Points : 0;

                result.Add(new ChampionSuggestionItem
                {
                    Rank = rank++,
                    ChampionId = counter.OpponentChampionId,
                    ChampionName = counter.OpponentName,
                    ChampionKey = oppInfo?.Key ?? counter.OpponentName,
                    IconUrl = counter.OpponentIconUrl,
                    Role = normRole,
                    RoleLabel = FormatRoleShort(normRole),
                    WinRate = 1.0 - enemyWr, // your win rate against this counter
                    PickRate = 0.08,
                    BanRate = 0.12,
                    Matches = counter.Games,
                    TierLabel = rank <= 3 ? "S" : "A",
                    MasteryLevel = mLvl,
                    MasteryPoints = mPts,
                    EnemyWinRateAgainstYou = enemyWr,
                    BadgeText = rank == 1 ? $"🛡️ #1 WORST COUNTER" : $"🛡️ HARD COUNTER ({enemyWr * 100:F1}%)",
                    BadgeColorHex = rank <= 3 ? "#E84057" : "#F0B232",
                    ReasonDescription = $"Counters {myName} {FormatRoleShort(normRole)} ({enemyWr * 100:F1}% Enemy Win Rate)"
                });
            }
        }

        // Mode B: You don't have prepicked -> Show top 10 champions with > 10k matches based on ban rate / meta threat!
        if (result.Count == 0)
        {
            var overviewList = await GetOverviewStatsListAsync();
            var laneCandidates = overviewList
                .Where(c => string.Equals(c.Role, normRole, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (laneCandidates.Count < 10)
                laneCandidates = overviewList.OrderByDescending(c => c.Matches).Take(40).ToList();

            // Sort by Ban Rate & Meta Threat descending
            var topThreats = laneCandidates
                .Where(c => c.Matches >= 10000)
                .OrderByDescending(c => c.BanRate)
                .ThenByDescending(c => c.WinRate)
                .Take(10)
                .ToList();

            if (topThreats.Count < 10)
            {
                topThreats = laneCandidates
                    .OrderByDescending(c => c.BanRate)
                    .ThenByDescending(c => c.WinRate)
                    .Take(10)
                    .ToList();
            }

            int rank = 1;
            foreach (var t in topThreats)
            {
                int mLvl = _playerMasteryDict.TryGetValue(t.ChampionId, out var m) ? m.Level : 0;
                long mPts = _playerMasteryDict.TryGetValue(t.ChampionId, out m) ? m.Points : 0;

                result.Add(new ChampionSuggestionItem
                {
                    Rank = rank++,
                    ChampionId = t.ChampionId,
                    ChampionName = t.ChampionName,
                    ChampionKey = t.ChampionKey,
                    IconUrl = t.IconUrl,
                    Role = t.Role,
                    RoleLabel = t.RoleLabel,
                    WinRate = t.WinRate,
                    PickRate = t.PickRate,
                    BanRate = t.BanRate,
                    Matches = t.Matches,
                    TierLabel = t.TierLabel,
                    MasteryLevel = mLvl,
                    MasteryPoints = mPts,
                    EnemyWinRateAgainstYou = t.WinRate,
                    BadgeText = rank <= 3 ? $"🚫 #1 MUST BAN ({t.BanRate * 100:F1}%)" : $"🚫 HIGH BAN RATE",
                    BadgeColorHex = rank <= 3 ? "#E84057" : "#F0B232",
                    ReasonDescription = $"{t.RoleLabel} Threat: {t.BanRate * 100:F1}% Ban Rate, {t.WinRate * 100:F1}% WR"
                });
            }
        }

        return result.Take(10).ToList();
    }

    // ==================== 3. COUNTER PICK SUGGESTIONS (VS ENEMY LANER OR FALLBACK TO META) ====================

    public async Task<List<ChampionSuggestionItem>> GetCounterPickSuggestionsAsync(int? enemyChampId, string? role, bool personalized)
    {
        await InitializeAsync();

        string normRole = NormalizeRole(role);
        if (normRole == "ALL") normRole = "MIDDLE";

        // If enemy champion is specified: find champions that beat enemyChampId
        if (enemyChampId.HasValue && enemyChampId.Value > 0)
        {
            var enemyInfo = BuildDatabaseService.Instance.GetChampionInfo(enemyChampId.Value);
            string enemyName = enemyInfo?.Name ?? "Enemy Laner";

            // Get counters to enemyChampId (i.e. champions where enemyChampId loses against them)
            var counters = await BuildDatabaseService.Instance.GetBiggestCountersToMyChampAsync(enemyChampId.Value, normRole);

            if (counters.Count > 0)
            {
                var candidateItems = new List<ChampionSuggestionItem>();
                foreach (var c in counters)
                {
                    var oppInfo = BuildDatabaseService.Instance.GetChampionInfo(c.OpponentChampionId);
                    if (oppInfo == null) continue;

                    double myWr = c.OpponentWinRateAgainstMe; // My win rate playing as this counter against enemy
                    int mLvl = _playerMasteryDict.TryGetValue(c.OpponentChampionId, out var m) ? m.Level : 0;
                    long mPts = _playerMasteryDict.TryGetValue(c.OpponentChampionId, out m) ? m.Points : 0;

                    candidateItems.Add(new ChampionSuggestionItem
                    {
                        ChampionId = c.OpponentChampionId,
                        ChampionName = c.OpponentName,
                        ChampionKey = oppInfo.Key,
                        IconUrl = c.OpponentIconUrl,
                        Role = normRole,
                        RoleLabel = FormatRoleShort(normRole),
                        WinRate = myWr,
                        PickRate = 0.08,
                        BanRate = 0.05,
                        Matches = c.Games,
                        TierLabel = myWr >= 0.54 ? "S+" : (myWr >= 0.52 ? "S" : "A"),
                        MasteryLevel = mLvl,
                        MasteryPoints = mPts,
                        EnemyWinRateAgainstYou = 1.0 - myWr,
                        IsPersonalizedMastery = personalized && mLvl > 4,
                        BadgeText = (personalized && mLvl > 4) 
                            ? $"⭐ MASTERY COUNTER (M{mLvl})" 
                            : $"🛡️ STATISTICAL COUNTER",
                        BadgeColorHex = (personalized && mLvl > 4) ? "#F0B232" : (myWr >= 0.53 ? "#2CE5B5" : "#5383E8"),
                        ReasonDescription = (personalized && mLvl > 4)
                            ? $"Counters {enemyName} ({myWr * 100:F1}% WR) + Your M{mLvl} Comfort ({mPts / 1000:N0}k pts)"
                            : $"Counters {enemyName} ({myWr * 100:F1}% Win Rate in {c.Games:N0} games)"
                    });
                }

                var finalPicks = new List<ChampionSuggestionItem>();
                var addedIds = new HashSet<int>();

                if (personalized)
                {
                    // Prioritize champions with Mastery > 4 (M5+) that counter the enemy, sorted by highest mastery
                    var masteryCounters = candidateItems
                        .Where(c => c.IsPersonalizedMastery)
                        .OrderByDescending(c => c.MasteryPoints)
                        .ThenByDescending(c => c.WinRate)
                        .Take(5)
                        .ToList();

                    foreach (var mc in masteryCounters)
                    {
                        addedIds.Add(mc.ChampionId);
                        finalPicks.Add(mc);
                    }
                }

                // Add remaining best statistical counters ordered by Win Rate descending
                foreach (var c in candidateItems.OrderByDescending(x => x.WinRate).ThenByDescending(x => x.Matches))
                {
                    if (finalPicks.Count >= 10) break;
                    if (addedIds.Contains(c.ChampionId)) continue;
                    addedIds.Add(c.ChampionId);
                    finalPicks.Add(c);
                }

                if (finalPicks.Count > 0)
                {
                    for (int i = 0; i < finalPicks.Count; i++)
                    {
                        finalPicks[i].Rank = i + 1;
                    }
                    return finalPicks.Take(10).ToList();
                }
            }
        }

        // If no enemy champion or no specific matchup counters found: fallback to general Pick Suggestions!
        return await GetPickSuggestionsAsync(normRole, personalized);
    }

    // ==================== OVERVIEW DATA FETCHER (BLITZ API + CRAWLER FALLBACK) ====================

    private async Task<List<ChampionOverviewStat>> GetOverviewStatsListAsync()
    {
        string cacheKey = "OVERVIEW_EMERALD_WORLD";
        if (_overviewCache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
            return cached;

        var list = new List<ChampionOverviewStat>();

        // 1. Try Online Blitz Overview API
        try
        {
            string cols = "all_roles_total_num_games,ban_num_games,champion_ban_rate,champion_id,champion_role_pickrate,champion_role_tier,champion_role_tier_filtered,games,individual_position,is_most_popular_role,patch,pick_rate,win_rate";
            string url = $"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats?queue_id=RANKED_SOLO_5X5&region=WORLD&tier=EMERALD%2B&columns={cols}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        int cId = item.TryGetProperty("champion_id", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : 0;
                        if (cId <= 0) continue;

                        var info = BuildDatabaseService.Instance.GetChampionInfo(cId);
                        if (info == null) continue;

                        string pos = item.TryGetProperty("individual_position", out var posP) ? posP.GetString() ?? "TOP" : "TOP";
                        int games = item.TryGetProperty("games", out var gp) && gp.ValueKind == JsonValueKind.Number ? gp.GetInt32() : 0;
                        double wr = item.TryGetProperty("win_rate", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetDouble() : 0.5;
                        double pr = item.TryGetProperty("pick_rate", out var prp) && prp.ValueKind == JsonValueKind.Number ? prp.GetDouble() : 0.05;
                        double br = item.TryGetProperty("champion_ban_rate", out var brp) && brp.ValueKind == JsonValueKind.Number ? brp.GetDouble() : 0.05;
                        int tier = item.TryGetProperty("champion_role_tier", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 3;
                        bool isPopular = item.TryGetProperty("is_most_popular_role", out var mp) && mp.ValueKind == JsonValueKind.True;

                        double tierWeight = tier switch
                        {
                            1 => 100.0,
                            2 => 80.0,
                            3 => 60.0,
                            4 => 40.0,
                            _ => 20.0
                        };
                        double metaScore = tierWeight + (wr * 50.0) + (pr * 30.0);

                        list.Add(new ChampionOverviewStat
                        {
                            ChampionId = cId,
                            ChampionName = info.Name,
                            ChampionKey = info.Key,
                            IconUrl = info.IconUrl,
                            Role = pos,
                            Matches = games,
                            WinRate = wr,
                            PickRate = pr,
                            BanRate = br,
                            TierNumber = tier,
                            IsMostPopular = isPopular,
                            MetaScore = metaScore
                        });
                    }
                }
            }
        }
        catch { }

        // 2. Fallback to Local Crawler Data (blitz_champions_data.json)
        if (list.Count < 20)
        {
            string[] possiblePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "crawler", "blitz_champions_data.json"),
                @"C:\Users\ufo\Downloads\TP\LoL_Helper_app\crawler\blitz_champions_data.json",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blitz_champions_data.json"),
                @"C:\Users\ufo\Downloads\TP\crawler\blitz_champions_data.json"
            };

            string path = possiblePaths.FirstOrDefault(File.Exists) ?? "";
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var champProp in doc.RootElement.EnumerateObject())
                    {
                        string champName = champProp.Name;
                        var champInfo = BuildDatabaseService.Instance.AllChampions.FirstOrDefault(c => string.Equals(c.Name, champName, StringComparison.OrdinalIgnoreCase))
                                        ?? new ChampionInfo { Id = 1, Key = champName, Name = champName };

                        if (champProp.Value.ValueKind != JsonValueKind.Object) continue;

                        foreach (var roleProp in champProp.Value.EnumerateObject())
                        {
                            string roleName = roleProp.Name;
                            var rVal = roleProp.Value;

                            int matches = rVal.TryGetProperty("Matches", out var mp) && mp.ValueKind == JsonValueKind.Number ? mp.GetInt32() : 12000;
                            double wr = rVal.TryGetProperty("Win Rate", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetDouble() : 0.5;
                            double pr = rVal.TryGetProperty("Pick Rate", out var prp) && prp.ValueKind == JsonValueKind.Number ? prp.GetDouble() : 0.05;
                            double br = rVal.TryGetProperty("Ban Rate", out var brp) && brp.ValueKind == JsonValueKind.Number ? brp.GetDouble() : 0.05;
                            int tier = rVal.TryGetProperty("Tier", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 3;
                            bool isPopular = rVal.TryGetProperty("IsMostPopular", out var imp) && imp.ValueKind == JsonValueKind.True;

                            double tierWeight = tier switch { 1 => 100.0, 2 => 80.0, 3 => 60.0, 4 => 40.0, _ => 20.0 };
                            double metaScore = tierWeight + (wr * 50.0) + (pr * 30.0);

                            list.Add(new ChampionOverviewStat
                            {
                                ChampionId = champInfo.Id,
                                ChampionName = champInfo.Name,
                                ChampionKey = champInfo.Key,
                                IconUrl = champInfo.IconUrl,
                                Role = roleName,
                                Matches = matches,
                                WinRate = wr,
                                PickRate = pr,
                                BanRate = br,
                                TierNumber = tier,
                                IsMostPopular = isPopular,
                                MetaScore = metaScore
                            });
                        }
                    }
                }
                catch { }
            }
        }

        if (list.Count > 0)
        {
            _overviewCache[cacheKey] = list;
        }

        return list;
    }

    private static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || role == "ALL") return "ALL";
        return role.Trim().ToUpperInvariant() switch
        {
            "TOP" => "TOP",
            "JUNGLE" or "JG" => "JUNGLE",
            "MID" or "MIDDLE" => "MIDDLE",
            "BOT" or "BOTTOM" or "ADC" => "BOTTOM",
            "SUP" or "SUPPORT" or "UTILITY" => "UTILITY",
            _ => "ALL"
        };
    }

    private static string FormatRoleShort(string role)
    {
        return (role ?? "MIDDLE").ToUpperInvariant() switch
        {
            "TOP" => "Top",
            "JUNGLE" => "Jungle",
            "MIDDLE" or "MID" => "Mid",
            "BOTTOM" or "BOT" => "ADC",
            "UTILITY" or "SUPPORT" => "Support",
            _ => "Mid"
        };
    }
}
