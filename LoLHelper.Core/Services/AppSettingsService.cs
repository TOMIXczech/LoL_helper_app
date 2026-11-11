using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class AppSettings
{
    // ==================== 1. AUTO ACCEPT SETTINGS ====================
    public bool AutoAcceptEnabled { get; set; } = false;
    public bool AutoAcceptOnlyPartyLeader { get; set; } = false;
    public int AutoAcceptDelayMs { get; set; } = 850;
    public int AutoAcceptRandomJitterMs { get; set; } = 150;
    public bool AutoAcceptPauseAfterDecline { get; set; } = true;
    public bool AutoAcceptRankedSolo { get; set; } = true;
    public bool AutoAcceptRankedFlex { get; set; } = true;
    public bool AutoAcceptNormalDraft { get; set; } = true;
    public bool AutoAcceptAramAndModes { get; set; } = true;

    // ==================== 2. AUTO CHAMP SELECT (PICK) SETTINGS ====================
    public bool AutoChampSelectEnabled { get; set; } = false;
    /// <summary>
    /// "SPECIFIC" = Pick Specific Champion
    /// "BEST_COUNTER" = Pick Best Statistical Counter vs Enemy Laner
    /// "MASTERY_COUNTER" = Pick Counter Based on My Champion Mastery Pool
    /// </summary>
    public string PickStrategy { get; set; } = "SPECIFIC";
    public string PreferredRole { get; set; } = "MIDDLE";
    public int PreferredPickChampId { get; set; } = 157; // Yasuo
    public int BackupPickChampId { get; set; } = 777;    // Yone
    public int SimulatedEnemyChampId { get; set; } = 238; // Zed (for preview & fallback when enemy hasn't picked yet)

    // Mastery Counter Settings
    public int MinMasteryLevelForCounter { get; set; } = 5;
    public int MasteryPoolTopCount { get; set; } = 8;
    public List<int> MasteryPoolChampIds { get; set; } = new() { 157, 777, 103, 84, 238, 91, 134, 61 };

    // Hover & Lock-in Behavior
    public bool AutoHoverPick { get; set; } = true;
    public bool AutoLockInPick { get; set; } = true;
    public int PickLockInDelayMs { get; set; } = 1200;

    // ==================== 3. AUTO BAN SETTINGS ====================
    public bool AutoBanEnabled { get; set; } = true;
    /// <summary>
    /// "SPECIFIC" = Ban Specific Champion
    /// "BIGGEST_COUNTER" = Ban Biggest Counter of the Champion I Want to Play
    /// "COUNTER_OF_COUNTER" = Ban Biggest Counter of the Counter Champion (2nd-Order Protection)
    /// </summary>
    public string BanStrategy { get; set; } = "BIGGEST_COUNTER";
    public int PreferredBanChampId { get; set; } = 53; // Blitzcrank
    public int BackupBanChampId { get; set; } = 122;   // Darius
    public bool ProtectTeammateHover { get; set; } = true;
    public int BanDelayMs { get; set; } = 900;

    // ==================== 4. AUTO LOADOUT & SMART QOL SETTINGS ====================
    public bool AutoExportRunesOnLock { get; set; } = true;
    public bool AutoExportSpellsOnLock { get; set; } = true;
    public string FlashKeySlot { get; set; } = "F"; // "D" or "F"
    public bool AutoExportItemsOnLock { get; set; } = true;
    public bool AutoSwitchToMatchupBuild { get; set; } = true;
    public bool AutoOpenBuildOnChampSelect { get; set; } = true;

    // ==================== 5. PICK & BAN SUGGESTIONS SETTINGS ====================
    public bool PickSuggestionPersonalizedMode { get; set; } = true;
    public int PickSuggestionMinMasteryLevel { get; set; } = 5; // Level > 4
    public int PickSuggestionMaxMasteryPicks { get; set; } = 5; // Max 5 mastery champs placed first
    public int PickSuggestionMinMatches { get; set; } = 10000;  // 10k matches threshold from blitz.gg
    public string DefaultSuggestionRole { get; set; } = "ALL";
    public bool AutoDetectLaneInSuggestions { get; set; } = true;
}

public class ComputedAutomationPreview
{
    public int PickChampionId { get; set; }
    public string PickChampionName { get; set; } = "Yasuo";
    public string PickChampionIconUrl { get; set; } = string.Empty;
    public string PickReasonLabel { get; set; } = "Specific Main";
    public double PickWinRate { get; set; } = 0.52;

