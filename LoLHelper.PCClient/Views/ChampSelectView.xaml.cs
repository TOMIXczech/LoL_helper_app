using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public partial class ChampSelectView : UserControl
{
    private readonly DispatcherTimer _pollTimer;
    private bool _isUpdating = false;

    // Champion & Filter State
    private int _currentChampId = -1;
    private int _lastDetectedLcuChampId = -1;
    private string _currentRole = "TOP";
    private string _currentTierKey = "EMERALD%2B";
    private string _currentTierLabel = "Emerald+";
    private int? _currentMatchupChampId = null;
    private string _currentSortMode = "WINRATE"; // WINRATE, COMMON, OTP

    private ChampionFullData? _currentChampData;
    private ChampionBuildPreset? _activePreset;
    private int _activeRuneOptionIndex = 0;

    // Local Interactive Rune & Spell State
    private string _activePageName = "Blitz Page";
    private int _primaryTreeId = 8000;
    private int _secondaryTreeId = 8400;
    private List<int> _selectedPerks = new();
    private int _currentSpell1Id = 4;
    private int _currentSpell2Id = 14;
    private readonly ContextMenu _spell1Menu = new();
    private readonly ContextMenu _spell2Menu = new();

    // Overlays State
    private Action<int?>? _onItemPickedCallback;
    private bool _itemPickerInitialized = false;
    private bool _champPickerInitialized = false;
    private bool _isPickingMatchup = false;

    public class SummonerSpellModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
    }

    private readonly List<SummonerSpellModel> _summonerSpells = new()
    {
        new SummonerSpellModel { Id = 1, Name = "Cleanse", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerBoost.png" },
        new SummonerSpellModel { Id = 3, Name = "Exhaust", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerExhaust.png" },
        new SummonerSpellModel { Id = 4, Name = "Flash", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerFlash.png" },
        new SummonerSpellModel { Id = 6, Name = "Ghost", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerHaste.png" },
        new SummonerSpellModel { Id = 7, Name = "Heal", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerHeal.png" },
        new SummonerSpellModel { Id = 11, Name = "Smite", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerSmite.png" },
        new SummonerSpellModel { Id = 12, Name = "Teleport", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerTeleport.png" },
        new SummonerSpellModel { Id = 14, Name = "Ignite", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerDot.png" },
        new SummonerSpellModel { Id = 21, Name = "Barrier", IconUrl = "https://ddragon.leagueoflegends.com/cdn/14.1.1/img/spell/SummonerBarrier.png" }
    };

    public event Action? OnOpenBanSuggestionsRequested;

    public ChampSelectView()
    {
        InitializeComponent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (s, e) => await PollLcuChampSelectAsync();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        InitFilterMenus();
        InitSpellMenus();

        AppSettingsService.Instance.OnChampSelectStateUpdated += (id, r) => Dispatcher.Invoke(UpdateLiveAutomationBanner);
        AppSettingsService.Instance.OnChampSelectPhaseChanged += p => Dispatcher.Invoke(UpdateLiveAutomationBanner);
        AppSettingsService.Instance.OnAutoBanTargetUpdated += (id, name, reason) => Dispatcher.Invoke(UpdateLiveAutomationBanner);
        AppSettingsService.Instance.OnAutomationStatusChanged += s => Dispatcher.Invoke(UpdateLiveAutomationBanner);
        AppSettingsService.Instance.OnSettingsChanged += () => Dispatcher.Invoke(UpdateLiveAutomationBanner);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await BuildDatabaseService.Instance.InitializeAsync();

            // Load Aatrox (266) initially if no champion is loaded yet
            if (_currentChampId <= 0)
            {
                await LoadChampionDataAsync(266, null);
            }

            _pollTimer.Start();
            await PollLcuChampSelectAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to initialize Blitz data: " + ex.Message);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
    }

    private void ViewAllBanSuggestionsBtn_Click(object sender, RoutedEventArgs e)
    {
        OnOpenBanSuggestionsRequested?.Invoke();
    }

    private void UpdateLiveAutomationBanner()
    {
        var appSvc = AppSettingsService.Instance;
        var s = appSvc.Settings;

        int banId = appSvc.CurrentAutoBanTargetChampId > 0 
            ? appSvc.CurrentAutoBanTargetChampId 
            : s.PreferredBanChampId;

        var champInfo = BuildDatabaseService.Instance.GetChampionInfo(banId);
        string champName = !string.IsNullOrEmpty(appSvc.CurrentAutoBanTargetChampName)
            ? appSvc.CurrentAutoBanTargetChampName
            : (champInfo?.Name ?? "Blitzcrank");

        string banReason = !string.IsNullOrEmpty(appSvc.CurrentAutoBanReason)
            ? appSvc.CurrentAutoBanReason
            : (s.BanStrategy == "BIGGEST_COUNTER" ? "#1 Counter to Prepick" : "Specific Target Ban");

        LiveBanTargetNameText.Text = champName;
        LiveBanReasonText.Text = banReason;

        if (champInfo != null && !string.IsNullOrEmpty(champInfo.IconUrl))
        {
            SetImageSourceSafe(LiveBanTargetImg, champInfo.IconUrl);
        }

        if (appSvc.IsBanCompletedThisSession)
        {
            LiveAutoBanStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x2C, 0xE5, 0xB5));
            LiveAutoBanStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));
            LiveAutoBanStatusIcon.Text = "✅";
            LiveAutoBanStatusText.Text = $"AUTO-BANNED {champName.ToUpperInvariant()}";
            LiveAutoBanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));
        }
        else if (string.Equals(appSvc.CurrentChampSelectPhase, "BAN", StringComparison.OrdinalIgnoreCase))
        {
            if (s.AutoBanEnabled)
            {
                LiveAutoBanStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x4B, 0x60));
                LiveAutoBanStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x60));
                LiveAutoBanStatusIcon.Text = "🚫";
                LiveAutoBanStatusText.Text = $"BANNING {champName.ToUpperInvariant()} ({s.BanDelayMs} MS)";
                LiveAutoBanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x7D));
            }
            else
            {
                LiveAutoBanStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF0, 0xB2, 0x32));
                LiveAutoBanStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xB2, 0x32));
                LiveAutoBanStatusIcon.Text = "🛡️";
                LiveAutoBanStatusText.Text = "RECOMMENDED BAN (AUTO-BAN OFF)";
                LiveAutoBanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xB2, 0x32));
            }
        }
        else if (appSvc.IsInChampSelect)
        {
            LiveAutoBanStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x53, 0x83, 0xE8));
            LiveAutoBanStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x53, 0x83, 0xE8));
            LiveAutoBanStatusIcon.Text = "🎯";
            string phaseName = string.IsNullOrEmpty(appSvc.CurrentChampSelectPhase) ? "ACTIVE" : appSvc.CurrentChampSelectPhase;
            LiveAutoBanStatusText.Text = $"CHAMP SELECT: {phaseName}";
            LiveAutoBanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x53, 0x83, 0xE8));
        }
        else
        {
            LiveAutoBanStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x8E, 0x92, 0xA4));
            LiveAutoBanStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x42, 0x56));
            LiveAutoBanStatusIcon.Text = s.AutoBanEnabled ? "⚙️" : "⏸️";
            LiveAutoBanStatusText.Text = s.AutoBanEnabled ? $"AUTO-BAN ARMED ({s.BanDelayMs} MS)" : "AUTO-BAN DISABLED";
            LiveAutoBanStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));
        }
    }

    // ==================== TOP BAR MENUS & HANDLERS ====================

    private void InitFilterMenus()
    {
        var tiers = new (string Label, string Key)[]
        {
            ("Challenger", "CHALLENGER"),
            ("Grandmaster", "GRANDMASTER"),
            ("Master+", "MASTER%2B"),
            ("Diamond+", "DIAMOND%2B"),
            ("Emerald+", "EMERALD%2B"),
            ("Platinum+", "PLATINUM%2B"),
            ("Gold", "GOLD"),
            ("Silver", "SILVER"),
            ("Bronze", "BRONZE"),
            ("Iron", "IRON")
        };

        foreach (var t in tiers)
        {
            string iconUrl = GetRankIconUrl(t.Label);
            var iconImg = new Image
            {
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(iconImg, iconUrl);

            var mi = new MenuItem
            {
                Header = t.Label,
                Icon = iconImg
            };
            mi.Click += async (s, e) =>
            {
                _currentTierLabel = t.Label;
                _currentTierKey = t.Key;
                SelectedTierText.Text = t.Label;
                SetImageSourceSafe(SelectedTierImg, iconUrl);
                SetImageSourceSafe(StandardBuildsTierImg, iconUrl);
                StandardBuildsHeaderText.Text = $"{t.Label} Builds";
                StandardBuildsHeaderText.Foreground = BrushFromHex(GetRankColorHex(t.Label));
                if (_currentChampId > 0)
                    await LoadChampionDataAsync(_currentChampId, _currentRole, preferMatchup: false, preserveSelection: true);
            };
            TierContextMenu.Items.Add(mi);
        }
    }

    private void TierSelectBtn_Click(object sender, MouseButtonEventArgs e)
    {
        TierContextMenu.PlacementTarget = TierSelectBtn;
        TierContextMenu.Placement = PlacementMode.Bottom;
        TierContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ChampSelectBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _isPickingMatchup = false;
        ChampPickerTitleText.Text = "SELECT CHAMPION";
        ChampPickerOverlay.Visibility = Visibility.Visible;
        ChampSearchBox.Text = "";
        PopulateChampPicker("");
        ChampSearchBox.Focus();
    }

    private void MatchupSelectBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _isPickingMatchup = true;
        ChampPickerTitleText.Text = "SELECT ENEMY MATCHUP";
        ChampPickerOverlay.Visibility = Visibility.Visible;
        ChampSearchBox.Text = "";
        PopulateChampPicker("");
        ChampSearchBox.Focus();
    }

    private void CloseChampPicker_Click(object sender, RoutedEventArgs e)
    {
        ChampPickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void ChampSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateChampPicker(ChampSearchBox.Text);
    }

    private void PopulateChampPicker(string filter)
    {
        if (!_champPickerInitialized)
        {
            AllChampsWrapPanel.Children.Clear();

            // Reset / Clear Matchup button
            var clearBorder = new Border
            {
                Width = 96, Height = 74,
                Background = BrushFromHex("#222532"),
                BorderBrush = BrushFromHex("#35394B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                Tag = "CLEAR_MATCHUP"
            };
            var clearStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            clearStack.Children.Add(new TextBlock { Text = "✕", Foreground = BrushFromHex("#FF7A7A"), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            clearStack.Children.Add(new TextBlock { Text = "All Matchups", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            clearBorder.Child = clearStack;
            clearBorder.MouseLeftButtonDown += async (s, e) =>
            {
                ChampPickerOverlay.Visibility = Visibility.Collapsed;
                if (_isPickingMatchup && _currentMatchupChampId != null)
                {
                    _currentMatchupChampId = null;
                    SelectedMatchupText.Text = "Select Matchup";
                    SelectedMatchupImg.Visibility = Visibility.Collapsed;
                    await LoadChampionDataAsync(_currentChampId, _currentRole);
                }
            };
            AllChampsWrapPanel.Children.Add(clearBorder);

            foreach (var champ in BuildDatabaseService.Instance.AllChampions)
            {
                var card = new Border
                {
                    Width = 96, Height = 74,
                    Background = BrushFromHex("#1D202B"),
                    BorderBrush = BrushFromHex("#2C3040"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(4),
                    Padding = new Thickness(4),
                    Cursor = Cursors.Hand,
                    Tag = champ
                };

                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var img = new Image { Width = 38, Height = 38, Margin = new Thickness(0, 0, 0, 4) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                SetImageSourceSafe(img, champ.IconUrl);

                var lbl = new TextBlock
                {
                    Text = champ.Name,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                sp.Children.Add(img);
                sp.Children.Add(lbl);
                card.Child = sp;

                card.MouseEnter += (s, e) => card.Background = BrushFromHex("#282C3C");
                card.MouseLeave += (s, e) => card.Background = BrushFromHex("#1D202B");

                int capturedId = champ.Id;
                var capturedChamp = champ;
                card.MouseLeftButtonDown += async (s, e) =>
                {
                    ChampPickerOverlay.Visibility = Visibility.Collapsed;
                    if (_isPickingMatchup)
                    {
                        if (_currentMatchupChampId != capturedId)
                        {
                            _currentMatchupChampId = capturedId;
                            SelectedMatchupText.Text = $"vs {capturedChamp.Name}";
                            SetImageSourceSafe(SelectedMatchupImg, capturedChamp.IconUrl);
                            SelectedMatchupImg.Visibility = Visibility.Visible;
                            await LoadChampionDataAsync(_currentChampId, _currentRole, preferMatchup: true, preserveSelection: false);
                        }
                    }
                    else
                    {
                        // Only update when selecting a new or different champion
                        if (capturedId != _currentChampId)
                        {
                            _currentMatchupChampId = null;
                            SelectedMatchupText.Text = "Select Matchup";
                            SelectedMatchupImg.Visibility = Visibility.Collapsed;
                            await LoadChampionDataAsync(capturedId, null);
                        }
                    }
                };

                AllChampsWrapPanel.Children.Add(card);
            }

            _champPickerInitialized = true;
        }

        string q = filter.Trim().ToLowerInvariant();
        foreach (UIElement el in AllChampsWrapPanel.Children)
        {
            if (el is Border b)
            {
                if (b.Tag is string tagStr && tagStr == "CLEAR_MATCHUP")
                {
                    b.Visibility = _isPickingMatchup ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (b.Tag is ChampionInfo cInfo)
                {
                    bool match = string.IsNullOrEmpty(q) || cInfo.Name.ToLowerInvariant().Contains(q);
                    if (_isPickingMatchup && cInfo.Id == _currentChampId)
                        match = false;
                    b.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    // ==================== LCU POLLING (ONLY UPDATES ON NEW/DIFFERENT CHAMPION) ====================

    private async Task PollLcuChampSelectAsync()
    {
        if (_isUpdating || !LcuApiClient.Instance.IsConnected) return;

        _isUpdating = true;
        try
        {
            string sessionJson = await LcuApiClient.Instance.GetChampSelectSessionAsync();
            if (!string.IsNullOrEmpty(sessionJson))
            {
                using var sDoc = JsonDocument.Parse(sessionJson);
                var sRoot = sDoc.RootElement;
                if (sRoot.TryGetProperty("localPlayerCellId", out var cellIdProp))
                {
                    int myCell = cellIdProp.GetInt32();
                    if (sRoot.TryGetProperty("myTeam", out var myTeamProp) && myTeamProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in myTeamProp.EnumerateArray())
                        {
                            if (p.TryGetProperty("cellId", out var pCell) && pCell.GetInt32() == myCell)
                            {
                                int champId = p.TryGetProperty("championId", out var cId) ? cId.GetInt32() : 0;
                                if (champId == 0)
                                    champId = p.TryGetProperty("championPickIntent", out var intentId) ? intentId.GetInt32() : 0;

                                string assignedRole = p.TryGetProperty("assignedPosition", out var pos) ? pos.GetString() ?? "" : "";
                                string mappedRole = MapLcuRole(assignedRole);

                                // Strictly update ONLY when the user selects/hovers a new or different champion
                                if (champId > 0 && champId != _lastDetectedLcuChampId)
                                {
                                    _lastDetectedLcuChampId = champId;
                                    _currentMatchupChampId = null;
                                    SelectedMatchupText.Text = "Select Matchup";
                                    SelectedMatchupImg.Visibility = Visibility.Collapsed;
                                    await LoadChampionDataAsync(champId, string.IsNullOrEmpty(mappedRole) ? null : mappedRole);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            _isUpdating = false;
        }
    }

    private static string MapLcuRole(string assignedPosition)
    {
        return assignedPosition.ToUpperInvariant() switch
        {
            "TOP" => "TOP",
            "JUNGLE" => "JUNGLE",
            "MIDDLE" or "MID" => "MIDDLE",
            "BOTTOM" or "BOT" => "BOTTOM",
            "UTILITY" or "SUPPORT" => "UTILITY",
            _ => ""
        };
    }

    // ==================== CHAMPION DATA LOADING & ROLE / SORT RENDERING ====================

    public async Task OpenChampionBuildAsync(int champId, string? role = null)
    {
        await BuildDatabaseService.Instance.InitializeAsync();
        await LoadChampionDataAsync(champId, role);
    }

    public async Task OpenChampionBuildAsync(string champKey, string? role = null)
    {
        await BuildDatabaseService.Instance.InitializeAsync();
        var champ = BuildDatabaseService.Instance.AllChampions.FirstOrDefault(c => 
            string.Equals(c.Key, champKey, StringComparison.OrdinalIgnoreCase) || 
            string.Equals(c.Name, champKey, StringComparison.OrdinalIgnoreCase));
        if (champ != null)
        {
            await LoadChampionDataAsync(champ.Id, role);
        }
    }

    private async Task LoadChampionDataAsync(int champId, string? preferredRole, bool preferMatchup = false, bool preserveSelection = false)
    {
        _currentChampId = champId;

        var data = await BuildDatabaseService.Instance.GetChampionFullDataAsync(
            champId, _currentTierKey, "WORLD", _currentMatchupChampId);

        _currentChampData = data;

        // Top Bar Champion Info
        SelectedChampNameText.Text = data.Champion.Name;
        SetImageSourceSafe(SelectedChampImg, data.Champion.IconUrl);
        OtpBuildsHeaderText.Text = $"{data.Champion.Name} OTP Builds";

        // Online vs Crawler Fallback Indicator
        if (data.IsFromOnline)
        {
            DataSourceBadge.Background = BrushFromHex("#123532");
            DataSourceText.Text = "● Live Blitz.gg";
            DataSourceText.Foreground = BrushFromHex("#2CE5B5");
        }
        else
        {
            DataSourceBadge.Background = BrushFromHex("#362B16");
            DataSourceText.Text = "● Crawler Backup";
            DataSourceText.Foreground = BrushFromHex("#F0A548");
        }

        // Determine active role
        string roleToUse = preferredRole ?? "";
        if (string.IsNullOrEmpty(roleToUse) || (!data.RoleBuilds.ContainsKey(roleToUse) && !data.RoleStats.ContainsKey(roleToUse)))
            roleToUse = data.DefaultRole;

        SetActiveRoleUI(roleToUse, preferMatchup, preserveSelection);
    }

    private void RoleFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string roleTag)
        {
            SetActiveRoleUI(roleTag, preferMatchup: _activePreset?.IsMatchup == true, preserveSelection: false);
        }
    }

    private void SetActiveRoleUI(string role, bool preferMatchup = false, bool preserveSelection = false)
    {
        _currentRole = role;

        FilterTop.IsChecked = role == "TOP";
        FilterJgl.IsChecked = role == "JUNGLE";
        FilterMid.IsChecked = role == "MIDDLE";
        FilterBot.IsChecked = role == "BOTTOM";
        FilterSup.IsChecked = role == "UTILITY";

        if (_currentChampData == null) return;

        // 1. Update Stats Banner for selected role
        if (!_currentChampData.RoleStats.TryGetValue(role, out var stats))
        {
            stats = _currentChampData.RoleStats.Values.FirstOrDefault() ?? new ChampionRoleStats { Role = role };
        }

        TopPatchText.Text = $"Patch: {stats.Patch}";
        RightPatchText.Text = $"Patch: {stats.Patch}";

        StatTierText.Text = stats.TierLabel;
        (string tierFg, string tierBd) = stats.TierLabel switch
        {
            "S" => ("#FFD166", "#5E4B1E"),
            "A" => ("#4DA3FF", "#203B5E"),
            "B" => ("#2CE5B5", "#1A4742"),
            "C" => ("#D8DEE9", "#35394B"),
            _ => ("#8E92A4", "#282B38")
        };
        StatTierText.Foreground = BrushFromHex(tierFg);
        StatTierBadge.BorderBrush = BrushFromHex(tierBd);

        StatWinRateText.Text = $"{(stats.WinRate * 100):F1}%";
        StatWinRateText.Foreground = stats.WinRate >= 0.5 ? BrushFromHex("#2CE5B5") : BrushFromHex("#FF7A7A");

        string sign = stats.WinRateChange > 0 ? "+" : "";
        StatWinRateChangeText.Text = $"{sign}{(stats.WinRateChange * 100):F1}%";
        StatWinRateChangeText.Foreground = stats.WinRateChange >= 0 ? BrushFromHex("#E5E9F0") : BrushFromHex("#D8DEE9");

        StatPickRateText.Text = $"{(stats.PickRate * 100):F1}%";
        StatBanRateText.Text = $"{(stats.BanRate * 100):F1}%";
        StatMatchesText.Text = $"{stats.Matches:N0}";

        // 2. Render Left Sidebar Builds & Select Build
        RenderBuildsSidebar(selectFirst: true, preferMatchup: preferMatchup, preserveSelection: preserveSelection);
    }

    private void SortTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            _currentSortMode = mode;

            // Update tab button styles
            ResetSortBtnStyle(SortWrBtn);
            ResetSortBtnStyle(SortCommonBtn);
            SortOtpCrownBtn.Background = BrushFromHex("#191B24");
            SortOtpCrownBtn.BorderBrush = BrushFromHex("#262936");

            if (mode == "OTP")
            {
                SortOtpCrownBtn.Background = BrushFromHex("#332818");
                SortOtpCrownBtn.BorderBrush = BrushFromHex("#F0A548");
            }
            else
            {
                btn.Background = BrushFromHex("#F0F2F5");
                btn.Foreground = BrushFromHex("#13151C");
                btn.FontWeight = FontWeights.Bold;
            }

            RenderBuildsSidebar(selectFirst: true, preferMatchup: _activePreset?.IsMatchup == true, preserveSelection: false);
        }
    }

    private void ResetSortBtnStyle(Button btn)
    {
        btn.Background = Brushes.Transparent;
        btn.Foreground = BrushFromHex("#8E92A4");
        btn.FontWeight = FontWeights.SemiBold;
    }

    private void RenderBuildsSidebar(bool selectFirst, bool preferMatchup = false, bool preserveSelection = false)
    {
        StandardBuildsPanel.Children.Clear();
        MatchupBuildsPanel.Children.Clear();
        OtpBuildsPanel.Children.Clear();

        if (_currentChampData == null) return;

        if (!_currentChampData.RoleBuilds.TryGetValue(_currentRole, out var roleBuilds) || roleBuilds.Count == 0)
        {
            roleBuilds = _currentChampData.RoleBuilds.Values.FirstOrDefault() ?? new List<ChampionBuildPreset>();
        }

        var stdBuilds = roleBuilds.Where(b => !b.IsOtp && !b.IsMatchup).ToList();
        var matchupBuilds = roleBuilds.Where(b => b.IsMatchup && !b.IsOtp).ToList();
        var otpBuilds = roleBuilds.Where(b => b.IsOtp).ToList();

        Func<IEnumerable<ChampionBuildPreset>, List<ChampionBuildPreset>> sorter = _currentSortMode switch
        {
            "WINRATE" => list => list.OrderByDescending(b => b.WinRate).ThenByDescending(b => b.Games).ToList(),
            "COMMON" => list => list.OrderByDescending(b => b.Games).ToList(),
            _ => list => list.OrderByDescending(b => b.Games).ToList()
        };

        stdBuilds = sorter(stdBuilds);
        matchupBuilds = sorter(matchupBuilds);
        otpBuilds = sorter(otpBuilds);

        StandardBuildsSection.Visibility = _currentSortMode == "OTP" && otpBuilds.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (_currentMatchupChampId.HasValue && matchupBuilds.Count > 0 && _currentSortMode != "OTP")
        {
            string enemyName = !string.IsNullOrEmpty(matchupBuilds[0].MatchupChampionName)
                ? matchupBuilds[0].MatchupChampionName
                : (BuildDatabaseService.Instance.GetChampionInfo(_currentMatchupChampId.Value)?.Name ?? "Enemy");
            MatchupBuildsHeaderText.Text = $"Matchup Builds (vs {enemyName})";
            MatchupBuildsSection.Visibility = Visibility.Visible;
        }
        else
        {
            MatchupBuildsSection.Visibility = Visibility.Collapsed;
        }

        OtpBuildsSection.Visibility = otpBuilds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectFirst)
        {
            bool useWinRateRunes = _currentSortMode == "WINRATE";
            foreach (var b in roleBuilds)
            {
                if (b.RuneOptions != null)
                {
                    foreach (var ro in b.RuneOptions)
                    {
                        var src = useWinRateRunes ? (ro.WinRateRunePreset ?? ro.RunePreset) : (ro.CommonRunePreset ?? ro.RunePreset);
                        ro.RunePreset.PrimaryStyleId = src.PrimaryStyleId;
                        ro.RunePreset.SubStyleId = src.SubStyleId;
                        ro.RunePreset.SelectedPerkIds = src.SelectedPerkIds.ToArray();
                    }

                    var topOpt = GetOrderedRuneOptions(b).FirstOrDefault();
                    if (topOpt != null)
                    {
                        b.RunePreset.PrimaryStyleId = topOpt.RunePreset.PrimaryStyleId;
                        b.RunePreset.SubStyleId = topOpt.RunePreset.SubStyleId;
                        b.RunePreset.SelectedPerkIds = topOpt.RunePreset.SelectedPerkIds.ToArray();
                        b.KeystoneWinRate = topOpt.WinRate;
                        b.KeystoneMatches = topOpt.Matches;
                    }
                }
            }

            if (preserveSelection && _activePreset != null)
            {
                string prevName = _activePreset.BuildName;
                if (_activePreset.IsMatchup && matchupBuilds.Count > 0)
                    _activePreset = matchupBuilds.FirstOrDefault(b => b.BuildName == prevName) ?? matchupBuilds[0];
                else if (_activePreset.IsOtp && otpBuilds.Count > 0)
                    _activePreset = otpBuilds.FirstOrDefault(b => b.BuildName == prevName) ?? otpBuilds[0];
                else
                    _activePreset = stdBuilds.FirstOrDefault(b => b.BuildName == prevName) ?? stdBuilds.FirstOrDefault() ?? matchupBuilds.FirstOrDefault() ?? otpBuilds.FirstOrDefault();
            }
            else if (_currentSortMode == "OTP" && otpBuilds.Count > 0)
            {
                _activePreset = otpBuilds[0];
            }
            else if (preferMatchup && matchupBuilds.Count > 0)
            {
                _activePreset = matchupBuilds[0];
            }
            else
            {
                _activePreset = stdBuilds.FirstOrDefault() ?? matchupBuilds.FirstOrDefault() ?? otpBuilds.FirstOrDefault();
            }
        }

        foreach (var build in stdBuilds)
        {
            StandardBuildsPanel.Children.Add(CreateStandardBuildCard(build, build == _activePreset));
        }

        foreach (var mBuild in matchupBuilds)
        {
            MatchupBuildsPanel.Children.Add(CreateMatchupBuildCard(mBuild, mBuild == _activePreset));
        }

        foreach (var otpBuild in otpBuilds)
        {
            OtpBuildsPanel.Children.Add(CreateOtpBuildCard(otpBuild, otpBuild == _activePreset));
        }

        if (selectFirst && _activePreset != null)
        {
            ApplySelectedPreset(_activePreset);
        }
    }

    private UIElement CreateStandardBuildCard(ChampionBuildPreset build, bool isSelected)
    {
        var card = new Border
        {
            Background = isSelected ? BrushFromHex("#123332") : BrushFromHex("#171922"),
            BorderBrush = isSelected ? BrushFromHex("#2CE5B5") : BrushFromHex("#222532"),
            BorderThickness = isSelected ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: Build Name + Games
        var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        leftStack.Children.Add(new TextBlock
        {
            Text = build.BuildName,
            Foreground = isSelected ? BrushFromHex("#2CE5B5") : Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        leftStack.Children.Add(new TextBlock
        {
            Text = build.GamesDisplay,
            Foreground = BrushFromHex("#8E92A4"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(leftStack, 0);
        grid.Children.Add(leftStack);

        // Right: Keystone+SubTree + 2 Core Items + WinRate Pill
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightStack.Children.Add(CreateRunePairIcon(build.KeystoneIconUrl, build.SubStyleIconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem1IconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem2IconUrl));

        var wrPill = new Border
        {
            Background = isSelected ? BrushFromHex("#1E4645") : BrushFromHex("#252836"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = build.WinRateDisplay,
                Foreground = BrushFromHex("#E5E9F0"),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            }
        };
        rightStack.Children.Add(wrPill);

        Grid.SetColumn(rightStack, 1);
        grid.Children.Add(rightStack);
        card.Child = grid;

        card.MouseEnter += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#1E212E");
                card.BorderBrush = BrushFromHex("#303446");
            }
        };
        card.MouseLeave += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#171922");
                card.BorderBrush = BrushFromHex("#222532");
            }
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            _activePreset = build;
            RenderBuildsSidebar(selectFirst: false);
            ApplySelectedPreset(build);
        };

        return card;
    }

    private UIElement CreateMatchupBuildCard(ChampionBuildPreset build, bool isSelected)
    {
        var card = new Border
        {
            Background = isSelected ? BrushFromHex("#123332") : BrushFromHex("#171922"),
            BorderBrush = isSelected ? BrushFromHex("#2CE5B5") : BrushFromHex("#222532"),
            BorderThickness = isSelected ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: Circular Enemy Portrait + "VS" Badge at Bottom Center (matching Blitz screenshot)
        var vsAvatarGrid = new Grid { Width = 38, Height = 40, Margin = new Thickness(0, 0, 10, 0) };

        var circleRing = new Ellipse
        {
            Width = 36,
            Height = 36,
            Stroke = BrushFromHex("#5C6278"),
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        string enemyIconUrl = !string.IsNullOrEmpty(build.MatchupChampionIconUrl)
            ? build.MatchupChampionIconUrl
            : (_currentMatchupChampId.HasValue ? (BuildDatabaseService.Instance.GetChampionInfo(_currentMatchupChampId.Value)?.IconUrl ?? "") : "");

        if (!string.IsNullOrEmpty(enemyIconUrl))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(enemyIconUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                circleRing.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch
            {
                circleRing.Fill = BrushFromHex("#252836");
            }
        }
        else
        {
            circleRing.Fill = BrushFromHex("#252836");
        }
        RenderOptions.SetBitmapScalingMode(circleRing, BitmapScalingMode.HighQuality);
        vsAvatarGrid.Children.Add(circleRing);

        var vsPill = new Border
        {
            Background = BrushFromHex("#181A24"),
            BorderBrush = BrushFromHex("#3A3F54"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0.5, 4, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock
            {
                Text = "VS",
                Foreground = Brushes.White,
                FontSize = 8.5,
                FontWeight = FontWeights.Black
            }
        };
        vsAvatarGrid.Children.Add(vsPill);
        Grid.SetColumn(vsAvatarGrid, 0);
        grid.Children.Add(vsAvatarGrid);

        // Middle: Build Name + Games
        var midStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        midStack.Children.Add(new TextBlock
        {
            Text = build.BuildName,
            Foreground = isSelected ? BrushFromHex("#2CE5B5") : Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        midStack.Children.Add(new TextBlock
        {
            Text = build.GamesDisplay,
            Foreground = BrushFromHex("#8E92A4"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(midStack, 1);
        grid.Children.Add(midStack);

        // Right: Keystone+SubTree + 2 Core Items + WinRate Pill
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightStack.Children.Add(CreateRunePairIcon(build.KeystoneIconUrl, build.SubStyleIconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem1IconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem2IconUrl));

        var wrPill = new Border
        {
            Background = isSelected ? BrushFromHex("#1E4645") : BrushFromHex("#252836"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = build.WinRateDisplay,
                Foreground = BrushFromHex("#E5E9F0"),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            }
        };
        rightStack.Children.Add(wrPill);

        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);
        card.Child = grid;

        card.MouseEnter += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#1E212E");
                card.BorderBrush = BrushFromHex("#303446");
            }
        };
        card.MouseLeave += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#171922");
                card.BorderBrush = BrushFromHex("#222532");
            }
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            _activePreset = build;
            RenderBuildsSidebar(selectFirst: false);
            ApplySelectedPreset(build);
        };

        return card;
    }

    private UIElement CreateOtpBuildCard(ChampionBuildPreset build, bool isSelected)
    {
        var otp = build.OtpPlayer ?? new OtpPlayerInfo { Name = build.BuildName, Region = "KR", RankTier = "MASTER", Lp = 400 };

        var card = new Border
        {
            Background = isSelected ? BrushFromHex("#2A2218") : BrushFromHex("#171922"),
            BorderBrush = isSelected ? BrushFromHex("#F0A548") : BrushFromHex("#222532"),
            BorderThickness = isSelected ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Avatar + Region Badge
        var avatarGrid = new Grid { Width = 36, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        var avatarBorder = new Border
        {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderBrush = BrushFromHex("#E84057"),
            BorderThickness = new Thickness(1.5),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top
        };
        var avatarImg = new Image { Stretch = Stretch.UniformToFill };
        SetImageSourceSafe(avatarImg, _currentChampData?.Champion.IconUrl ?? "");
        avatarBorder.Child = avatarImg;
        avatarGrid.Children.Add(avatarBorder);

        var regionPill = new Border
        {
            Background = otp.CleanRegion == "KR" ? BrushFromHex("#1D5B9E") : BrushFromHex("#1A7A6C"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock
            {
                Text = otp.CleanRegion,
                Foreground = Brushes.White,
                FontSize = 8,
                FontWeight = FontWeights.Black
            }
        };
        avatarGrid.Children.Add(regionPill);
        Grid.SetColumn(avatarGrid, 0);
        grid.Children.Add(avatarGrid);

        // Name #Tag & Riot Rank Icon + Rank Text
        var midStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        midStack.Children.Add(new TextBlock
        {
            Text = otp.DisplayRiotId,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        string rankColor = GetRankColorHex(otp.RankTier);
        var rankRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var rankCrestImg = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(rankCrestImg, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(rankCrestImg, GetRankIconUrl(otp.RankTier));
        rankRow.Children.Add(rankCrestImg);

        rankRow.Children.Add(new TextBlock
        {
            Text = otp.FormattedRank,
            Foreground = BrushFromHex(rankColor),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        midStack.Children.Add(rankRow);
        Grid.SetColumn(midStack, 1);
        grid.Children.Add(midStack);

        // Right: Keystone + 2 Core Items
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightStack.Children.Add(CreateRunePairIcon(build.KeystoneIconUrl, build.SubStyleIconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem1IconUrl));
        rightStack.Children.Add(CreateSmallItemIcon(build.CoreItem2IconUrl));
        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        card.Child = grid;

        card.MouseEnter += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#1E212E");
                card.BorderBrush = BrushFromHex("#303446");
            }
        };
        card.MouseLeave += (s, e) =>
        {
            if (!isSelected)
            {
                card.Background = BrushFromHex("#171922");
                card.BorderBrush = BrushFromHex("#222532");
            }
        };

        card.MouseLeftButtonDown += (s, e) =>
        {
            _activePreset = build;
            RenderBuildsSidebar(selectFirst: false);
            ApplySelectedPreset(build);
        };

        return card;
    }

    private UIElement CreateRunePairIcon(string keystoneUrl, string subTreeUrl)
    {
        var grid = new Grid { Width = 30, Height = 26, Margin = new Thickness(0, 0, 4, 0) };
        var ksImg = new Image { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(ksImg, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(ksImg, keystoneUrl);
        grid.Children.Add(ksImg);

        var subBd = new Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = BrushFromHex("#13151C"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var subImg = new Image { Width = 11, Height = 11 };
        RenderOptions.SetBitmapScalingMode(subImg, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(subImg, subTreeUrl);
        subBd.Child = subImg;
        grid.Children.Add(subBd);

        return grid;
    }

    private UIElement CreateSmallItemIcon(string iconUrl)
    {
        var bd = new Border
        {
            Width = 25, Height = 25,
            CornerRadius = new CornerRadius(4),
            BorderBrush = BrushFromHex("#2E3244"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 3, 0),
            ClipToBounds = true,
            Background = BrushFromHex("#13151C")
        };
        var img = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(img, iconUrl);
        bd.Child = img;
        return bd;
    }

    // ==================== APPLY SELECTED BUILD TO CENTER & RIGHT PANELS ====================

    private List<RuneOptionInfo> GetOrderedRuneOptions(ChampionBuildPreset preset)
    {
        if (preset.RuneOptions == null || preset.RuneOptions.Count == 0)
        {
            return new List<RuneOptionInfo>
            {
                new RuneOptionInfo
                {
                    RunePreset = preset.RunePreset,
                    WinRate = preset.KeystoneWinRate,
                    Matches = preset.KeystoneMatches
                }
            };
        }

        if (_currentSortMode == "WINRATE")
        {
            return preset.RuneOptions
                .OrderByDescending(o => Math.Round(o.WinRate * 100))
                .ThenByDescending(o => o.Matches)
                .ToList();
        }

        return preset.RuneOptions
            .OrderByDescending(o => o.Matches)
            .ToList();
    }

    private void ApplySelectedPreset(ChampionBuildPreset preset)
    {
        var runeOptions = GetOrderedRuneOptions(preset);
        _activeRuneOptionIndex = 0;

        var activeRuneOpt = runeOptions[0];
        preset.RunePreset.PrimaryStyleId = activeRuneOpt.RunePreset.PrimaryStyleId;
        preset.RunePreset.SubStyleId = activeRuneOpt.RunePreset.SubStyleId;
        preset.RunePreset.SelectedPerkIds = activeRuneOpt.RunePreset.SelectedPerkIds.ToArray();
        preset.KeystoneWinRate = activeRuneOpt.WinRate;
        preset.KeystoneMatches = activeRuneOpt.Matches;

        _activePageName = preset.RunePreset.Name;
        _primaryTreeId = preset.RunePreset.PrimaryStyleId;
        _secondaryTreeId = preset.RunePreset.SubStyleId;
        _selectedPerks = preset.RunePreset.SelectedPerkIds.ToList();

        RenderRuneOptions(preset);
        RenderRuneBuilder();
        RenderSkillOrder(preset.SkillOrder);

        _currentSpell1Id = preset.Spell1Id > 0 ? preset.Spell1Id : 4;
        _currentSpell2Id = preset.Spell2Id > 0 ? preset.Spell2Id : 14;
        RenderSpellOptions(preset);

        RenderItemBuilder(preset);

        _ = AutoInjectBuildAsync();
    }

    private void RenderRuneOptions(ChampionBuildPreset preset)
    {
        RuneOptionsPanel.Children.Clear();
        var options = GetOrderedRuneOptions(preset);

        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var opt = options[i];
            bool isSelected = idx == _activeRuneOptionIndex;

            var card = new Border
            {
                Background = isSelected ? BrushFromHex("#1D202E") : BrushFromHex("#171923"),
                BorderBrush = isSelected ? BrushFromHex("#E5E9F0") : BrushFromHex("#242736"),
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(4, 0, 4, 4),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();

            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var runePair = CreateRunePairIcon(opt.KeystoneIconUrl, opt.SubStyleIconUrl);
            if (runePair is FrameworkElement fe)
                fe.Margin = new Thickness(0, 0, 12, 0);
            topRow.Children.Add(runePair);

            topRow.Children.Add(new Border
            {
                Background = BrushFromHex("#2A2E3D"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{Math.Round(opt.WinRate * 100):0}%",
                    Foreground = BrushFromHex("#E5E9F0"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            });

            stack.Children.Add(topRow);
            stack.Children.Add(new TextBlock
            {
                Text = $"{opt.Matches:N0} GAMES",
                Foreground = BrushFromHex("#8E92A4"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            card.Child = stack;

            card.MouseEnter += (s, e) =>
            {
                if (!isSelected)
                {
                    card.Background = BrushFromHex("#1F2230");
                    card.BorderBrush = BrushFromHex("#35394C");
                }
            };
            card.MouseLeave += (s, e) =>
            {
                if (!isSelected)
                {
                    card.Background = BrushFromHex("#171923");
                    card.BorderBrush = BrushFromHex("#242736");
                }
            };

            card.MouseLeftButtonDown += (s, e) =>
            {
                _activeRuneOptionIndex = idx;
                _primaryTreeId = opt.RunePreset.PrimaryStyleId;
                _secondaryTreeId = opt.RunePreset.SubStyleId;
                _selectedPerks = opt.RunePreset.SelectedPerkIds.ToList();

                preset.RunePreset.PrimaryStyleId = _primaryTreeId;
                preset.RunePreset.SubStyleId = _secondaryTreeId;
                preset.RunePreset.SelectedPerkIds = _selectedPerks.Take(9).ToArray();
                preset.KeystoneWinRate = opt.WinRate;
                preset.KeystoneMatches = opt.Matches;

                RenderRuneOptions(preset);
                RenderRuneBuilder();
                RenderBuildsSidebar(selectFirst: false);
                _ = PushRunesToClientAsync();
            };

            RuneOptionsPanel.Children.Add(card);
        }
    }

    // ==================== CENTER PANEL: RUNES VISUALIZER ====================

    private void RenderRuneBuilder()
    {
        InteractiveRuneBuilder.Children.Clear();

        var allTrees = RuneService.Instance.AllTrees;
        if (allTrees.Count == 0) return;

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // --- LEFT: PRIMARY TREE ---
        var primaryPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var primaryTreeSelector = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var tree in allTrees)
        {
            var img = CreateRuneNode(tree.Icon, 24, tree.Id == _primaryTreeId, () => OnTreeClicked(tree.Id, true), isTreeIcon: true, tooltip: tree.Name);
            img.Margin = new Thickness(5, 0, 5, 0);
            primaryTreeSelector.Children.Add(img);
        }
        primaryPanel.Children.Add(primaryTreeSelector);

        var primaryTreeData = allTrees.FirstOrDefault(t => t.Id == _primaryTreeId);
        if (primaryTreeData != null)
        {
            for (int slotIdx = 0; slotIdx < primaryTreeData.Slots.Count; slotIdx++)
            {
                var slot = primaryTreeData.Slots[slotIdx];
                bool isKeystone = slotIdx == 0;
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, isKeystone ? 16 : 12)
                };

                foreach (var rune in slot.Runes)
                {
                    bool isSelected = _selectedPerks.Take(4).Contains(rune.Id);
                    var node = CreateRuneNode(rune.Icon, isKeystone ? 46 : 36, isSelected, () => OnRuneClicked(rune.Id, true), isKeystone: isKeystone, tooltip: rune.Name);
                    node.Margin = new Thickness(isKeystone ? 7 : 9, 0, isKeystone ? 7 : 9, 0);
                    rowPanel.Children.Add(node);
                }
                primaryPanel.Children.Add(rowPanel);
            }
        }

        Grid.SetColumn(primaryPanel, 0);
        mainGrid.Children.Add(primaryPanel);

        // --- RIGHT: SECONDARY TREE & STAT SHARDS ---
        var secondaryPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var secondaryTreeSelector = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var tree in allTrees)
        {
            bool isPrimary = tree.Id == _primaryTreeId;
            bool isSelected = tree.Id == _secondaryTreeId && !isPrimary;
            var node = CreateRuneNode(tree.Icon, 22, isSelected, () => { if (!isPrimary) OnTreeClicked(tree.Id, false); }, isTreeIcon: true, tooltip: tree.Name);
            node.Margin = new Thickness(5, 0, 5, 0);
            if (isPrimary) node.Opacity = 0.15;
            secondaryTreeSelector.Children.Add(node);
        }
        secondaryPanel.Children.Add(secondaryTreeSelector);

        var secondaryTreeData = allTrees.FirstOrDefault(t => t.Id == _secondaryTreeId);
        if (secondaryTreeData != null)
        {
            for (int i = 1; i < secondaryTreeData.Slots.Count; i++)
            {
                int captureSlotIndex = i;
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
                foreach (var rune in secondaryTreeData.Slots[i].Runes)
                {
                    int captureRuneId = rune.Id;
                    bool isSelected = _selectedPerks.Skip(4).Take(2).Contains(captureRuneId);
                    var node = CreateRuneNode(rune.Icon, 34, isSelected, () => OnRuneClicked(captureRuneId, false, captureSlotIndex), isSecondary: true, tooltip: rune.Name);
                    node.Margin = new Thickness(8, 0, 8, 0);
                    rowPanel.Children.Add(node);
                }
                secondaryPanel.Children.Add(rowPanel);
            }
        }

        // Stat Shards (3 rows x 3 columns)
        secondaryPanel.Children.Add(new Border { Height = 6 });
        int[][] shardRows =
        {
            new[] { 5008, 5005, 5007 },
            new[] { 5008, 5010, 5001 },
            new[] { 5011, 5013, 5001 }
        };

        for (int i = 0; i < shardRows.Length; i++)
        {
            int captureShardIndex = i;
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
            foreach (int shardId in shardRows[i])
            {
                int captureShardId = shardId;
                bool isSelected = _selectedPerks.Count > 6 + captureShardIndex && _selectedPerks[6 + captureShardIndex] == captureShardId;
                string iconUrl = RuneService.Instance.GetImageUrl(captureShardId);
                var node = CreateRuneNode(iconUrl, 24, isSelected, () => OnShardClicked(captureShardId, captureShardIndex), isShard: true, tooltip: GetShardName(captureShardId));
                node.Margin = new Thickness(8, 0, 8, 0);
                rowPanel.Children.Add(node);
            }
            secondaryPanel.Children.Add(rowPanel);
        }

        Grid.SetColumn(secondaryPanel, 2);
        mainGrid.Children.Add(secondaryPanel);

        InteractiveRuneBuilder.Children.Add(mainGrid);
    }

    private FrameworkElement CreateRuneNode(
        string url,
        double size,
        bool isSelected,
        Action onClick,
        bool isKeystone = false,
        bool isTreeIcon = false,
        bool isSecondary = false,
        bool isShard = false,
        string tooltip = "")
    {
        var img = new Image
        {
            Width = isShard ? size - 6 : (isKeystone || isTreeIcon ? size : size - 4),
            Height = isShard ? size - 6 : (isKeystone || isTreeIcon ? size : size - 4),
            Opacity = isSelected ? 1.0 : 0.25,
            Stretch = Stretch.Uniform
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(img, url);

        string ringColor = isSecondary ? "#75C97B" : "#E5B85B";
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = isKeystone || isTreeIcon ? Brushes.Transparent : BrushFromHex("#1A1D26"),
            BorderBrush = isSelected && !isKeystone && !isTreeIcon ? BrushFromHex(ringColor) : (isKeystone || isTreeIcon ? Brushes.Transparent : BrushFromHex("#2D3140")),
            BorderThickness = new Thickness(isKeystone || isTreeIcon ? 0 : (isSelected ? 2 : 1.5)),
            Cursor = Cursors.Hand,
            Child = img,
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : new ToolTip
            {
                Content = tooltip,
                Background = BrushFromHex("#181A22"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            }
        };

        border.MouseEnter += (s, e) => { if (!isSelected) img.Opacity = 0.65; };
        border.MouseLeave += (s, e) => { if (!isSelected) img.Opacity = 0.25; };
        border.MouseLeftButtonDown += (s, e) => onClick();

        return border;
    }

    private static string GetShardName(int id) => id switch
    {
        5008 => "Adaptive Force",
        5005 => "Attack Speed",
        5007 => "Ability Haste",
        5002 => "Armor",
        5003 => "Magic Resist",
        5001 => "Scaling Health",
        5011 => "Flat Health",
        5013 => "Tenacity & Slow Resist",
        5010 => "Move Speed",
        _ => "Stat Shard"
    };

    private void OnTreeClicked(int treeId, bool isPrimary)
    {
        if (isPrimary)
        {
            if (_primaryTreeId == treeId) return;
            _primaryTreeId = treeId;
            if (_secondaryTreeId == treeId)
                _secondaryTreeId = RuneService.Instance.AllTrees.First(t => t.Id != treeId).Id;

            var tree = RuneService.Instance.AllTrees.First(t => t.Id == treeId);
            while (_selectedPerks.Count < 4) _selectedPerks.Add(0);
            for (int i = 0; i < 4; i++) _selectedPerks[i] = tree.Slots[i].Runes[0].Id;
        }
        else
        {
            if (_secondaryTreeId == treeId) return;
            _secondaryTreeId = treeId;
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == treeId);
            while (_selectedPerks.Count < 6) _selectedPerks.Add(0);
            _selectedPerks[4] = tree.Slots[1].Runes[0].Id;
            _selectedPerks[5] = tree.Slots[2].Runes[0].Id;
        }

        SyncEditedRunesToActivePreset();
        RenderRuneBuilder();
        _ = PushRunesToClientAsync();
    }

    private void OnRuneClicked(int runeId, bool isPrimary, int secondarySlotIndex = 0)
    {
        if (isPrimary)
        {
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == _primaryTreeId);
            for (int i = 0; i < tree.Slots.Count; i++)
            {
                if (tree.Slots[i].Runes.Any(r => r.Id == runeId))
                {
                    while (_selectedPerks.Count <= i) _selectedPerks.Add(0);
                    _selectedPerks[i] = runeId;
                    break;
                }
            }
        }
        else
        {
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == _secondaryTreeId);
            var activeRows = new List<int>();
            for (int i = 1; i < tree.Slots.Count; i++)
            {
                if (_selectedPerks.Skip(4).Take(2).Any(id => tree.Slots[i].Runes.Any(r => r.Id == id)))
                    activeRows.Add(i);
            }

            if (activeRows.Contains(secondarySlotIndex))
            {
                for (int i = 4; i < 6; i++)
                {
                    if (tree.Slots[secondarySlotIndex].Runes.Any(r => r.Id == _selectedPerks[i]))
                    {
                        _selectedPerks[i] = runeId;
                        break;
                    }
                }
            }
            else
            {
                while (_selectedPerks.Count < 6) _selectedPerks.Add(0);
                _selectedPerks[5] = _selectedPerks[4];
                _selectedPerks[4] = runeId;
            }
        }

        SyncEditedRunesToActivePreset();
        RenderRuneBuilder();
        _ = PushRunesToClientAsync();
    }

    private void OnShardClicked(int shardId, int rowIndex)
    {
        while (_selectedPerks.Count <= 6 + rowIndex) _selectedPerks.Add(0);
        _selectedPerks[6 + rowIndex] = shardId;

        SyncEditedRunesToActivePreset();
        RenderRuneBuilder();
        _ = PushRunesToClientAsync();
    }

    private void SyncEditedRunesToActivePreset()
    {
        if (_activePreset == null) return;
        _activePreset.RunePreset.PrimaryStyleId = _primaryTreeId;
        _activePreset.RunePreset.SubStyleId = _secondaryTreeId;
        _activePreset.RunePreset.SelectedPerkIds = _selectedPerks.Take(9).ToArray();

        var ordered = GetOrderedRuneOptions(_activePreset);
        if (_activeRuneOptionIndex >= 0 && _activeRuneOptionIndex < ordered.Count)
        {
            ordered[_activeRuneOptionIndex].RunePreset.PrimaryStyleId = _primaryTreeId;
            ordered[_activeRuneOptionIndex].RunePreset.SubStyleId = _secondaryTreeId;
            ordered[_activeRuneOptionIndex].RunePreset.SelectedPerkIds = _selectedPerks.Take(9).ToArray();
        }

        RenderRuneOptions(_activePreset);
        RenderBuildsSidebar(selectFirst: false);
    }

    // ==================== CENTER PANEL: ABILITY MAX ORDER (1-18 GRID) ====================

    private void RenderSkillOrder(SkillOrderInfo skillOrder)
    {
        SkillPriorityPanel.Children.Clear();
        SkillOrderGridContainer.Children.Clear();
        SkillOrderGridContainer.RowDefinitions.Clear();
        SkillOrderGridContainer.ColumnDefinitions.Clear();

        SkillOrderWrText.Text = $"{Math.Round(skillOrder.WinRate * 100):0}%";
        SkillOrderGamesText.Text = $"{skillOrder.Matches:N0} GAMES";

        var spells = _currentChampData?.Champion.Spells ?? new List<ChampionSpellInfo>();
        string GetSpellIcon(int slot) => spells.FirstOrDefault(s => s.Slot == slot)?.IconUrl ?? "";
        string GetSpellKey(int slot) => slot switch { 1 => "Q", 2 => "W", 3 => "E", 4 => "R", _ => "Q" };
        string GetSpellColor(int slot) => slot switch
        {
            1 => "#E8833A", // Q Orange
            2 => "#36B5E8", // W Cyan
            3 => "#4DA3FF", // E Blue
            4 => "#F0C654", // R Gold
            _ => "#E5E9F0"
        };

        // 1. Left Priority Icons: [Q] > [E] > [W]
        var priority = skillOrder.GetPriority();
        for (int i = 0; i < priority.Count; i++)
        {
            int slot = priority[i];
            string key = GetSpellKey(slot);
            string color = GetSpellColor(slot);

            var iconContainer = new Grid { Width = 36, Height = 40 };
            var imgBorder = new Border
            {
                Width = 34, Height = 34,
                CornerRadius = new CornerRadius(6),
                BorderBrush = BrushFromHex("#2E3244"),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Top,
                Background = BrushFromHex("#1A1D26")
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, GetSpellIcon(slot));
            imgBorder.Child = img;
            iconContainer.Children.Add(imgBorder);

            var badge = new Border
            {
                Background = BrushFromHex("#13151C"),
                BorderBrush = BrushFromHex("#2E3244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = key,
                    Foreground = BrushFromHex(color),
                    FontSize = 9,
                    FontWeight = FontWeights.Black
                }
            };
            iconContainer.Children.Add(badge);
            SkillPriorityPanel.Children.Add(iconContainer);

            if (i < priority.Count - 1)
            {
                SkillPriorityPanel.Children.Add(new TextBlock
                {
                    Text = "›",
                    Foreground = BrushFromHex("#6E7385"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 4)
                });
            }
        }

        // 2. Right 1-18 Grid
        // Column 0: Spell Icon, Columns 1..18: Levels 1..18
        SkillOrderGridContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (int lvl = 1; lvl <= 18; lvl++)
        {
            SkillOrderGridContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 20 });
        }

        // Row 0: Level Numbers 1..18, Rows 1..4: Q, W, E, R
        for (int r = 0; r <= 4; r++)
        {
            SkillOrderGridContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 18 : 24) });
        }

        for (int lvl = 1; lvl <= 18; lvl++)
        {
            var lvlText = new TextBlock
            {
                Text = lvl.ToString(),
                Foreground = BrushFromHex("#7F8396"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(lvlText, 0);
            Grid.SetColumn(lvlText, lvl);
            SkillOrderGridContainer.Children.Add(lvlText);
        }

        for (int slot = 1; slot <= 4; slot++)
        {
            var spImgBd = new Border
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = BrushFromHex("#1A1D26")
            };
            var spImg = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(spImg, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(spImg, GetSpellIcon(slot));
            spImgBd.Child = spImg;
            Grid.SetRow(spImgBd, slot);
            Grid.SetColumn(spImgBd, 0);
            SkillOrderGridContainer.Children.Add(spImgBd);

            string key = GetSpellKey(slot);
            string color = GetSpellColor(slot);

            for (int lvl = 1; lvl <= 18; lvl++)
            {
                bool isLeveled = skillOrder.Order.Count >= lvl && skillOrder.Order[lvl - 1] == slot;
                var cell = new Border
                {
                    Height = 20,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Background = isLeveled ? BrushFromHex("#1E2330") : BrushFromHex("#171922"),
                    BorderBrush = isLeveled ? BrushFromHex("#2E3548") : Brushes.Transparent,
                    BorderThickness = new Thickness(1)
                };

                if (isLeveled)
                {
                    cell.Child = new TextBlock
                    {
                        Text = key,
                        Foreground = BrushFromHex(color),
                        FontSize = 10,
                        FontWeight = FontWeights.Black,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }

                Grid.SetRow(cell, slot);
                Grid.SetColumn(cell, lvl);
                SkillOrderGridContainer.Children.Add(cell);
            }
        }
    }

    // ==================== RIGHT PANEL: SUMMONER SPELLS & ITEMS ====================

    private void InitSpellMenus()
    {
        foreach (var spell in _summonerSpells)
        {
            var item1 = new MenuItem { Header = spell.Name, Tag = spell.Id };
            item1.Click += (s, e) => SelectSpell(1, spell.Id);
            _spell1Menu.Items.Add(item1);

            var item2 = new MenuItem { Header = spell.Name, Tag = spell.Id };
            item2.Click += (s, e) => SelectSpell(2, spell.Id);
            _spell2Menu.Items.Add(item2);
        }
    }

    private void SelectSpell(int slot, int spellId)
    {
        if (slot == 1)
        {
            if (_currentSpell2Id == spellId) _currentSpell2Id = _currentSpell1Id;
            _currentSpell1Id = spellId;
        }
        else
        {
            if (_currentSpell1Id == spellId) _currentSpell1Id = _currentSpell2Id;
            _currentSpell2Id = spellId;
        }

        if (_activePreset != null)
        {
            _activePreset.Spell1Id = _currentSpell1Id;
            _activePreset.Spell2Id = _currentSpell2Id;
            if (_activePreset.SpellOptions.Count > 0)
            {
                _activePreset.SpellOptions[0].Spell1Id = _currentSpell1Id;
                _activePreset.SpellOptions[0].Spell2Id = _currentSpell2Id;
            }
            RenderSpellOptions(_activePreset);
        }

        _ = PushSpellsToClientAsync();
    }

    private void RenderSpellOptions(ChampionBuildPreset preset)
    {
        SpellOptionsPanel.Children.Clear();

        var options = preset.SpellOptions.Count > 0
            ? preset.SpellOptions
            : new List<SummonerSpellOption> { new() { Spell1Id = _currentSpell1Id, Spell2Id = _currentSpell2Id, WinRate = preset.WinRate, Matches = preset.Games } };

        for (int i = 0; i < options.Count && i < 2; i++)
        {
            var opt = options[i];
            bool isSelectedPair = opt.Spell1Id == _currentSpell1Id && opt.Spell2Id == _currentSpell2Id;

            var card = new Border
            {
                Background = isSelectedPair ? BrushFromHex("#1A232E") : BrushFromHex("#181A24"),
                BorderBrush = isSelectedPair ? BrushFromHex("#2CE5B5") : BrushFromHex("#232634"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Spell 1 Icon
            var sp1Model = _summonerSpells.FirstOrDefault(s => s.Id == opt.Spell1Id) ?? _summonerSpells[2];
            var sp1Bd = CreateSpellIconBorder(sp1Model);
            sp1Bd.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _currentSpell1Id = opt.Spell1Id;
                _currentSpell2Id = opt.Spell2Id;
                _spell1Menu.PlacementTarget = sp1Bd;
                _spell1Menu.IsOpen = true;
            };
            row.Children.Add(sp1Bd);

            // Spell 2 Icon
            var sp2Model = _summonerSpells.FirstOrDefault(s => s.Id == opt.Spell2Id) ?? _summonerSpells[7];
            var sp2Bd = CreateSpellIconBorder(sp2Model);
            sp2Bd.Margin = new Thickness(4, 0, 12, 0);
            sp2Bd.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _currentSpell1Id = opt.Spell1Id;
                _currentSpell2Id = opt.Spell2Id;
                _spell2Menu.PlacementTarget = sp2Bd;
                _spell2Menu.IsOpen = true;
            };
            row.Children.Add(sp2Bd);

            // Win Rate Pill
            row.Children.Add(new Border
            {
                Background = BrushFromHex("#123837"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{Math.Round(opt.WinRate * 100):0}%",
                    Foreground = BrushFromHex("#2CE5B5"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            });

            // Games Count
            row.Children.Add(new TextBlock
            {
                Text = $"{opt.Matches:N0} GAMES",
                Foreground = BrushFromHex("#9CA0B0"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            card.Child = row;

            card.MouseLeftButtonDown += (s, e) =>
            {
                _currentSpell1Id = opt.Spell1Id;
                _currentSpell2Id = opt.Spell2Id;
                preset.Spell1Id = _currentSpell1Id;
                preset.Spell2Id = _currentSpell2Id;
                RenderSpellOptions(preset);
                _ = PushSpellsToClientAsync();
            };

            SpellOptionsPanel.Children.Add(card);
        }
    }

    private Border CreateSpellIconBorder(SummonerSpellModel spell)
    {
        var img = new Image { Width = 28, Height = 28, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(img, spell.IconUrl);

        return new Border
        {
            Width = 30, Height = 30,
            CornerRadius = new CornerRadius(4),
            BorderBrush = BrushFromHex("#34384B"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            ToolTip = $"{spell.Name} (Click to swap)",
            Child = img
        };
    }

    private void RenderItemBuilder(ChampionBuildPreset? preset)
    {
        InteractiveItemBuilder.Children.Clear();
        if (preset == null) return;

        var coreSet = new HashSet<int>(preset.CoreFinalBuildIds.Take(3));

        RenderItemSection("Starting Items", preset.StartingItemIds, iconSize: 32);
        RenderItemSection("Build Order", preset.CoreBuildOrderIds, iconSize: 32, highlightIds: coreSet);
        RenderItemSection("Completed Items", preset.CoreFinalBuildIds, iconSize: 34);
        RenderItemSection("Situational Items", preset.SituationalItemIds, iconSize: 32);
    }

    private void RenderItemSection(string title, List<int> itemIds, double iconSize = 32, HashSet<int>? highlightIds = null)
    {
        if (itemIds == null) return;

        InteractiveItemBuilder.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = BrushFromHex("#8E92A4"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 6)
        });

        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

        for (int i = 0; i < itemIds.Count; i++)
        {
            int index = i;
            int itemId = itemIds[i];
            var item = ItemService.Instance.GetItem(itemId);
            if (item == null) continue;

            bool isHighlighted = highlightIds != null && highlightIds.Contains(itemId);

            var img = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.UniformToFill,
                Cursor = Cursors.Hand,
                ToolTip = new ToolTip
                {
                    Content = item.Name,
                    Background = BrushFromHex("#181A22"),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                }
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, item.IconUrl);

            var border = new Border
            {
                Width = iconSize + 2,
                Height = iconSize + 2,
                BorderBrush = isHighlighted ? BrushFromHex("#D4A548") : BrushFromHex("#2E3244"),
                BorderThickness = new Thickness(isHighlighted ? 1.5 : 1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 5, 5),
                ClipToBounds = true,
                Background = BrushFromHex("#13151C"),
                Child = img
            };

            border.MouseEnter += (s, e) => border.Opacity = 0.85;
            border.MouseLeave += (s, e) => border.Opacity = 1.0;

            border.MouseLeftButtonDown += (s, e) =>
            {
                OpenItemPicker(async newId =>
                {
                    if (newId == null) itemIds.RemoveAt(index);
                    else itemIds[index] = newId.Value;
                    RenderItemBuilder(_activePreset);
                    if (_activePreset != null && LcuApiClient.Instance.IsConnected)
                        await LcuApiClient.Instance.SetItemBuildAsync(_activePreset);
                });
            };

            panel.Children.Add(border);
        }

        var addBtn = CreateIconBlock("+", "#6E7385", "#181A24", iconSize + 2);
        addBtn.MouseLeftButtonDown += (s, e) =>
        {
            OpenItemPicker(async newId =>
            {
                if (newId != null)
                {
                    itemIds.Add(newId.Value);
                    RenderItemBuilder(_activePreset);
                    if (_activePreset != null && LcuApiClient.Instance.IsConnected)
                        await LcuApiClient.Instance.SetItemBuildAsync(_activePreset);
                }
            });
        };
        panel.Children.Add(addBtn);

        InteractiveItemBuilder.Children.Add(panel);
    }

    private Border CreateIconBlock(string text, string fgColor, string bgColor, double size = 34)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = BrushFromHex(fgColor),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0)
        };
        var border = new Border
        {
            Width = size, Height = size,
            Background = BrushFromHex(bgColor),
            BorderBrush = BrushFromHex("#2E3244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 5, 5),
            Cursor = Cursors.Hand,
            Child = tb,
            ToolTip = text == "+" ? "Add Item" : "Remove Item"
        };
        border.MouseEnter += (s, e) => border.Opacity = 0.8;
        border.MouseLeave += (s, e) => border.Opacity = 1.0;
        return border;
    }

    // ==================== ITEM PICKER OVERLAY ====================

    private void OpenItemPicker(Action<int?> callback)
    {
        _onItemPickedCallback = callback;
        ItemPickerOverlay.Visibility = Visibility.Visible;
        ItemSearchBox.Text = "";
        PopulateItemPicker("");
        ItemSearchBox.Focus();
    }

    private void CloseItemPicker_Click(object sender, RoutedEventArgs e)
    {
        ItemPickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void ItemSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateItemPicker(ItemSearchBox.Text);
    }

    private void PopulateItemPicker(string filter)
    {
        if (!_itemPickerInitialized)
        {
            AllItemsWrapPanel.Children.Clear();

            var removeBtn = CreateIconBlock("✕", "#FF7A7A", "#252836", 40);
            removeBtn.MouseLeftButtonDown += (s, e) =>
            {
                ItemPickerOverlay.Visibility = Visibility.Collapsed;
                _onItemPickedCallback?.Invoke(null);
            };
            removeBtn.Tag = "REMOVE";
            AllItemsWrapPanel.Children.Add(removeBtn);

            foreach (var item in ItemService.Instance.AllItems.OrderBy(x => x.Name))
            {
                var img = new Image
                {
                    Width = 38, Height = 38,
                    Cursor = Cursors.Hand,
                    ToolTip = item.Name
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                SetImageSourceSafe(img, item.IconUrl);

                var border = new Border
                {
                    BorderBrush = BrushFromHex("#34384B"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(4),
                    Child = img,
                    Tag = item
                };

                int capturedId = item.Id;
                border.MouseLeftButtonDown += (s, e) =>
                {
                    ItemPickerOverlay.Visibility = Visibility.Collapsed;
                    _onItemPickedCallback?.Invoke(capturedId);
                };

                AllItemsWrapPanel.Children.Add(border);
            }
            _itemPickerInitialized = true;
        }

        filter = filter.ToLowerInvariant();
        foreach (UIElement child in AllItemsWrapPanel.Children)
        {
            if (child is Border b)
            {
                if (b.Tag is string s && s == "REMOVE")
                {
                    b.Visibility = Visibility.Visible;
                }
                else if (b.Tag is GameItem item)
                {
                    bool match = string.IsNullOrEmpty(filter) || item.Name.ToLowerInvariant().Contains(filter);
                    b.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    // ==================== CLIENT INJECTION ====================

    public async Task AutoInjectBuildAsync()
    {
        if (_activePreset == null || !LcuApiClient.Instance.IsConnected) return;
        try
        {
            if (_selectedPerks.Count >= 9)
            {
                await LcuApiClient.Instance.SetRunePageAsync(
                    _activePageName, _primaryTreeId, _secondaryTreeId, _selectedPerks.Take(9).ToArray());
            }

            await LcuApiClient.Instance.SetItemBuildAsync(_activePreset);
            await LcuApiClient.Instance.SetSummonerSpellsAsync(_currentSpell1Id, _currentSpell2Id);
        }
        catch { }
    }

    private async Task PushRunesToClientAsync()
    {
        if (!LcuApiClient.Instance.IsConnected || _selectedPerks.Count < 9) return;
        try
        {
            await LcuApiClient.Instance.SetRunePageAsync(_activePageName, _primaryTreeId, _secondaryTreeId, _selectedPerks.Take(9).ToArray());
        }
        catch { }
    }

    private async Task PushSpellsToClientAsync()
    {
        if (!LcuApiClient.Instance.IsConnected) return;
        try
        {
            await LcuApiClient.Instance.SetSummonerSpellsAsync(_currentSpell1Id, _currentSpell2Id);
        }
        catch { }
    }

    // ==================== HELPERS ====================

    private static string GetRankIconUrl(string tierOrRank)
    {
        string clean = (tierOrRank ?? "EMERALD")
            .Replace("+", "")
            .Replace("%2B", "")
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
            .Replace("%2B", "")
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
