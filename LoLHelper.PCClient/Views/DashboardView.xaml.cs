using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public class MatchHistoryEntry
{
    public long GameId { get; set; }
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl { get; set; } = string.Empty;
    public int ChampLevel { get; set; } = 18;
    public string Role { get; set; } = "MIDDLE"; // TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY
    public string QueueType { get; set; } = "Ranked Solo"; // Ranked Solo, Ranked Flex, ARAM, Normal Draft
    public string QueueCategory { get; set; } = "SOLO"; // SOLO, FLEX, OTHER
    public bool Win { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int TotalCs { get; set; }
    public int GoldEarned { get; set; }
    public int DamageToChampions { get; set; }
    public int VisionScore { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime GameTimestamp { get; set; }
    public int Spell1Id { get; set; } = 4;
    public int Spell2Id { get; set; } = 14;
    public int KeystonePerkId { get; set; } = 8010;
    public int SubStyleTreeId { get; set; } = 8100;
    public int[] ItemIds { get; set; } = new int[7];
    public string MultiKillBadge { get; set; } = string.Empty;

    public double KdaRatio => (Kills + Assists) / (double)Math.Max(1, Deaths);
    public double CsPerMin => DurationSeconds > 0 ? TotalCs / (DurationSeconds / 60.0) : 0.0;
    public string DurationFormatted => $"{DurationSeconds / 60}m {DurationSeconds % 60:D2}s";

    public string TimeAgoFormatted
    {
        get
        {
            var diff = DateTime.Now - GameTimestamp;
            if (diff.TotalMinutes < 2) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 2) return "Yesterday";
            return $"{(int)diff.TotalDays}d ago";
        }
    }
}

public class RankedQueueSummary
{
    public string Tier { get; set; } = "EMERALD";
    public string Division { get; set; } = "II";
    public int LeaguePoints { get; set; } = 74;
    public int Wins { get; set; } = 68;
    public int Losses { get; set; } = 53;

    public double WinRate => (Wins + Losses) > 0 ? (Wins * 100.0) / (Wins + Losses) : 0.0;
    public string FormattedRank
    {
        get
        {
            string t = (Tier ?? "UNRANKED").ToUpperInvariant();
            if (t is "UNRANKED" or "NONE" or "") return "UNRANKED";
            if (t is "MASTER" or "GRANDMASTER" or "CHALLENGER") return t;
            return string.IsNullOrEmpty(Division) || Division == "NA" ? t : $"{t} {Division}";
        }
    }
}

public partial class DashboardView : UserControl
{
    public event Action<int, string>? OnOpenChampionBuildRequested;
    public event Action? OnOpenSettingsRequested;

    private bool _isInitialized = false;
    private bool _isSyncingAutomationUi = false;
    private string _currentQueueFilter = "ALL";
    private string _currentSearchFilter = string.Empty;

    // Profile State
    private string _summonerName = "Summoner";
    private string _summonerTag = "#EUW";
    private int _profileIconId = 588;
    private int _summonerLevel = 284;
    private long _xpSinceLastLevel = 2140;
    private long _xpUntilNextLevel = 2880;
    private string _puuid = string.Empty;
    private long _summonerId = 0;

    private RankedQueueSummary _soloRank = new();
    private RankedQueueSummary _flexRank = new()
    {
        Tier = "PLATINUM",
        Division = "I",
        LeaguePoints = 42,
        Wins = 31,
        Losses = 24
    };

    private List<MatchHistoryEntry> _last20Games = new();

    private static readonly Dictionary<int, string> SummonerSpellIcons = new()
    {
        { 1, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerBoost.png" },
        { 3, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerExhaust.png" },
        { 4, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerFlash.png" },
        { 6, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerHaste.png" },
        { 7, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerHeal.png" },
        { 11, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerSmite.png" },
        { 12, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerTeleport.png" },
        { 13, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerMana.png" },
        { 14, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerDot.png" },
        { 21, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerBarrier.png" },
        { 32, "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerSnowball.png" }
    };

    public DashboardView()
    {
        InitializeComponent();

        AppSettingsService.Instance.OnSettingsChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _ = SyncHomeAutomationBarAsync();
            });
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            _isInitialized = true;

            await BuildDatabaseService.Instance.InitializeAsync();
            await ItemService.Instance.InitializeAsync();
            await RuneService.Instance.InitializeAsync();

            if (LcuApiClient.Instance.IsConnected)
            {
                await RefreshLiveProfileAsync();
            }
            else
            {
                LoadDemoProfileData();
                RenderAllDashboardUI();
            }
        }

        await SyncHomeAutomationBarAsync();
    }

    // ==================== SYNCHRONIZED HOME AUTOMATION BAR ====================

    private async Task SyncHomeAutomationBarAsync()
    {
        if (_isSyncingAutomationUi) return;
        _isSyncingAutomationUi = true;
        try
        {
            var s = AppSettingsService.Instance.Settings;

            // 1. Auto Accept Switch & Subtitle
            HomeAutoAcceptSwitch.IsChecked = s.AutoAcceptEnabled;
            HomeAutoAcceptBadge.Background = BrushFromHex(s.AutoAcceptEnabled ? "#143D37" : "#25293A");
            HomeAutoAcceptBadgeText.Text = s.AutoAcceptEnabled ? "ON" : "OFF";
            HomeAutoAcceptBadgeText.Foreground = BrushFromHex(s.AutoAcceptEnabled ? "#2CE5B5" : "#8E92A4");

            string leaderPart = s.AutoAcceptOnlyPartyLeader ? "👑 Leader Only" : "All Lobby Roles";
            HomeAutoAcceptSubText.Text = $"Latency: {s.AutoAcceptDelayMs}ms (±{s.AutoAcceptRandomJitterMs}ms) • {leaderPart}";

            // 2. Auto Champ Select Switch & Subtitle
            HomeAutoChampSelectSwitch.IsChecked = s.AutoChampSelectEnabled;
            HomeAutoSelectBadge.Background = BrushFromHex(s.AutoChampSelectEnabled ? "#18324C" : "#25293A");
            HomeAutoSelectBadgeText.Text = s.AutoChampSelectEnabled ? "ON" : "OFF";
            HomeAutoSelectBadgeText.Foreground = BrushFromHex(s.AutoChampSelectEnabled ? "#5383E8" : "#8E92A4");

            HomeAutoSelectSubText.Text = $"Pick: {AppSettingsService.Instance.FormatPickStrategyShort()} • {AppSettingsService.Instance.FormatBanStrategyShort()}";

            // 3. Live Pick & Ban Preview Icons
            if (_isInitialized)
            {
                var preview = await AppSettingsService.Instance.ComputePreviewAsync();
                HomePreviewPickNameText.Text = preview.PickChampionName;
                HomePreviewPickSubText.Text = preview.PickReasonLabel;
                SetImageSourceSafe(HomePreviewPickImg, preview.PickChampionIconUrl);

                if (s.AutoBanEnabled)
                {
                    HomePreviewBanNameText.Text = preview.BanChampionName;
                    HomePreviewBanSubText.Text = preview.BanReasonLabel;
                    SetImageSourceSafe(HomePreviewBanImg, preview.BanChampionIconUrl);
                }
                else
                {
                    HomePreviewBanNameText.Text = "Off";
                    HomePreviewBanSubText.Text = "Auto Ban Disabled";
                    HomePreviewBanImg.Source = null;
                }
            }
        }
        finally
        {
            _isSyncingAutomationUi = false;
        }
    }

    private void HomeAutoAcceptSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingAutomationUi) return;
        AppSettingsService.Instance.SetAutoAccept(HomeAutoAcceptSwitch.IsChecked == true);
    }

    private void HomeAutoChampSelectSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingAutomationUi) return;
        AppSettingsService.Instance.SetAutoChampSelect(HomeAutoChampSelectSwitch.IsChecked == true);
    }

    private void HomeOpenSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        OnOpenSettingsRequested?.Invoke();
    }

    // ==================== PUBLIC LCU HOOKS ====================

    public void UpdateConnection(string status, Brush color)
    {
        ConnectionStatusText.Text = status.StartsWith("●") ? status : $"● {status}";
        ConnectionStatusText.Foreground = color;
    }

    public void UpdateSummoner(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && name != "Not Available")
        {
            _summonerName = name;
            SummonerNameText.Text = name;
        }
    }

    public void UpdateLogs(string log)
    {
        // Trigger a full live profile & 20-game history refresh when LCU connects
        _ = RefreshLiveProfileAsync();
    }

    private async void RefreshProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLiveProfileAsync();
    }

    // ==================== LIVE LCU FETCHING ====================

    public async Task RefreshLiveProfileAsync()
    {
        await BuildDatabaseService.Instance.InitializeAsync();
        await ItemService.Instance.InitializeAsync();
        await RuneService.Instance.InitializeAsync();

        if (!LcuApiClient.Instance.IsConnected)
        {
            if (_last20Games.Count == 0)
                LoadDemoProfileData();

            ProfileSourceBadge.Background = BrushFromHex("#352915");
            ConnectionStatusText.Text = "● Demo Preview (Launch LoL for Live Sync)";
            ConnectionStatusText.Foreground = BrushFromHex("#F0B232");
            RenderAllDashboardUI();
            return;
        }

        try
        {
            // 1. Current Summoner (Name, Tag, Icon, Level, XP)
            string sumJson = await LcuApiClient.Instance.GetCurrentSummonerAsync();
            if (!string.IsNullOrEmpty(sumJson))
            {
                using var doc = JsonDocument.Parse(sumJson);
                var root = doc.RootElement;

                string gName = root.TryGetProperty("gameName", out var gn) ? gn.GetString() ?? "" : "";
                string dName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                string tag = root.TryGetProperty("tagLine", out var tl) ? tl.GetString() ?? "" : "";

                _summonerName = !string.IsNullOrEmpty(gName) ? gName : (!string.IsNullOrEmpty(dName) ? dName : "Summoner");
                _summonerTag = !string.IsNullOrEmpty(tag) ? $"#{tag}" : "";
                _profileIconId = root.TryGetProperty("profileIconId", out var pi) ? pi.GetInt32() : 588;
                _summonerLevel = root.TryGetProperty("summonerLevel", out var sl) ? sl.GetInt32() : 1;
                _xpSinceLastLevel = root.TryGetProperty("xpSinceLastLevel", out var xps) ? xps.GetInt64() : 0;
                _xpUntilNextLevel = root.TryGetProperty("xpUntilNextLevel", out var xpu) ? Math.Max(1, xpu.GetInt64()) : 1000;
                _puuid = root.TryGetProperty("puuid", out var pu) ? pu.GetString() ?? "" : "";
                _summonerId = root.TryGetProperty("summonerId", out var sid) ? sid.GetInt64() : 0;
            }

            // 2. Ranked Stats (Solo/Duo & Flex)
            string rankedJson = await LcuApiClient.Instance.GetCurrentRankedStatsAsync();
            if (!string.IsNullOrEmpty(rankedJson))
            {
                ParseRankedStats(rankedJson);
            }

            // 3. Last 20 Games Match History
            string historyJson = await LcuApiClient.Instance.GetMatchHistoryAsync(_puuid, 0, 19);
            var parsedGames = ParseLcuMatchHistory(historyJson);
            if (parsedGames.Count > 0)
            {
                _last20Games = parsedGames;
                ProfileSourceBadge.Background = BrushFromHex("#123532");
                ConnectionStatusText.Text = "● Live LCU Account Synced";
                ConnectionStatusText.Foreground = BrushFromHex("#2CE5B5");
            }
            else
            {
                if (_last20Games.Count == 0)
                    LoadDemoProfileData();

                ProfileSourceBadge.Background = BrushFromHex("#123532");
                ConnectionStatusText.Text = "● LCU Connected (Sample Match Feed)";
                ConnectionStatusText.Foreground = BrushFromHex("#2CE5B5");
            }
        }
        catch
        {
            if (_last20Games.Count == 0)
                LoadDemoProfileData();
        }

        RenderAllDashboardUI();
    }

    private void ParseRankedStats(string rankedJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rankedJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("queueMap", out var qMap) && qMap.ValueKind == JsonValueKind.Object)
            {
                if (qMap.TryGetProperty("RANKED_SOLO_5x5", out var soloEl))
                    _soloRank = ParseQueueObject(soloEl);
                if (qMap.TryGetProperty("RANKED_FLEX_SR", out var flexEl))
                    _flexRank = ParseQueueObject(flexEl);
            }
            else if (root.TryGetProperty("queues", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qArr.EnumerateArray())
                {
                    string qType = q.TryGetProperty("queueType", out var qt) ? qt.GetString() ?? "" : "";
                    if (qType == "RANKED_SOLO_5x5") _soloRank = ParseQueueObject(q);
                    else if (qType == "RANKED_FLEX_SR") _flexRank = ParseQueueObject(q);
                }
            }
        }
        catch { }
    }

    private static RankedQueueSummary ParseQueueObject(JsonElement el)
    {
        string tier = el.TryGetProperty("tier", out var t) ? t.GetString() ?? "UNRANKED" : "UNRANKED";
        if (string.IsNullOrEmpty(tier) || tier == "NONE") tier = "UNRANKED";
        string div = el.TryGetProperty("division", out var d) ? d.GetString() ?? "" : "";
        int lp = el.TryGetProperty("leaguePoints", out var l) ? l.GetInt32() : 0;
        int wins = el.TryGetProperty("wins", out var w) ? w.GetInt32() : 0;
        int losses = el.TryGetProperty("losses", out var lo) ? lo.GetInt32() : 0;

        return new RankedQueueSummary
        {
            Tier = tier,
            Division = div,
            LeaguePoints = lp,
            Wins = wins,
            Losses = losses
        };
    }

    private List<MatchHistoryEntry> ParseLcuMatchHistory(string historyJson)
    {
        var list = new List<MatchHistoryEntry>();
        if (string.IsNullOrEmpty(historyJson)) return list;

        try
        {
            using var doc = JsonDocument.Parse(historyJson);
            var root = doc.RootElement;

            JsonElement gamesArr = default;
            if (root.TryGetProperty("games", out var gamesProp))
            {
                if (gamesProp.ValueKind == JsonValueKind.Array)
                    gamesArr = gamesProp;
                else if (gamesProp.ValueKind == JsonValueKind.Object && gamesProp.TryGetProperty("games", out var innerGames) && innerGames.ValueKind == JsonValueKind.Array)
                    gamesArr = innerGames;
            }

            if (gamesArr.ValueKind != JsonValueKind.Array) return list;

            foreach (var g in gamesArr.EnumerateArray())
            {
                if (list.Count >= 20) break;

                long gameId = g.TryGetProperty("gameId", out var gid) ? gid.GetInt64() : 0;
                int duration = g.TryGetProperty("gameDuration", out var dur) ? dur.GetInt32() : 1650;
                int queueId = g.TryGetProperty("queueId", out var qid) ? qid.GetInt32() : 420;
                long creationMs = g.TryGetProperty("gameCreation", out var gc) ? gc.GetInt64() : 0;
                DateTime timestamp = creationMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(creationMs).LocalDateTime
                    : DateTime.Now.AddHours(-list.Count * 2 - 1);

                if (!g.TryGetProperty("participants", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
                    continue;

                var p = parts[0];
                int champId = p.TryGetProperty("championId", out var cid) ? cid.GetInt32() : 266;
                int spell1 = p.TryGetProperty("spell1Id", out var s1) ? s1.GetInt32() : 4;
                int spell2 = p.TryGetProperty("spell2Id", out var s2) ? s2.GetInt32() : 14;

                var champInfo = BuildDatabaseService.Instance.GetChampionInfo(champId);
                string champName = champInfo?.Name ?? $"Champ #{champId}";
                string champIcon = champInfo?.IconUrl ?? $"https://ddragon.leagueoflegends.com/cdn/14.1.1/img/champion/Aatrox.png";

                string role = "MIDDLE";
                if (p.TryGetProperty("timeline", out var tl))
                {
                    string lane = tl.TryGetProperty("lane", out var ln) ? ln.GetString() ?? "" : "";
                    string r = tl.TryGetProperty("role", out var rl) ? rl.GetString() ?? "" : "";
                    role = NormalizeRole(lane, r, spell1, spell2);
                }

                bool win = false;
                int kills = 0, deaths = 0, assists = 0, cs = 0, gold = 0, dmg = 0, vision = 0, level = 18;
                int perk0 = 8010, subStyle = 8100;
                int[] items = new int[7];
                string multiKill = "";

                if (p.TryGetProperty("stats", out var st))
                {
                    win = st.TryGetProperty("win", out var w) && w.ValueKind == JsonValueKind.True;
                    kills = st.TryGetProperty("kills", out var k) ? k.GetInt32() : 0;
                    deaths = st.TryGetProperty("deaths", out var d) ? d.GetInt32() : 0;
                    assists = st.TryGetProperty("assists", out var a) ? a.GetInt32() : 0;
                    int minions = st.TryGetProperty("totalMinionsKilled", out var tm) ? tm.GetInt32() : 0;
                    int jungle = st.TryGetProperty("neutralMinionsKilled", out var nm) ? nm.GetInt32() : 0;
                    cs = minions + jungle;
                    gold = st.TryGetProperty("goldEarned", out var ge) ? ge.GetInt32() : 0;
                    dmg = st.TryGetProperty("totalDamageDealtToChampions", out var td) ? td.GetInt32() : 0;
                    vision = st.TryGetProperty("visionScore", out var vs) ? vs.GetInt32() : 0;
                    level = st.TryGetProperty("champLevel", out var cl) ? cl.GetInt32() : 18;
                    perk0 = st.TryGetProperty("perk0", out var p0) ? p0.GetInt32() : 8010;
                    subStyle = st.TryGetProperty("perkSubStyle", out var ps) ? ps.GetInt32() : 8100;

                    for (int i = 0; i < 7; i++)
                    {
                        if (st.TryGetProperty($"item{i}", out var itProp))
                            items[i] = itProp.GetInt32();
                    }

                    int penta = st.TryGetProperty("pentaKills", out var pk) ? pk.GetInt32() : 0;
                    int quadra = st.TryGetProperty("quadraKills", out var qk) ? qk.GetInt32() : 0;
                    int triple = st.TryGetProperty("tripleKills", out var tk) ? tk.GetInt32() : 0;
                    int dbl = st.TryGetProperty("doubleKills", out var dk) ? dk.GetInt32() : 0;

                    if (penta > 0) multiKill = "PENTA KILL";
                    else if (quadra > 0) multiKill = "QUADRA KILL";
                    else if (triple > 0) multiKill = "TRIPLE KILL";
                    else if (dbl > 0) multiKill = "DOUBLE KILL";
                    else if ((kills + assists) / (double)Math.Max(1, deaths) >= 6.0) multiKill = "MVP CARRY";
                }

                var (qName, qCat) = MapQueueId(queueId);

                list.Add(new MatchHistoryEntry
                {
                    GameId = gameId,
                    ChampionId = champId,
                    ChampionName = champName,
                    ChampionIconUrl = champIcon,
                    ChampLevel = level,
                    Role = role,
                    QueueType = qName,
                    QueueCategory = qCat,
                    Win = win,
                    Kills = kills,
                    Deaths = deaths,
                    Assists = assists,
                    TotalCs = cs,
                    GoldEarned = gold,
                    DamageToChampions = dmg,
                    VisionScore = vision,
                    DurationSeconds = duration,
                    GameTimestamp = timestamp,
                    Spell1Id = spell1,
                    Spell2Id = spell2,
                    KeystonePerkId = perk0,
                    SubStyleTreeId = subStyle,
                    ItemIds = items,
                    MultiKillBadge = multiKill
                });
            }
        }
        catch { }

        return list;
    }

    private static (string Name, string Category) MapQueueId(int queueId)
    {
        return queueId switch
        {
            420 => ("Ranked Solo", "SOLO"),
            440 => ("Ranked Flex", "FLEX"),
            450 => ("ARAM", "OTHER"),
            400 or 430 or 490 => ("Normal SR", "OTHER"),
            1700 => ("Arena", "OTHER"),
            _ => ("Ranked Solo", "SOLO")
        };
    }

    private static string NormalizeRole(string lane, string role, int spell1, int spell2)
    {
        if (spell1 == 11 || spell2 == 11 || lane.Equals("JUNGLE", StringComparison.OrdinalIgnoreCase))
            return "JUNGLE";
        if (lane.Equals("TOP", StringComparison.OrdinalIgnoreCase))
            return "TOP";
        if (lane.Equals("MIDDLE", StringComparison.OrdinalIgnoreCase) || lane.Equals("MID", StringComparison.OrdinalIgnoreCase))
            return "MIDDLE";
        if (role.Contains("SUPPORT", StringComparison.OrdinalIgnoreCase) || lane.Equals("UTILITY", StringComparison.OrdinalIgnoreCase))
            return "UTILITY";
        if (lane.Equals("BOTTOM", StringComparison.OrdinalIgnoreCase) || lane.Equals("BOT", StringComparison.OrdinalIgnoreCase))
            return "BOTTOM";
        return "MIDDLE";
    }

    // ==================== SHOWCASE / FALLBACK 20-GAME PROFILE ====================

    private void LoadDemoProfileData()
    {
        _summonerName = "TOMIXczech";
        _summonerTag = "#EUNE";
        _profileIconId = 588;
        _summonerLevel = 284;
        _xpSinceLastLevel = 2140;
        _xpUntilNextLevel = 2880;

        _soloRank = new RankedQueueSummary
        {
            Tier = "EMERALD",
            Division = "II",
            LeaguePoints = 74,
            Wins = 68,
            Losses = 53
        };
        _flexRank = new RankedQueueSummary
        {
            Tier = "PLATINUM",
            Division = "I",
            LeaguePoints = 42,
            Wins = 31,
            Losses = 24
        };

        var seeds = new (int ChampId, string Role, string Queue, string QCat, bool Win, int K, int D, int A, int Cs, int Gold, int Dmg, int Vis, int Dur, int S1, int S2, int Ks, int Sub, int[] Items, string Badge)[]
        {
            (266, "TOP", "Ranked Solo", "SOLO", true,  12, 2, 9,  248, 15840, 38420, 34, 1785, 4, 12, 8010, 8400, new[]{ 6610, 3111, 3053, 6333, 3071, 3026, 3363 }, "MVP CARRY"),
            (266, "TOP", "Ranked Solo", "SOLO", true,  9,  3, 11, 224, 14120, 31900, 29, 1690, 4, 12, 8010, 8400, new[]{ 6610, 3047, 3071, 6333, 3053, 1038, 3340 }, "TRIPLE KILL"),
            (777, "MIDDLE", "Ranked Solo", "SOLO", true, 15, 4, 7, 265, 16950, 42150, 31, 1890, 4, 14, 8008, 8400, new[]{ 3153, 3006, 6673, 3031, 6333, 3026, 3363 }, "QUADRA KILL"),
            (104, "JUNGLE", "Ranked Solo", "SOLO", true, 10, 3, 12, 212, 14780, 29400, 41, 1640, 4, 11, 8021, 8100, new[]{ 6676, 3111, 3071, 3036, 3156, 1036, 3364 }, "DOUBLE KILL"),
            (222, "BOTTOM", "Ranked Solo", "SOLO", false, 8,  6, 5,  238, 13400, 28100, 22, 1810, 4, 7,  8008, 8200, new[]{ 3031, 3006, 3085, 3036, 1038, 1055, 3363 }, "ACE"),
            (266, "TOP", "Ranked Solo", "SOLO", true,  11, 1, 8,  256, 15600, 36800, 36, 1720, 4, 12, 8010, 8400, new[]{ 6610, 3047, 6333, 3071, 3065, 1037, 3340 }, "UNSTOPPABLE"),
            (157, "MIDDLE", "Ranked Flex", "FLEX", true, 14, 5, 9,  244, 15920, 35600, 27, 1765, 4, 14, 8008, 8400, new[]{ 3153, 3006, 3031, 6673, 3026, 1038, 3363 }, "TRIPLE KILL"),
            (64,  "JUNGLE", "Ranked Solo", "SOLO", false, 5,  6, 10, 176, 11850, 19800, 44, 1680, 4, 11, 8010, 8300, new[]{ 6630, 3111, 3071, 3053, 1036, 0,    3364 }, "VISION KING"),
            (266, "TOP", "Ranked Solo", "SOLO", true,  8,  2, 12, 231, 14300, 30200, 33, 1710, 4, 12, 8010, 8400, new[]{ 6610, 3111, 3071, 6333, 3053, 0,    3340 }, "DOUBLE KILL"),
            (30,  "BOTTOM", "Ranked Solo", "SOLO", true,  16, 5, 14, 249, 17200, 48900, 28, 1940, 4, 3,  8128, 8000, new[]{ 6655, 3020, 4645, 3089, 3135, 3157, 3363 }, "PENTA KILL"),
            (412, "UTILITY", "Ranked Flex", "FLEX", true, 2,  3, 24, 42,  10900, 11400, 78, 1750, 4, 14, 8439, 8300, new[]{ 3869, 3117, 3190, 3109, 3050, 2055, 3364 }, "VISION KING"),
            (777, "MIDDLE", "Ranked Solo", "SOLO", false, 6,  7, 4,  215, 12100, 22400, 21, 1620, 4, 14, 8008, 8400, new[]{ 3153, 3006, 6673, 1038, 1055, 0,    3340 }, ""),
            (122, "TOP", "Ranked Solo", "SOLO", true,  13, 3, 6,  241, 15400, 34700, 26, 1695, 4, 6,  8010, 8400, new[]{ 3078, 3047, 3053, 3742, 3065, 1054, 3340 }, "TRIPLE KILL"),
            (103, "MIDDLE", "Ranked Solo", "SOLO", true,  10, 2, 13, 234, 14800, 33100, 35, 1730, 4, 14, 8112, 8200, new[]{ 6655, 3020, 4645, 3089, 3157, 1056, 3364 }, "MVP CARRY"),
            (145, "BOTTOM", "Ranked Solo", "SOLO", false, 7,  5, 6,  229, 13100, 25900, 24, 1770, 4, 7,  8008, 8300, new[]{ 6672, 3006, 3124, 3157, 1043, 0,    3363 }, ""),
            (266, "TOP", "Ranked Solo", "SOLO", true,  9,  4, 8,  219, 13900, 29700, 30, 1660, 4, 12, 8010, 8400, new[]{ 6610, 3047, 3071, 6333, 1037, 0,    3340 }, "DOUBLE KILL"),
            (238, "MIDDLE", "Normal SR", "OTHER", true,  17, 4, 6,  242, 16800, 41200, 29, 1810, 4, 14, 8112, 8200, new[]{ 6692, 3158, 6691, 6694, 3814, 3036, 3364 }, "QUADRA KILL"),
            (81,  "BOTTOM", "ARAM", "OTHER", false, 11, 8, 19, 84,  14900, 37500, 0,  1340, 4, 32, 8010, 8300, new[]{ 3078, 3158, 3042, 6694, 3153, 3072, 2052 }, "HIGH DAMAGE"),
            (64,  "JUNGLE", "Ranked Solo", "SOLO", false, 4,  6, 9,  168, 11200, 17400, 39, 1590, 4, 11, 8010, 8300, new[]{ 6630, 3047, 3071, 3156, 0,    0,    3364 }, ""),
            (266, "TOP", "Ranked Solo", "SOLO", true,  10, 2, 10, 237, 14950, 33600, 32, 1740, 4, 12, 8010, 8400, new[]{ 6610, 3111, 3071, 6333, 3053, 1028, 3340 }, "MVP CARRY")
        };

        _last20Games.Clear();
        int idx = 0;
        foreach (var s in seeds)
        {
            var cInfo = BuildDatabaseService.Instance.GetChampionInfo(s.ChampId);
            string cName = cInfo?.Name ?? $"Champion {s.ChampId}";
            string cIcon = cInfo?.IconUrl ?? "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/champion/Aatrox.png";

            _last20Games.Add(new MatchHistoryEntry
            {
                GameId = 7000000000L + idx,
                ChampionId = s.ChampId,
                ChampionName = cName,
                ChampionIconUrl = cIcon,
                ChampLevel = s.Win ? 18 : 16,
                Role = s.Role,
                QueueType = s.Queue,
                QueueCategory = s.QCat,
                Win = s.Win,
                Kills = s.K,
                Deaths = s.D,
                Assists = s.A,
                TotalCs = s.Cs,
                GoldEarned = s.Gold,
                DamageToChampions = s.Dmg,
                VisionScore = s.Vis,
                DurationSeconds = s.Dur,
                GameTimestamp = DateTime.Now.AddHours(-(idx * 3 + 1)),
                Spell1Id = s.S1,
                Spell2Id = s.S2,
                KeystonePerkId = s.Ks,
                SubStyleTreeId = s.Sub,
                ItemIds = s.Items,
                MultiKillBadge = s.Badge
            });
            idx++;
        }
    }

    // ==================== RENDERING ALL DASHBOARD SECTIONS ====================

    private void RenderAllDashboardUI()
    {
        RenderTopProfileAndRanks();
        RenderLast20SummaryAndForm();
        RenderTopChampionsSidebar();
        RenderRoleBreakdownSidebar();
        RenderMatchHistoryFeed();
    }

    private void RenderTopProfileAndRanks()
    {
        // 1. Summoner Card
        SummonerNameText.Text = _summonerName;
        SummonerTagText.Text = _summonerTag;
        SummonerLevelBadgeText.Text = $"LVL {_summonerLevel}";

        string version = ItemService.Instance.LatestVersion;
        string profileIconUrl = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/profileicon/{_profileIconId}.png";
        SetImageSourceSafe(ProfileIconImg, profileIconUrl);

        double xpPct = Math.Clamp((double)_xpSinceLastLevel / Math.Max(1, _xpUntilNextLevel), 0.0, 1.0);
        XpLabelText.Text = $"XP TO LEVEL {_summonerLevel + 1}";
        XpValueText.Text = $"{_xpSinceLastLevel:N0} / {_xpUntilNextLevel:N0} XP ({Math.Round(xpPct * 100):0}%)";
        XpProgressFill.Width = Math.Max(10, 210 * xpPct);

        // 2. Solo/Duo Rank Card
        string soloColor = GetRankColorHex(_soloRank.Tier);
        SetImageSourceSafe(SoloRankIconImg, GetRankIconUrl(_soloRank.Tier));
        SoloRankTierText.Text = _soloRank.FormattedRank;
        SoloRankTierText.Foreground = BrushFromHex(soloColor);
        SoloRankLpText.Text = $"{_soloRank.LeaguePoints} LP";
        SoloLpProgressFill.Background = BrushFromHex(soloColor);
        SoloLpProgressFill.Width = Math.Max(8, 150 * Math.Clamp(_soloRank.LeaguePoints / 100.0, 0.05, 1.0));
        SoloWlText.Text = $"{_soloRank.Wins}W {_soloRank.Losses}L";
        SoloWrText.Text = $"{_soloRank.WinRate:F1}% WR";
        SoloWrText.Foreground = BrushFromHex(_soloRank.WinRate >= 52.0 ? "#2CE5B5" : "#A5ACC4");

        // 3. Flex Rank Card
        string flexColor = GetRankColorHex(_flexRank.Tier);
        SetImageSourceSafe(FlexRankIconImg, GetRankIconUrl(_flexRank.Tier));
        FlexRankTierText.Text = _flexRank.FormattedRank;
        FlexRankTierText.Foreground = BrushFromHex(flexColor);
        FlexRankLpText.Text = $"{_flexRank.LeaguePoints} LP";
        FlexLpProgressFill.Background = BrushFromHex(flexColor);
        FlexLpProgressFill.Width = Math.Max(8, 150 * Math.Clamp(_flexRank.LeaguePoints / 100.0, 0.05, 1.0));
        FlexWlText.Text = $"{_flexRank.Wins}W {_flexRank.Losses}L";
        FlexWrText.Text = $"{_flexRank.WinRate:F1}% WR";
        FlexWrText.Foreground = BrushFromHex(_flexRank.WinRate >= 52.0 ? "#37C8B7" : "#A5ACC4");
    }

    private void RenderLast20SummaryAndForm()
    {
        if (_last20Games.Count == 0) return;

        int total = _last20Games.Count;
        int wins = _last20Games.Count(g => g.Win);
        int losses = total - wins;
        double wrPct = (wins * 100.0) / total;

        double avgK = _last20Games.Average(g => g.Kills);
        double avgD = _last20Games.Average(g => g.Deaths);
        double avgA = _last20Games.Average(g => g.Assists);
        double overallKda = (avgK + avgA) / Math.Max(0.5, avgD);

        double avgCsM = _last20Games.Average(g => g.CsPerMin);
        double avgDmg = _last20Games.Average(g => g.DamageToChampions);
        double avgGold = _last20Games.Average(g => g.GoldEarned);
        double avgVis = _last20Games.Average(g => g.VisionScore);

        Last20KdaRatioText.Text = $"{overallKda:F2} : 1 KDA";
        Last20KdaRatioText.Foreground = BrushFromHex(GetKdaColorHex(overallKda));
        Last20AvgKdaText.Text = $"{avgK:F1} / {avgD:F1} / {avgA:F1}";
        Last20WlText.Text = $"{wins}W - {losses}L ({wrPct:F0}%)";

        AvgCsText.Text = $"{avgCsM:F1} CS/m";
        AvgDmgText.Text = $"{avgDmg / 1000.0:F1}k DMG";
        AvgGoldText.Text = $"{avgGold / 1000.0:F1}k Gold";
        AvgVisionText.Text = $"{avgVis:F1} Vis";

        // Calculate current streak from newest game
        bool firstWin = _last20Games[0].Win;
        int streak = 0;
        foreach (var g in _last20Games)
        {
            if (g.Win == firstWin) streak++;
            else break;
        }

        if (firstWin && streak >= 2)
        {
            StreakBadgeBorder.Background = BrushFromHex("#2D2218");
            StreakBadgeBorder.BorderBrush = BrushFromHex("#F0B232");
            StreakBadgeText.Text = $"🔥 {streak} WIN STREAK";
            StreakBadgeText.Foreground = BrushFromHex("#F0B232");
        }
        else if (!firstWin && streak >= 2)
        {
            StreakBadgeBorder.Background = BrushFromHex("#2E1920");
            StreakBadgeBorder.BorderBrush = BrushFromHex("#E84057");
            StreakBadgeText.Text = $"❄️ {streak} LOSS STREAK";
            StreakBadgeText.Foreground = BrushFromHex("#E84057");
        }
        else
        {
            StreakBadgeBorder.Background = BrushFromHex("#152F2C");
            StreakBadgeBorder.BorderBrush = BrushFromHex("#2CE5B5");
            StreakBadgeText.Text = $"⚡ {wrPct:F0}% RECENT WR";
            StreakBadgeText.Foreground = BrushFromHex("#2CE5B5");
        }

        // Update Win/Loss split bar
        WinBarCol.Width = new GridLength(Math.Max(1, wins), GridUnitType.Star);
        LossBarCol.Width = new GridLength(Math.Max(1, losses), GridUnitType.Star);

        // Populate 20-Game Mini Portrait Form Strip
        RecentFormWrapPanel.Children.Clear();
        foreach (var game in _last20Games)
        {
            string borderCol = game.Win ? "#2CE5B5" : "#E84057";
            string bgCol = game.Win ? "#132A27" : "#2C161D";

            var tile = new Border
            {
                Width = 27,
                Height = 27,
                CornerRadius = new CornerRadius(5),
                BorderBrush = BrushFromHex(borderCol),
                BorderThickness = new Thickness(1.5),
                Background = BrushFromHex(bgCol),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                ToolTip = $"{game.ChampionName} ({(game.Win ? "VICTORY" : "DEFEAT")})\nKDA: {game.Kills}/{game.Deaths}/{game.Assists} ({game.KdaRatio:F2}:1)\nClick to open {game.ChampionName} build"
            };

            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, game.ChampionIconUrl);
            tile.Child = img;

            int cId = game.ChampionId;
            string cRole = game.Role;
            tile.MouseLeftButtonDown += (s, e) => OnOpenChampionBuildRequested?.Invoke(cId, cRole);

            RecentFormWrapPanel.Children.Add(tile);
        }
    }

    private void RenderTopChampionsSidebar()
    {
        TopChampionsStack.Children.Clear();
        if (_last20Games.Count == 0) return;

        var grouped = _last20Games
            .GroupBy(g => g.ChampionId)
            .Select(grp =>
            {
                var first = grp.First();
                int count = grp.Count();
                int wins = grp.Count(x => x.Win);
                double wr = (wins * 100.0) / count;
                double k = grp.Average(x => x.Kills);
                double d = grp.Average(x => x.Deaths);
                double a = grp.Average(x => x.Assists);
                double kda = (k + a) / Math.Max(0.5, d);
                return new
                {
                    ChampId = grp.Key,
                    first.ChampionName,
                    first.ChampionIconUrl,
                    first.Role,
                    Games = count,
                    Wins = wins,
                    Losses = count - wins,
                    WinRate = wr,
                    AvgK = k,
                    AvgD = d,
                    AvgA = a,
                    Kda = kda
                };
            })
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.WinRate)
            .Take(5)
            .ToList();

        foreach (var ch in grouped)
        {
            var rowBorder = new Border
            {
                Background = BrushFromHex("#1A1D28"),
                BorderBrush = BrushFromHex("#262A3A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = $"Click to view {ch.ChampionName} builds & runes"
            };

            rowBorder.MouseEnter += (s, e) => rowBorder.Background = BrushFromHex("#222636");
            rowBorder.MouseLeave += (s, e) => rowBorder.Background = BrushFromHex("#1A1D28");
            int cId = ch.ChampId;
            string cRole = ch.Role;
            rowBorder.MouseLeftButtonDown += (s, e) => OnOpenChampionBuildRequested?.Invoke(cId, cRole);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon
            var iconBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 10, 0),
                BorderBrush = BrushFromHex("#32374D"),
                BorderThickness = new Thickness(1)
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, ch.ChampionIconUrl);
            iconBorder.Child = img;
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Name + Avg KDA
            var midStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            midStack.Children.Add(new TextBlock
            {
                Text = ch.ChampionName,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold
            });
            midStack.Children.Add(new TextBlock
            {
                Text = $"{ch.Kda:F2}:1 KDA  ({ch.AvgK:F1}/{ch.AvgD:F1}/{ch.AvgA:F1})",
                Foreground = BrushFromHex(GetKdaColorHex(ch.Kda)),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            });
            Grid.SetColumn(midStack, 1);
            grid.Children.Add(midStack);

            // Win Rate + W/L
            var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            rightStack.Children.Add(new TextBlock
            {
                Text = $"{ch.WinRate:F0}% WR",
                Foreground = BrushFromHex(ch.WinRate >= 55 ? "#2CE5B5" : (ch.WinRate >= 50 ? "#E5E9F0" : "#E84057")),
                FontSize = 12,
                FontWeight = FontWeights.Black,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = $"{ch.Wins}W - {ch.Losses}L ({ch.Games}G)",
                Foreground = BrushFromHex("#8E92A4"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            rowBorder.Child = grid;
            TopChampionsStack.Children.Add(rowBorder);
        }
    }

    private void RenderRoleBreakdownSidebar()
    {
        RoleBreakdownStack.Children.Clear();
        if (_last20Games.Count == 0) return;

        var roles = new (string Key, string Label)[]
        {
            ("TOP", "Top Lane"),
            ("JUNGLE", "Jungle"),
            ("MIDDLE", "Mid Lane"),
            ("BOTTOM", "Bot Carry"),
            ("UTILITY", "Support")
        };

        int maxRoleGames = Math.Max(1, _last20Games.GroupBy(g => g.Role).Select(g => g.Count()).DefaultIfEmpty(1).Max());

        foreach (var r in roles)
        {
            var matches = _last20Games.Where(g => g.Role.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            int count = matches.Count;
            int wins = matches.Count(g => g.Win);
            double wr = count > 0 ? (wins * 100.0) / count : 0.0;

            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

            // Role Icon
            var roleImg = new Image
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(roleImg, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(roleImg, GetRoleIconUrl(r.Key));
            Grid.SetColumn(roleImg, 0);
            rowGrid.Children.Add(roleImg);

            // Role Name
            var roleLbl = new TextBlock
            {
                Text = r.Label,
                Foreground = BrushFromHex("#D8DEE9"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(roleLbl, 1);
            rowGrid.Children.Add(roleLbl);

            // Progress Bar
            var barBg = new Border
            {
                Height = 7,
                Background = BrushFromHex("#1F2230"),
                CornerRadius = new CornerRadius(3.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
                ClipToBounds = true
            };
            double ratio = (double)count / maxRoleGames;
            var barFill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = count > 0 ? Math.Max(10, 105 * ratio) : 0,
                Background = BrushFromHex(wr >= 50 ? "#5383E8" : "#616A82"),
                CornerRadius = new CornerRadius(3.5)
            };
            barBg.Child = barFill;
            Grid.SetColumn(barBg, 2);
            rowGrid.Children.Add(barBg);

            // Stats Text
            var statTxt = new TextBlock
            {
                Text = count > 0 ? $"{count}G • {wr:F0}% WR" : "0G",
                Foreground = BrushFromHex(count > 0 ? (wr >= 55 ? "#2CE5B5" : "#A5ACC4") : "#555B70"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statTxt, 3);
            rowGrid.Children.Add(statTxt);

            RoleBreakdownStack.Children.Add(rowGrid);
        }
    }

    // ==================== 20-GAME MATCH HISTORY FEED ====================

    private void QueueFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked || clicked.Tag is not string filterTag) return;
        _currentQueueFilter = filterTag;

        var buttons = new[] { FilterAllBtn, FilterSoloBtn, FilterFlexBtn, FilterOtherBtn };
        foreach (var b in buttons)
        {
            b.Background = Brushes.Transparent;
            b.Foreground = BrushFromHex("#8E92A4");
        }

        clicked.Background = BrushFromHex("#25293A");
        clicked.Foreground = Brushes.White;

        RenderMatchHistoryFeed();
    }

    private void MatchSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentSearchFilter = MatchSearchBox.Text.Trim();
        MatchSearchPlaceholder.Visibility = string.IsNullOrEmpty(MatchSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RenderMatchHistoryFeed();
    }

    private void RenderMatchHistoryFeed()
    {
        MatchHistoryListStack.Children.Clear();
        FilterAllBtn.Content = $"All Games ({_last20Games.Count})";

        var filtered = _last20Games.Where(g =>
        {
            if (_currentQueueFilter != "ALL" && !g.QueueCategory.Equals(_currentQueueFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(_currentSearchFilter) && !g.ChampionName.Contains(_currentSearchFilter, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            MatchHistoryListStack.Children.Add(new TextBlock
            {
                Text = "No matches found for the selected filter.",
                Foreground = BrushFromHex("#7E849B"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        int maxDamage = Math.Max(10000, _last20Games.Select(g => g.DamageToChampions).DefaultIfEmpty(25000).Max());

        foreach (var game in filtered)
        {
            MatchHistoryListStack.Children.Add(CreateMatchHistoryCard(game, maxDamage));
        }
    }

    private UIElement CreateMatchHistoryCard(MatchHistoryEntry game, int maxDamage)
    {
        string accentHex = game.Win ? "#2CE5B5" : "#E84057";
        string cardBgHex = game.Win ? "#141D24" : "#21161E";
        string cardHoverHex = game.Win ? "#18252E" : "#291B25";
        string borderHex = game.Win ? "#1F363B" : "#3A222C";

        var cardBorder = new Border
        {
            Background = BrushFromHex(cardBgHex),
            BorderBrush = BrushFromHex(borderHex),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 7),
            ClipToBounds = true
        };

        cardBorder.MouseEnter += (s, e) => cardBorder.Background = BrushFromHex(cardHoverHex);
        cardBorder.MouseLeave += (s, e) => cardBorder.Background = BrushFromHex(cardBgHex);

        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // Colored Victory/Defeat bar
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) }); // Outcome + Queue + Duration
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) }); // Champ + Spells + Runes + Role
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); // KDA + Multi-kill badge
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) }); // CS + Gold + Damage bar
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 7-Item Strip
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Action Button

        // Col 0: Left Status Strip
        var strip = new Border { Background = BrushFromHex(accentHex) };
        Grid.SetColumn(strip, 0);
        outerGrid.Children.Add(strip);

        // Col 1: Outcome, Queue, Time Ago, Duration
        var col1 = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 8, 6, 8) };
        col1.Children.Add(new TextBlock
        {
            Text = game.Win ? "VICTORY" : "DEFEAT",
            Foreground = BrushFromHex(accentHex),
            FontSize = 12,
            FontWeight = FontWeights.Black
        });
        col1.Children.Add(new TextBlock
        {
            Text = game.QueueType,
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 1, 0, 2)
        });
        col1.Children.Add(new TextBlock
        {
            Text = $"{game.DurationFormatted} • {game.TimeAgoFormatted}",
            Foreground = BrushFromHex("#8A90A6"),
            FontSize = 10
        });
        Grid.SetColumn(col1, 1);
        outerGrid.Children.Add(col1);

        // Col 2: Champion Icon + Level Badge + Spells + Runes + Name/Role
        var col2Grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 6, 6, 6) };
        col2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        col2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        col2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        col2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Champion Portrait + Level + Role badge
        var champBox = new Grid { Width = 46, Height = 46, Margin = new Thickness(0, 0, 6, 0) };
        var champImgBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(8),
            BorderBrush = BrushFromHex(accentHex),
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true
        };
        var champImg = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(champImg, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(champImg, game.ChampionIconUrl);
        champImgBorder.Child = champImg;
        champBox.Children.Add(champImgBorder);

        var lvlBadge = new Border
        {
            Background = BrushFromHex("#11131A"),
            BorderBrush = BrushFromHex("#34394E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3, 0, 3, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        lvlBadge.Child = new TextBlock
        {
            Text = game.ChampLevel.ToString(),
            Foreground = Brushes.White,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold
        };
        champBox.Children.Add(lvlBadge);
        Grid.SetColumn(champBox, 0);
        col2Grid.Children.Add(champBox);

        // 2 Summoner Spells
        var spellsStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        spellsStack.Children.Add(CreateSmallIconBorder(GetSummonerSpellIconUrl(game.Spell1Id), 20, 4, new Thickness(0, 0, 0, 3)));
        spellsStack.Children.Add(CreateSmallIconBorder(GetSummonerSpellIconUrl(game.Spell2Id), 20, 4, new Thickness(0)));
        Grid.SetColumn(spellsStack, 1);
        col2Grid.Children.Add(spellsStack);

        // Keystone + Secondary Tree
        var runesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        string ksUrl = RuneService.Instance.GetImageUrl(game.KeystonePerkId);
        string subUrl = RuneService.Instance.GetImageUrl(game.SubStyleTreeId);
        runesStack.Children.Add(CreateSmallIconBorder(ksUrl, 21, 10, new Thickness(0, 0, 0, 2), "#11131A"));
        runesStack.Children.Add(CreateSmallIconBorder(subUrl, 19, 9, new Thickness(0), "#11131A"));
        Grid.SetColumn(runesStack, 2);
        col2Grid.Children.Add(runesStack);

        // Champ Name + Role Icon
        var nameRoleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameRoleStack.Children.Add(new TextBlock
        {
            Text = game.ChampionName,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var roleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var roleIcon = new Image { Width = 13, Height = 13, Margin = new Thickness(0, 0, 4, 0) };
        RenderOptions.SetBitmapScalingMode(roleIcon, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(roleIcon, GetRoleIconUrl(game.Role));
        roleRow.Children.Add(roleIcon);
        roleRow.Children.Add(new TextBlock
        {
            Text = FormatRoleShort(game.Role),
            Foreground = BrushFromHex("#8E92A4"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        });
        nameRoleStack.Children.Add(roleRow);
        Grid.SetColumn(nameRoleStack, 3);
        col2Grid.Children.Add(nameRoleStack);

        Grid.SetColumn(col2Grid, 2);
        outerGrid.Children.Add(col2Grid);

        // Col 3: KDA & Multi-Kill / MVP Badge
        var col3 = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 6, 6, 6) };
        col3.Children.Add(new TextBlock
        {
            Text = $"{game.Kills} / {game.Deaths} / {game.Assists}",
            Foreground = Brushes.White,
            FontSize = 13.5,
            FontWeight = FontWeights.Black
        });

        var kdaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        kdaRow.Children.Add(new TextBlock
        {
            Text = $"{game.KdaRatio:F2}:1 KDA",
            Foreground = BrushFromHex(GetKdaColorHex(game.KdaRatio)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!string.IsNullOrEmpty(game.MultiKillBadge))
        {
            bool isPentaOrQuad = game.MultiKillBadge.Contains("PENTA") || game.MultiKillBadge.Contains("QUADRA");
            string badgeBg = isPentaOrQuad ? "#3A1E28" : "#23283B";
            string badgeFg = isPentaOrQuad ? "#FF7A90" : "#F0B232";

            var badge = new Border
            {
                Background = BrushFromHex(badgeBg),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = game.MultiKillBadge,
                Foreground = BrushFromHex(badgeFg),
                FontSize = 9,
                FontWeight = FontWeights.Black
            };
            kdaRow.Children.Add(badge);
        }
        col3.Children.Add(kdaRow);
        Grid.SetColumn(col3, 3);
        outerGrid.Children.Add(col3);

        // Col 4: CS, Gold, Vision & Damage Dealt Bar
        var col4 = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 6, 8, 6) };
        col4.Children.Add(new TextBlock
        {
            Text = $"🌾 {game.TotalCs} CS ({game.CsPerMin:F1}/m)  •  👁️ {game.VisionScore}",
            Foreground = BrushFromHex("#C5CCE0"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        });
        col4.Children.Add(new TextBlock
        {
            Text = $"⚔️ {game.DamageToChampions:N0} DMG  •  🪙 {game.GoldEarned / 1000.0:F1}k",
            Foreground = BrushFromHex("#9BA3BA"),
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 3)
        });

        var dmgBarBg = new Border
        {
            Height = 5,
            Width = 130,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = BrushFromHex("#1A1C26"),
            CornerRadius = new CornerRadius(2.5),
            ClipToBounds = true
        };
        double dmgRatio = Math.Clamp((double)game.DamageToChampions / maxDamage, 0.08, 1.0);
        dmgBarBg.Child = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 130 * dmgRatio,
            Background = BrushFromHex("#E84057"),
            CornerRadius = new CornerRadius(2.5)
        };
        col4.Children.Add(dmgBarBg);
        Grid.SetColumn(col4, 4);
        outerGrid.Children.Add(col4);

        // Col 5: 7-Slot Item Build Icons Strip
        var itemsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0)
        };

        for (int i = 0; i < 7; i++)
        {
            int itemId = i < game.ItemIds.Length ? game.ItemIds[i] : 0;
            bool isTrinket = i == 6;

            var slotBorder = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(isTrinket ? 15 : 6),
                Background = BrushFromHex("#151722"),
                BorderBrush = BrushFromHex("#2A2E40"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(isTrinket ? 6 : 0, 0, 3, 0),
                ClipToBounds = true
            };

            if (itemId > 0)
            {
                var gItem = ItemService.Instance.GetItem(itemId);
                string version = ItemService.Instance.LatestVersion;
                string itemUrl = gItem?.IconUrl ?? $"https://ddragon.leagueoflegends.com/cdn/{version}/img/item/{itemId}.png";
                var itemImg = new Image { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(itemImg, BitmapScalingMode.HighQuality);
                SetImageSourceSafe(itemImg, itemUrl);
                slotBorder.Child = itemImg;
                slotBorder.ToolTip = gItem != null ? $"{gItem.Name} ({gItem.Gold}g)" : $"Item #{itemId}";
            }

            itemsPanel.Children.Add(slotBorder);
        }
        Grid.SetColumn(itemsPanel, 5);
        outerGrid.Children.Add(itemsPanel);

        // Col 6: Quick Jump to Champion Build Button
        var openBuildBtn = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0),
            Cursor = Cursors.Hand,
            ToolTip = $"Open {game.ChampionName} ({FormatRoleShort(game.Role)}) Builds & Runes"
        };
        var btnBorderFactory = new FrameworkElementFactory(typeof(Border));
        btnBorderFactory.SetValue(Border.BackgroundProperty, BrushFromHex("#232738"));
        btnBorderFactory.SetValue(Border.BorderBrushProperty, BrushFromHex("#343A52"));
        btnBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        btnBorderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        btnBorderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

        var txtFactory = new FrameworkElementFactory(typeof(TextBlock));
        txtFactory.SetValue(TextBlock.TextProperty, "⚔️ Build");
        txtFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        txtFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
        txtFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        btnBorderFactory.AppendChild(txtFactory);

        openBuildBtn.Template = new ControlTemplate(typeof(Button)) { VisualTree = btnBorderFactory };
        int targetChampId = game.ChampionId;
        string targetRole = game.Role;
        openBuildBtn.Click += (s, e) => OnOpenChampionBuildRequested?.Invoke(targetChampId, targetRole);

        Grid.SetColumn(openBuildBtn, 6);
        outerGrid.Children.Add(openBuildBtn);

        cardBorder.Child = outerGrid;
        return cardBorder;
    }

    // ==================== ICON & COLOR HELPERS ====================

    private static Border CreateSmallIconBorder(string url, double size, double radius, Thickness margin, string bgHex = "#181A24")
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Background = BrushFromHex(bgHex),
            BorderBrush = BrushFromHex("#2B3042"),
            BorderThickness = new Thickness(1),
            Margin = margin,
            ClipToBounds = true
        };
        if (!string.IsNullOrEmpty(url))
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, url);
            b.Child = img;
        }
        return b;
    }

    private static string GetSummonerSpellIconUrl(int spellId)
    {
        return SummonerSpellIcons.TryGetValue(spellId, out var url)
            ? url
            : "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerFlash.png";
    }

    private static string GetRoleIconUrl(string role)
    {
        string r = (role ?? "MIDDLE").ToUpperInvariant();
        return r switch
        {
            "TOP" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-top.png",
            "JUNGLE" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-jungle.png",
            "MIDDLE" or "MID" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-middle.png",
            "BOTTOM" or "BOT" or "ADC" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-bottom.png",
            "UTILITY" or "SUPPORT" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-utility.png",
            _ => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-clash/global/default/assets/images/position-selector/positions/icon-position-middle.png"
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

    private static string GetRankIconUrl(string tierOrRank)
    {
        string clean = (tierOrRank ?? "EMERALD")
            .Replace("+", "")
            .Trim()
            .Split(' ')[0]
            .ToLowerInvariant();

        return clean switch
        {
            "challenger" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/challenger.png",
            "grandmaster" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/grandmaster.png",
            "master" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/master.png",
            "diamond" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/diamond.png",
            "emerald" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/emerald.png",
            "platinum" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/platinum.png",
            "gold" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/gold.png",
            "silver" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/silver.png",
            "bronze" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/bronze.png",
            "iron" => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/iron.png",
            _ => "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/emerald.png"
        };
    }

    private static string GetRankColorHex(string tierOrRank)
    {
        string clean = (tierOrRank ?? "EMERALD")
            .Replace("+", "")
            .Trim()
            .Split(' ')[0]
            .ToUpperInvariant();

        return clean switch
        {
            "CHALLENGER" => "#F4C874",
            "GRANDMASTER" => "#FF6B6B",
            "MASTER" => "#D078FF",
            "DIAMOND" => "#5799FF",
            "EMERALD" => "#2CE5B5",
            "PLATINUM" => "#37C8B7",
            "GOLD" => "#E5B454",
            "SILVER" => "#A9B8C6",
            "BRONZE" => "#C6865A",
            "IRON" => "#948A87",
            _ => "#2CE5B5"
        };
    }

    private static string GetKdaColorHex(double kda)
    {
        if (kda >= 5.0) return "#F0B232"; // Legendary Gold
        if (kda >= 3.5) return "#2CE5B5"; // Emerald Teal
        if (kda >= 2.5) return "#5383E8"; // Diamond Blue
        return "#A5ACC4";
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private static void SetImageSourceSafe(Image img, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            img.Source = null;
            return;
        }
        try
        {
            img.Source = new BitmapImage(new Uri(url));
        }
        catch { }
    }
}