    public int BackupPickChampionId { get; set; }
    public string BackupPickChampionName { get; set; } = "Yone";
    public string BackupPickChampionIconUrl { get; set; } = string.Empty;

    public int BanChampionId { get; set; }
    public string BanChampionName { get; set; } = "Vex";
    public string BanChampionIconUrl { get; set; } = string.Empty;
    public string BanReasonLabel { get; set; } = "#1 Counter to Yasuo";
    public double BanEnemyWinRate { get; set; } = 0.558;

    public int SecondaryBanChampionId { get; set; }
    public string SecondaryBanChampionName { get; set; } = "Pantheon";
    public string SecondaryBanChampionIconUrl { get; set; } = string.Empty;
}

public class AppSettingsService
{
    public static AppSettingsService Instance { get; } = new AppSettingsService();

    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Fired whenever any setting is changed from the Home Tab, Top Bar, or Settings View.
    /// </summary>
    public event Action? OnSettingsChanged;

    /// <summary>
    /// Fired when the background LCU automation performs an action (e.g. Auto-Accepted Ready Check, Banned, Picked).
    /// </summary>
    public event Action<string>? OnAutomationStatusChanged;

    /// <summary>
    /// Fired when a champion is locked in and AutoOpenBuildOnChampSelect is true.
    /// </summary>
    public event Action<int, string>? OnAutoOpenChampionBuild;

    public string LastAutomationStatus { get; private set; } = "Automation Ready";

    public bool IsInChampSelect { get; private set; } = false;
    public int CurrentChampSelectPrepickedId { get; private set; } = 0;
    public string CurrentChampSelectAssignedRole { get; private set; } = "MIDDLE";
    public event Action<int, string>? OnChampSelectStateUpdated;

    private readonly string _settingsFilePath;
    private readonly Random _rng = new();
    private CancellationTokenSource? _loopCts;
    private int _lastHandledReadyCheckTimestamp = 0;
    private int _lastBannedSessionActionId = -1;
    private int _lastPickedSessionActionId = -1;
    private int _lastHoveredSessionActionId = -1;
    private int _lastExportedLoadoutChampId = -1;

    private AppSettingsService()
    {
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
        Load();
        StartAutomationLoop();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                    if (Settings.MasteryPoolChampIds == null || Settings.MasteryPoolChampIds.Count == 0)
                        Settings.MasteryPoolChampIds = new List<int> { 157, 777, 103, 84, 238, 91, 134, 61 };
                }
            }
        }
        catch { }
    }

    public void SaveAndNotify()
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(Settings, opts));
        }
        catch { }

        OnSettingsChanged?.Invoke();
    }

    public void SetAutoAccept(bool enabled)
    {
        if (Settings.AutoAcceptEnabled == enabled) return;
        Settings.AutoAcceptEnabled = enabled;
        UpdateStatus(enabled
            ? $"⚡ Auto Accept ON ({Settings.AutoAcceptDelayMs}ms{(Settings.AutoAcceptOnlyPartyLeader ? ", Leader Only" : "")})"
            : "Auto Accept Paused");
        SaveAndNotify();
    }

    public void SetAutoChampSelect(bool enabled)
    {
        if (Settings.AutoChampSelectEnabled == enabled) return;
        Settings.AutoChampSelectEnabled = enabled;
        UpdateStatus(enabled
            ? $"🎯 Auto Champ Select ON ({FormatPickStrategyShort()})"
            : "Auto Champ Select Paused");
        SaveAndNotify();
    }

    public string FormatPickStrategyShort()
    {
        return Settings.PickStrategy switch
        {
            "BEST_COUNTER" => "Best Counter",
            "MASTERY_COUNTER" => $"Mastery Counter (M{Settings.MinMasteryLevelForCounter}+)",
            _ => "Specific Pick"
        };
    }

    public string FormatBanStrategyShort()
    {
        if (!Settings.AutoBanEnabled) return "Ban Off";
        return Settings.BanStrategy switch
        {
            "BIGGEST_COUNTER" => "Ban #1 Counter",
            "COUNTER_OF_COUNTER" => "Ban Counter's Counter",
            _ => "Ban Specific"
        };
    }

    private void UpdateStatus(string status)
    {
        LastAutomationStatus = status;
        OnAutomationStatusChanged?.Invoke(status);
    }

    // ==================== PICK & BAN RESOLUTION ENGINE ====================

    public async Task<ComputedAutomationPreview> ComputePreviewAsync(
        int? liveEnemyChampId = null,
        HashSet<int>? unavailableChampIds = null,
        HashSet<int>? teammateHoveredIds = null,
        int? prepickedChampId = null,
        string? activeRole = null)
    {
        await BuildDatabaseService.Instance.InitializeAsync();

        unavailableChampIds ??= new HashSet<int>();
        teammateHoveredIds ??= new HashSet<int>();

        string role = !string.IsNullOrEmpty(activeRole) && activeRole != "AUTO"
            ? activeRole
            : (string.IsNullOrEmpty(Settings.PreferredRole) || Settings.PreferredRole == "AUTO" ? "MIDDLE" : Settings.PreferredRole);

        int enemyId = liveEnemyChampId.HasValue && liveEnemyChampId.Value > 0
            ? liveEnemyChampId.Value
            : Settings.SimulatedEnemyChampId;

        var enemyInfo = BuildDatabaseService.Instance.GetChampionInfo(enemyId)
                        ?? BuildDatabaseService.Instance.AllChampions.FirstOrDefault()
                        ?? new ChampionInfo { Id = 238, Name = "Zed" };

        var preview = new ComputedAutomationPreview();

        // 1. Resolve Backup Pick
        var backupInfo = BuildDatabaseService.Instance.GetChampionInfo(Settings.BackupPickChampId)
                         ?? BuildDatabaseService.Instance.AllChampions.Skip(1).FirstOrDefault()
                         ?? new ChampionInfo { Id = 777, Name = "Yone" };
        preview.BackupPickChampionId = backupInfo.Id;
        preview.BackupPickChampionName = backupInfo.Name;
        preview.BackupPickChampionIconUrl = backupInfo.IconUrl;

        // 2. Resolve Primary Pick based on PickStrategy (Synced directly with SuggestionService)
        if (Settings.PickStrategy == "BEST_COUNTER")
        {
            var counterSuggestions = await SuggestionService.Instance.GetCounterPickSuggestionsAsync(
                enemyId > 0 ? enemyId : null, role, personalized: false);

            var bestCounter = counterSuggestions.FirstOrDefault(c => !unavailableChampIds.Contains(c.ChampionId))
                              ?? counterSuggestions.FirstOrDefault();

            if (bestCounter != null)
            {
                preview.PickChampionId = bestCounter.ChampionId;
                preview.PickChampionName = bestCounter.ChampionName;
                preview.PickChampionIconUrl = bestCounter.IconUrl;
                preview.PickWinRate = bestCounter.WinRate;
                preview.PickReasonLabel = $"Best Counter vs {enemyInfo.Name} ({bestCounter.WinRateFormatted} WR)";
            }
        }
        else if (Settings.PickStrategy == "MASTERY_COUNTER")
        {
            var counterSuggestions = await SuggestionService.Instance.GetCounterPickSuggestionsAsync(
                enemyId > 0 ? enemyId : null, role, personalized: true);

            var bestMasteryCounter = counterSuggestions.FirstOrDefault(c => !unavailableChampIds.Contains(c.ChampionId))
                                     ?? counterSuggestions.FirstOrDefault();

            if (bestMasteryCounter != null)
            {
                preview.PickChampionId = bestMasteryCounter.ChampionId;
                preview.PickChampionName = bestMasteryCounter.ChampionName;
                preview.PickChampionIconUrl = bestMasteryCounter.IconUrl;
                preview.PickWinRate = bestMasteryCounter.WinRate;
                preview.PickReasonLabel = bestMasteryCounter.IsPersonalizedMastery
                    ? $"Mastery Counter vs {enemyInfo.Name} ({bestMasteryCounter.BadgeText})"
                    : $"Best Counter vs {enemyInfo.Name} ({bestMasteryCounter.WinRateFormatted} WR)";
            }
            else
            {
                // Fallback to #1 Mastery Pool champion
                int topMasteryId = Settings.MasteryPoolChampIds.FirstOrDefault(id => !unavailableChampIds.Contains(id));
                if (topMasteryId <= 0) topMasteryId = Settings.PreferredPickChampId;
                var mInfo = BuildDatabaseService.Instance.GetChampionInfo(topMasteryId) ?? backupInfo;
                preview.PickChampionId = mInfo.Id;
                preview.PickChampionName = mInfo.Name;
                preview.PickChampionIconUrl = mInfo.IconUrl;
                preview.PickWinRate = 0.535;
                preview.PickReasonLabel = $"Top Mastery Comfort vs {enemyInfo.Name}";
            }
        }

        // Default or fallback to Specific Champion
        if (preview.PickChampionId <= 0)
        {
            int targetPickId = !unavailableChampIds.Contains(Settings.PreferredPickChampId)
                ? Settings.PreferredPickChampId
                : Settings.BackupPickChampId;
            var pInfo = BuildDatabaseService.Instance.GetChampionInfo(targetPickId)
                        ?? BuildDatabaseService.Instance.AllChampions.FirstOrDefault()
                        ?? new ChampionInfo { Id = 157, Name = "Yasuo" };
            preview.PickChampionId = pInfo.Id;
            preview.PickChampionName = pInfo.Name;
            preview.PickChampionIconUrl = pInfo.IconUrl;
            preview.PickWinRate = 0.524;
            preview.PickReasonLabel = targetPickId == Settings.PreferredPickChampId
                ? $"Specific Main ({role})"
                : $"Backup Pick (Main Unavailable)";
        }

        int intendedPickId = prepickedChampId.HasValue && prepickedChampId.Value > 0
            ? prepickedChampId.Value
            : preview.PickChampionId;

        var intendedChampInfo = BuildDatabaseService.Instance.GetChampionInfo(intendedPickId)
                                ?? BuildDatabaseService.Instance.GetChampionInfo(preview.PickChampionId);
        string intendedName = intendedChampInfo?.Name ?? preview.PickChampionName;

        // 3. Resolve Ban based on BanStrategy (Synced directly with SuggestionService)
        if (Settings.BanStrategy == "BIGGEST_COUNTER")
        {
            // Ban the #1 statistical counter to the champion we want to play (or prepicked) using SuggestionService!
            var banSuggestions = await SuggestionService.Instance.GetBanSuggestionsAsync(intendedPickId, role);
            var validBans = banSuggestions
                .Where(c => !unavailableChampIds.Contains(c.ChampionId) &&
                            (!Settings.ProtectTeammateHover || !teammateHoveredIds.Contains(c.ChampionId)) &&
                            c.ChampionId != intendedPickId)
                .ToList();

            var topBan = validBans.FirstOrDefault() ?? banSuggestions.FirstOrDefault();
            var secondBan = validBans.Skip(1).FirstOrDefault() ?? banSuggestions.Skip(1).FirstOrDefault();

            if (topBan != null)
            {
                preview.BanChampionId = topBan.ChampionId;
                preview.BanChampionName = topBan.ChampionName;
                preview.BanChampionIconUrl = topBan.IconUrl;
                preview.BanEnemyWinRate = topBan.EnemyWinRateAgainstYou;
                preview.BanReasonLabel = $"#1 Counter to {intendedName} ({topBan.EnemyWinRateFormatted} Enemy WR)";
            }
            if (secondBan != null)
            {
                preview.SecondaryBanChampionId = secondBan.ChampionId;
                preview.SecondaryBanChampionName = secondBan.ChampionName;
                preview.SecondaryBanChampionIconUrl = secondBan.IconUrl;
            }
        }
        else if (Settings.BanStrategy == "COUNTER_OF_COUNTER")
        {
            // Step A: Find the #1 counter to our intended champion using SuggestionService
            var banSuggestions = await SuggestionService.Instance.GetBanSuggestionsAsync(intendedPickId, role);
            var biggestThreat = banSuggestions.FirstOrDefault();
            int threatChampId = biggestThreat?.ChampionId ?? enemyId;
            string threatChampName = biggestThreat?.ChampionName ?? enemyInfo.Name;

            // Step B: Find the biggest counter to that counter using SuggestionService!
            var secondOrderSuggestions = await SuggestionService.Instance.GetCounterPickSuggestionsAsync(threatChampId, role, personalized: false);
            var validBans = secondOrderSuggestions
                .Where(c => !unavailableChampIds.Contains(c.ChampionId) &&
                            (!Settings.ProtectTeammateHover || !teammateHoveredIds.Contains(c.ChampionId)) &&
                            c.ChampionId != intendedPickId)
                .ToList();

            var topBan = validBans.FirstOrDefault() ?? secondOrderSuggestions.FirstOrDefault();
            var secondBan = validBans.Skip(1).FirstOrDefault() ?? secondOrderSuggestions.Skip(1).FirstOrDefault();

            if (topBan != null)
            {
                preview.BanChampionId = topBan.ChampionId;
                preview.BanChampionName = topBan.ChampionName;
                preview.BanChampionIconUrl = topBan.IconUrl;
                preview.BanEnemyWinRate = 1.0 - topBan.WinRate;
                preview.BanReasonLabel = $"Counter to {threatChampName} (Protects {intendedName})";
            }
            if (secondBan != null)
            {
                preview.SecondaryBanChampionId = secondBan.ChampionId;
                preview.SecondaryBanChampionName = secondBan.ChampionName;
                preview.SecondaryBanChampionIconUrl = secondBan.IconUrl;
            }
        }

        // Default or fallback to Specific Ban
        if (preview.BanChampionId <= 0)
        {
            bool primaryHoveredByAlly = Settings.ProtectTeammateHover && teammateHoveredIds.Contains(Settings.PreferredBanChampId);
            int targetBanId = (!unavailableChampIds.Contains(Settings.PreferredBanChampId) && !primaryHoveredByAlly)
                ? Settings.PreferredBanChampId
                : Settings.BackupBanChampId;

            var bInfo = BuildDatabaseService.Instance.GetChampionInfo(targetBanId)
                        ?? BuildDatabaseService.Instance.AllChampions.FirstOrDefault()
                        ?? new ChampionInfo { Id = 53, Name = "Blitzcrank" };
            var bBackup = BuildDatabaseService.Instance.GetChampionInfo(Settings.BackupBanChampId)
                          ?? BuildDatabaseService.Instance.AllChampions.Skip(2).FirstOrDefault()
                          ?? new ChampionInfo { Id = 122, Name = "Darius" };

            preview.BanChampionId = bInfo.Id;
            preview.BanChampionName = bInfo.Name;
            preview.BanChampionIconUrl = bInfo.IconUrl;
            preview.BanEnemyWinRate = 0.532;
            preview.BanReasonLabel = primaryHoveredByAlly ? "Backup Ban (Ally Hovered Primary)" : "Specific Target Ban";

            preview.SecondaryBanChampionId = bBackup.Id;
            preview.SecondaryBanChampionName = bBackup.Name;
            preview.SecondaryBanChampionIconUrl = bBackup.IconUrl;
        }

        return preview;
    }

    // ==================== SYNC MASTERY POOL FROM LCU ====================

    public async Task<bool> SyncMasteryPoolFromLcuAsync()
    {
        if (!LcuApiClient.Instance.IsConnected) return false;
        try
        {
            string json = await LcuApiClient.Instance.GetTopChampionMasteryAsync();
            if (string.IsNullOrEmpty(json)) return false;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arr = root.ValueKind == JsonValueKind.Array
                ? root
                : (root.TryGetProperty("masteries", out var mProp) && mProp.ValueKind == JsonValueKind.Array ? mProp : default);

            if (arr.ValueKind != JsonValueKind.Array) return false;

            var ids = new List<int>();
            foreach (var item in arr.EnumerateArray())
            {
                int cId = item.TryGetProperty("championId", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : 0;
                int lvl = item.TryGetProperty("championLevel", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetInt32() : 1;
                int pts = item.TryGetProperty("championPoints", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetInt32() : 0;
                if (cId > 0 && lvl >= Settings.MinMasteryLevelForCounter)
                {
                    ids.Add(cId);
                }
            }

            if (ids.Count > 0)
            {
                Settings.MasteryPoolChampIds = ids.Take(Math.Max(5, Settings.MasteryPoolTopCount)).ToList();
                SaveAndNotify();
                return true;
            }
        }
        catch { }
        return false;
    }

    // ==================== LIVE LCU AUTOMATION ENGINE ====================

    private void StartAutomationLoop()
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (LcuApiClient.Instance.IsConnected)
                    {
                        if (Settings.AutoAcceptEnabled)
                        {
                            await CheckAndHandleReadyCheckAsync(token);
                        }

                        if (Settings.AutoChampSelectEnabled || Settings.AutoBanEnabled || Settings.AutoExportRunesOnLock || Settings.AutoExportSpellsOnLock || Settings.AutoExportItemsOnLock || Settings.AutoOpenBuildOnChampSelect)
                        {
                            await CheckAndHandleChampSelectAsync(token);
                        }
                    }
                }
                catch { }

                await Task.Delay(650, token);
            }
        }, token);
    }

    private async Task CheckAndHandleReadyCheckAsync(CancellationToken token)
    {
        string rcJson = await LcuApiClient.Instance.GetReadyCheckAsync();
        if (string.IsNullOrEmpty(rcJson)) return;

        using var doc = JsonDocument.Parse(rcJson);
        var root = doc.RootElement;
        string state = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "" : "";
        string playerResp = root.TryGetProperty("playerResponse", out var prp) ? prp.GetString() ?? "None" : "None";
        double timer = root.TryGetProperty("timer", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetDouble() : 0;

        if (!string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(playerResp, "Declined", StringComparison.OrdinalIgnoreCase))
        {
            if (Settings.AutoAcceptPauseAfterDecline && Settings.AutoAcceptEnabled)
            {
                Settings.AutoAcceptEnabled = false;
                UpdateStatus("⏸️ Auto Accept Paused (Manual Decline Detected)");
                SaveAndNotify();
            }
            return;
        }

        if (string.Equals(playerResp, "Accepted", StringComparison.OrdinalIgnoreCase))
            return;

        int bucket = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 8);
        if (_lastHandledReadyCheckTimestamp == bucket)
            return;

        // Check Lobby Party Leader & Queue Filters if configured
        string lobbyJson = await LcuApiClient.Instance.GetLobbyAsync();
        if (!string.IsNullOrEmpty(lobbyJson))
        {
            try
            {
                using var lobbyDoc = JsonDocument.Parse(lobbyJson);
                var lRoot = lobbyDoc.RootElement;

                if (Settings.AutoAcceptOnlyPartyLeader)
                {
                    bool isLeader = true;
                    if (lRoot.TryGetProperty("localMember", out var lm) && lm.TryGetProperty("isLeader", out var il) && il.ValueKind == JsonValueKind.False)
                        isLeader = false;

                    if (!isLeader)
                    {
                        UpdateStatus("👑 Skipped Auto Accept (Not Party Leader)");
                        return;
                    }
                }

                if (lRoot.TryGetProperty("gameConfig", out var gc) && gc.TryGetProperty("queueId", out var qip) && qip.ValueKind == JsonValueKind.Number)
                {
                    int qId = qip.GetInt32();
                    if (qId == 420 && !Settings.AutoAcceptRankedSolo) return;
                    if (qId == 440 && !Settings.AutoAcceptRankedFlex) return;
                    if ((qId is 400 or 430 or 490) && !Settings.AutoAcceptNormalDraft) return;
                    if ((qId is 450 or 1700 or 1900) && !Settings.AutoAcceptAramAndModes) return;
                }
            }
            catch { }
        }

        _lastHandledReadyCheckTimestamp = bucket;

        int jitter = Settings.AutoAcceptRandomJitterMs > 0 ? _rng.Next(0, Settings.AutoAcceptRandomJitterMs + 1) : 0;
        int totalDelay = Math.Clamp(Settings.AutoAcceptDelayMs + jitter, 0, 9000);

        if (totalDelay > 0)
        {
            UpdateStatus($"⏳ Ready Check Detected — Accepting in {totalDelay} ms...");
            await Task.Delay(totalDelay, token);
        }

        bool accepted = await LcuApiClient.Instance.AcceptReadyCheckAsync();
        if (accepted)
        {
            UpdateStatus($"✅ Match Auto-Accepted ({totalDelay} ms latency)");
        }
    }

    private async Task CheckAndHandleChampSelectAsync(CancellationToken token)
    {
        string sessionJson = await LcuApiClient.Instance.GetChampSelectSessionAsync();
        if (string.IsNullOrEmpty(sessionJson))
        {
            if (IsInChampSelect)
            {
                IsInChampSelect = false;
                CurrentChampSelectPrepickedId = 0;
                _lastBannedSessionActionId = -1;
                _lastPickedSessionActionId = -1;
                _lastHoveredSessionActionId = -1;
                _lastExportedLoadoutChampId = -1;
                OnChampSelectStateUpdated?.Invoke(0, Settings.PreferredRole);
            }
            return;
        }

        IsInChampSelect = true;

        using var doc = JsonDocument.Parse(sessionJson);
        var root = doc.RootElement;

        int localCellId = root.TryGetProperty("localPlayerCellId", out var lcp) && lcp.ValueKind == JsonValueKind.Number ? lcp.GetInt32() : -1;
        if (localCellId < 0) return;

        var unavailableIds = new HashSet<int>();
        var teammateHoveredIds = new HashSet<int>();
        string assignedRole = Settings.PreferredRole;
        int myLockedChampId = 0;
        int myPrepickedChampId = 0;

        // Parse myTeam
        if (root.TryGetProperty("myTeam", out var myTeamArr) && myTeamArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in myTeamArr.EnumerateArray())
            {
                int cellId = member.TryGetProperty("cellId", out var cp) ? cp.GetInt32() : -1;
                int cId = member.TryGetProperty("championId", out var chp) ? chp.GetInt32() : 0;
                int intentId = member.TryGetProperty("championPickIntent", out var ip) ? ip.GetInt32() : 0;
                string pos = member.TryGetProperty("assignedPosition", out var posP) ? posP.GetString() ?? "" : "";

                if (cId > 0) unavailableIds.Add(cId);

                if (cellId == localCellId)
                {
                    myLockedChampId = cId;
                    if (intentId > 0) myPrepickedChampId = intentId;
                    else if (cId > 0) myPrepickedChampId = cId;

                    if (!string.IsNullOrWhiteSpace(pos))
                        assignedRole = pos.ToUpperInvariant();
                }
                else
                {
                    if (cId > 0) teammateHoveredIds.Add(cId);
                    if (intentId > 0) teammateHoveredIds.Add(intentId);
                }
            }
        }

        // Notify if champ select prepick or role updated
        if (myPrepickedChampId != CurrentChampSelectPrepickedId || assignedRole != CurrentChampSelectAssignedRole)
        {
            CurrentChampSelectPrepickedId = myPrepickedChampId;
            CurrentChampSelectAssignedRole = assignedRole;
            OnChampSelectStateUpdated?.Invoke(myPrepickedChampId, assignedRole);
        }

        // Parse theirTeam (to detect enemy laner for counter-pick!)
        int? detectedEnemyLanerId = null;
        if (root.TryGetProperty("theirTeam", out var theirTeamArr) && theirTeamArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var enemy in theirTeamArr.EnumerateArray())
            {
                int cId = enemy.TryGetProperty("championId", out var chp) ? chp.GetInt32() : 0;
                string ePos = enemy.TryGetProperty("assignedPosition", out var posP) ? posP.GetString() ?? "" : "";
                if (cId > 0)
                {
                    unavailableIds.Add(cId);
                    if (!string.IsNullOrEmpty(ePos) && string.Equals(ePos, assignedRole, StringComparison.OrdinalIgnoreCase))
                        detectedEnemyLanerId = cId;
                    else if (!detectedEnemyLanerId.HasValue)
                        detectedEnemyLanerId = cId;
                }
            }
        }

        // Parse bans
        if (root.TryGetProperty("bans", out var bansObj) && bansObj.ValueKind == JsonValueKind.Object)
        {
            if (bansObj.TryGetProperty("myTeamBans", out var mb) && mb.ValueKind == JsonValueKind.Array)
                foreach (var b in mb.EnumerateArray())
                    if (b.ValueKind == JsonValueKind.Number && b.GetInt32() > 0) unavailableIds.Add(b.GetInt32());

            if (bansObj.TryGetProperty("theirTeamBans", out var tb) && tb.ValueKind == JsonValueKind.Array)
                foreach (var b in tb.EnumerateArray())
                    if (b.ValueKind == JsonValueKind.Number && b.GetInt32() > 0) unavailableIds.Add(b.GetInt32());
        }

        var preview = await ComputePreviewAsync(detectedEnemyLanerId, unavailableIds, teammateHoveredIds, myPrepickedChampId, assignedRole);

        // Inspect actions array
        if (root.TryGetProperty("actions", out var actionsOuter) && actionsOuter.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionGroup in actionsOuter.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array) continue;
                foreach (var act in actionGroup.EnumerateArray())
                {
                    int actorCell = act.TryGetProperty("actorCellId", out var acp) ? acp.GetInt32() : -1;
                    if (actorCell != localCellId) continue;

                    int actId = act.TryGetProperty("id", out var idp) ? idp.GetInt32() : -1;
                    string type = act.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
                    bool inProgress = act.TryGetProperty("isInProgress", out var ipp) && ipp.ValueKind == JsonValueKind.True;
                    bool completed = act.TryGetProperty("completed", out var cmp) && cmp.ValueKind == JsonValueKind.True;

                    if (completed || actId < 0) continue;

                    // 1. Handle Ban Action
                    if (string.Equals(type, "ban", StringComparison.OrdinalIgnoreCase) && inProgress && Settings.AutoBanEnabled)
                    {
                        if (_lastBannedSessionActionId != actId && preview.BanChampionId > 0)
                        {
                            // 1. Hover the ban first
                            await LcuApiClient.Instance.PatchChampSelectActionAsync(actId, preview.BanChampionId, false);

                            // 2. Wait configured ban latency
                            int delay = Math.Clamp(Settings.BanDelayMs, 150, 6000);
                            await Task.Delay(delay, token);

                            // 3. Lock in the ban
                            bool ok = await LcuApiClient.Instance.PatchChampSelectActionAsync(actId, preview.BanChampionId, true);
                            if (ok)
                            {
                                _lastBannedSessionActionId = actId;
                                UpdateStatus($"🚫 Auto-Banned {preview.BanChampionName} ({preview.BanReasonLabel})");
                            }
                        }
                    }

                    // 2. Handle Pick Hover / Lock-in Action
                    if (string.Equals(type, "pick", StringComparison.OrdinalIgnoreCase) && Settings.AutoChampSelectEnabled)
                    {
                        if (!inProgress && Settings.AutoHoverPick && _lastHoveredSessionActionId != actId && preview.PickChampionId > 0)
                        {
                            _lastHoveredSessionActionId = actId;
                            await LcuApiClient.Instance.PatchChampSelectActionAsync(actId, preview.PickChampionId, false);
                            UpdateStatus($"👋 Auto-Hovered {preview.PickChampionName}");
                        }
                        else if (inProgress && _lastPickedSessionActionId != actId && preview.PickChampionId > 0)
                        {
                            _lastPickedSessionActionId = actId;
                            // Always hover first
                            await LcuApiClient.Instance.PatchChampSelectActionAsync(actId, preview.PickChampionId, false);

                            if (Settings.AutoLockInPick)
                            {
                                int pickDelay = Math.Clamp(Settings.PickLockInDelayMs, 150, 8000);
                                await Task.Delay(pickDelay, token);

                                bool ok = await LcuApiClient.Instance.PatchChampSelectActionAsync(actId, preview.PickChampionId, true);
                                if (ok)
                                    UpdateStatus($"🎯 Auto-Locked {preview.PickChampionName} ({preview.PickReasonLabel})");
                            }
                        }
                    }
                }
            }
        }

        // 3. Auto Loadout Export when champion is locked in
        if (myLockedChampId > 0 && _lastExportedLoadoutChampId != myLockedChampId)
        {
            _lastExportedLoadoutChampId = myLockedChampId;

            if (Settings.AutoOpenBuildOnChampSelect)
            {
                OnAutoOpenChampionBuild?.Invoke(myLockedChampId, assignedRole);
            }

            if (Settings.AutoExportRunesOnLock || Settings.AutoExportSpellsOnLock || Settings.AutoExportItemsOnLock)
            {
                var fullData = await BuildDatabaseService.Instance.GetChampionFullDataAsync(
                    myLockedChampId,
                    "EMERALD%2B",
                    "WORLD",
                    Settings.AutoSwitchToMatchupBuild ? detectedEnemyLanerId : null);

                var builds = fullData.RoleBuilds.TryGetValue(assignedRole, out var rb) && rb.Count > 0
                    ? rb
                    : fullData.RoleBuilds.Values.FirstOrDefault();

                var bestPreset = builds?.FirstOrDefault(b => Settings.AutoSwitchToMatchupBuild && b.IsMatchup)
                                 ?? builds?.FirstOrDefault();

                if (bestPreset != null)
                {
                    if (Settings.AutoExportRunesOnLock)
                    {
                        await LcuApiClient.Instance.SetRunePageAsync(
                            bestPreset.RunePreset.Name,
                            bestPreset.RunePreset.PrimaryStyleId,
                            bestPreset.RunePreset.SubStyleId,
                            bestPreset.RunePreset.SelectedPerkIds);
                    }

                    if (Settings.AutoExportSpellsOnLock)
                    {
                        int s1 = bestPreset.Spell1Id;
                        int s2 = bestPreset.Spell2Id;
                        // Respect D vs F Flash preference!
                        if (Settings.FlashKeySlot == "D" && s2 == 4)
                            (s1, s2) = (s2, s1);
                        else if (Settings.FlashKeySlot == "F" && s1 == 4)
                            (s1, s2) = (s2, s1);

                        await LcuApiClient.Instance.SetSummonerSpellsAsync(s1, s2);
                    }

                    if (Settings.AutoExportItemsOnLock)
                    {
                        await LcuApiClient.Instance.SetItemBuildAsync(bestPreset);
                    }

                    UpdateStatus($"🔮 Auto-Exported Loadout for {bestPreset.ChampionName}");
                }
            }
        }
    }
}
