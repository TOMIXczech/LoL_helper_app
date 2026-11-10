using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public partial class SettingsView : UserControl
{
    public event Action? OnBackRequested;

    private bool _isInitialized = false;
    private bool _isSyncingUi = false;

    public SettingsView()
    {
        InitializeComponent();

        AppSettingsService.Instance.OnSettingsChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_isInitialized)
                {
                    SyncUiFromSettings();
                    _ = UpdateLiveStrategyPreviewAsync();
                }
            });
        };

        AppSettingsService.Instance.OnAutomationStatusChanged += status =>
        {
            Dispatcher.Invoke(() =>
            {
                AutomationLiveStatusText.Text = $"• {status.ToUpperInvariant()}";
            });
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            await BuildDatabaseService.Instance.InitializeAsync();
            PopulateChampionCombos();
            _isInitialized = true;
        }

        SyncUiFromSettings();
        await UpdateLiveStrategyPreviewAsync();
    }

    private void PopulateChampionCombos()
    {
        _isSyncingUi = true;
        try
        {
            var champs = BuildDatabaseService.Instance.AllChampions;
            PrimaryPickCombo.ItemsSource = champs;
            BackupPickCombo.ItemsSource = champs;
            PrimaryBanCombo.ItemsSource = champs;
            BackupBanCombo.ItemsSource = champs;
            SimulatedEnemyCombo.ItemsSource = champs;
            AddMasteryChampCombo.ItemsSource = champs;
            if (champs.Count > 0)
                AddMasteryChampCombo.SelectedIndex = 0;
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    public void SyncUiFromSettings()
    {
        if (_isSyncingUi) return;
        _isSyncingUi = true;
        try
        {
            var s = AppSettingsService.Instance.Settings;

            // 1. Auto Accept Checkboxes, Sliders & TextBoxes
            AutoAcceptMasterSwitch.IsChecked = s.AutoAcceptEnabled;
            PartyLeaderOnlySwitch.IsChecked = s.AutoAcceptOnlyPartyLeader;
            PauseAfterDeclineSwitch.IsChecked = s.AutoAcceptPauseAfterDecline;
            AcceptDelaySlider.Value = s.AutoAcceptDelayMs;
            AcceptDelayValueText.Text = $"ACCEPT LATENCY SLIDER: {s.AutoAcceptDelayMs} MS";
            if (!AcceptDelayTextBox.IsKeyboardFocused)
                AcceptDelayTextBox.Text = s.AutoAcceptDelayMs.ToString();

            AcceptJitterSlider.Value = s.AutoAcceptRandomJitterMs;
            if (!AcceptJitterTextBox.IsKeyboardFocused)
                AcceptJitterTextBox.Text = s.AutoAcceptRandomJitterMs.ToString();

            QueueSoloCheck.IsChecked = s.AutoAcceptRankedSolo;
            QueueFlexCheck.IsChecked = s.AutoAcceptRankedFlex;
            QueueNormalCheck.IsChecked = s.AutoAcceptNormalDraft;
            QueueAramCheck.IsChecked = s.AutoAcceptAramAndModes;

            // 2. Smart Loadout & QoL
            AutoRunesSwitch.IsChecked = s.AutoExportRunesOnLock;
            AutoSpellsSwitch.IsChecked = s.AutoExportSpellsOnLock;
            AutoItemsSwitch.IsChecked = s.AutoExportItemsOnLock;
            AutoMatchupBuildSwitch.IsChecked = s.AutoSwitchToMatchupBuild;
            AutoOpenBuildSwitch.IsChecked = s.AutoOpenBuildOnChampSelect;

            bool flashOnD = string.Equals(s.FlashKeySlot, "D", StringComparison.OrdinalIgnoreCase);
            FlashKeyDBtn.BorderBrush = BrushFromHex(flashOnD ? "#2CE5B5" : "#3D4663");
            FlashKeyDBtn.Foreground = flashOnD ? BrushFromHex("#2CE5B5") : BrushFromHex("#9BA3BA");

            FlashKeyFBtn.BorderBrush = BrushFromHex(!flashOnD ? "#2CE5B5" : "#3D4663");
            FlashKeyFBtn.Foreground = !flashOnD ? BrushFromHex("#2CE5B5") : BrushFromHex("#9BA3BA");

            // 3. Auto Champ Select (Pick)
            AutoChampSelectMasterSwitch.IsChecked = s.AutoChampSelectEnabled;
            AutoHoverPickSwitch.IsChecked = s.AutoHoverPick;
            AutoLockInPickSwitch.IsChecked = s.AutoLockInPick;
            PickDelaySlider.Value = s.PickLockInDelayMs;
            PickDelayValueText.Text = $"PICK LOCK-IN DELAY: {s.PickLockInDelayMs} MS";
            if (!PickDelayTextBox.IsKeyboardFocused)
                PickDelayTextBox.Text = s.PickLockInDelayMs.ToString();

            SelectComboByTag(PickStrategyCombo, s.PickStrategy);
            PickStrategyHintText.Text = s.PickStrategy switch
            {
                "BEST_COUNTER" => "AUTOMATICALLY PICKS THE #1 STATISTICAL COUNTER AGAINST YOUR ENEMY LANER",
                "MASTERY_COUNTER" => "PICKS THE BEST COUNTER TO YOUR ENEMY LANER FILTERED BY YOUR CHAMPION MASTERY POOL",
                _ => "ALWAYS PICKS YOUR SELECTED PRIMARY CHAMPION (OR BACKUP IF BANNED/TAKEN)"
            };

            SelectComboByTag(PreferredRoleCombo, s.PreferredRole);

            PrimaryPickCombo.SelectedValue = s.PreferredPickChampId;
            BackupPickCombo.SelectedValue = s.BackupPickChampId;
            SimulatedEnemyCombo.SelectedValue = s.SimulatedEnemyChampId;

            SetChampImage(PrimaryPickChampImg, s.PreferredPickChampId);
            SetChampImage(BackupPickChampImg, s.BackupPickChampId);
            SetChampImage(SimulatedEnemyImg, s.SimulatedEnemyChampId);

            // Mastery Config
            MinMasterySlider.Value = s.MinMasteryLevelForCounter;
            MasteryLevelBadgeText.Text = $"MINIMUM MASTERY LEVEL FOR COUNTER: {s.MinMasteryLevelForCounter}";
            RenderMasteryPoolIcons();

            // 4. Auto Ban
            AutoBanMasterSwitch.IsChecked = s.AutoBanEnabled;
            SelectComboByTag(BanStrategyCombo, s.BanStrategy);
            BanStrategyHintText.Text = s.BanStrategy switch
            {
                "SPECIFIC" => "ALWAYS BANS YOUR SELECTED SPECIFIC BAN TARGET (OR BACKUP BAN IF ALLY HOVERS IT)",
                "COUNTER_OF_COUNTER" => "BANS THE BIGGEST COUNTER TO YOUR COUNTERPICK (2ND-ORDER PROTECTION)",
                _ => "CALCULATES AND BANS THE #1 BIGGEST COUNTER TO THE CHAMPION YOU WANT TO PLAY"
            };

            PrimaryBanCombo.SelectedValue = s.PreferredBanChampId;
            BackupBanCombo.SelectedValue = s.BackupBanChampId;
            SetChampImage(PrimaryBanChampImg, s.PreferredBanChampId);
            SetChampImage(BackupBanChampImg, s.BackupBanChampId);

            ProtectAllyHoverSwitch.IsChecked = s.ProtectTeammateHover;
            BanDelaySlider.Value = s.BanDelayMs;
            BanDelayValueText.Text = $"BAN LOCK-IN DELAY: {s.BanDelayMs} MS";
            if (!BanDelayTextBox.IsKeyboardFocused)
                BanDelayTextBox.Text = s.BanDelayMs.ToString();

            // Suggestions Config
            PersonalizedModeSwitch.IsChecked = s.PickSuggestionPersonalizedMode;
            AutoDetectLaneInSuggestionsSwitch.IsChecked = s.AutoDetectLaneInSuggestions;
            if (!MinMatchesTextBox.IsKeyboardFocused)
                MinMatchesTextBox.Text = s.PickSuggestionMinMatches.ToString();
            MinMasteryLevelSlider.Value = s.PickSuggestionMinMasteryLevel;
            MinMasteryLevelLabel.Text = $"MINIMUM MASTERY LEVEL REQUIRED FOR PRIORITY: LEVEL {s.PickSuggestionMinMasteryLevel} (M{s.PickSuggestionMinMasteryLevel}+)";

            AutomationLiveStatusText.Text = $"• {AppSettingsService.Instance.LastAutomationStatus.ToUpperInvariant()}";
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private static void SelectComboByTag(ComboBox combo, string tagValue)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
            combo.SelectedIndex = 0;
    }

    // ==================== LEFT SIDEBAR SECTION FILTERING ====================

    private void SidebarNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn || clickedBtn.Tag is not string category)
            return;

        var navPairs = new (Border Bar, Button Btn)[]
        {
            (NavBarAllAutomation, NavBtnAllAutomation),
            (NavBarAutoAccept, NavBtnAutoAccept),
            (NavBarChampSelect, NavBtnChampSelect),
            (NavBarBanEngine, NavBtnBanEngine),
            (NavBarMastery, NavBtnMastery),
            (NavBarLoadout, NavBtnLoadout),
            (NavBarSuggestions, NavBtnSuggestions),
            (NavBarPreview, NavBtnPreview)
        };

        foreach (var (bar, btn) in navPairs)
        {
            bool isSelected = btn == clickedBtn;
            bar.Background = isSelected ? BrushFromHex("#2CE5B5") : Brushes.Transparent;
            btn.Foreground = isSelected ? Brushes.White : BrushFromHex("#8E92A4");
        }

        switch (category)
        {
            case "AUTO_ACCEPT":
                HeaderCategoryTitleText.Text = "AUTOMATION  /  AUTO ACCEPT";
                SectionAutoAccept.Visibility = Visibility.Visible;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Collapsed;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Collapsed;
                break;

            case "AUTO_CHAMP":
                HeaderCategoryTitleText.Text = "AUTOMATION  /  AUTO CHAMP & COUNTERS";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Visible;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Visible;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Visible;
                break;

            case "AUTO_BAN":
                HeaderCategoryTitleText.Text = "AUTOMATION  /  AUTO BAN & COUNTERS";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Visible;
                SectionMastery.Visibility = Visibility.Collapsed;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Visible;
                break;

            case "MASTERY_POOL":
                HeaderCategoryTitleText.Text = "MASTERY COUNTER POOL";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Visible;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Visible;
                break;

            case "LOADOUT":
                HeaderCategoryTitleText.Text = "RUNES, SPELLS & ITEMS";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Collapsed;
                SectionLoadout.Visibility = Visibility.Visible;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Collapsed;
                break;

            case "SUGGESTIONS":
                HeaderCategoryTitleText.Text = "RECOMMENDATIONS  /  SUGGESTIONS CONFIG";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Collapsed;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Visible;
                SectionSimulator.Visibility = Visibility.Collapsed;
                break;

            case "SIMULATOR":
                HeaderCategoryTitleText.Text = "COUNTER SIMULATOR";
                SectionAutoAccept.Visibility = Visibility.Collapsed;
                SectionChampSelect.Visibility = Visibility.Collapsed;
                SectionBanEngine.Visibility = Visibility.Collapsed;
                SectionMastery.Visibility = Visibility.Collapsed;
                SectionLoadout.Visibility = Visibility.Collapsed;
                SectionSuggestions.Visibility = Visibility.Collapsed;
                SectionSimulator.Visibility = Visibility.Visible;
                break;

            default: // ALL_AUTOMATION
                HeaderCategoryTitleText.Text = "AUTOMATION";
                SectionAutoAccept.Visibility = Visibility.Visible;
                SectionChampSelect.Visibility = Visibility.Visible;
                SectionBanEngine.Visibility = Visibility.Visible;
                SectionMastery.Visibility = Visibility.Visible;
                SectionLoadout.Visibility = Visibility.Visible;
                SectionSuggestions.Visibility = Visibility.Visible;
                SectionSimulator.Visibility = Visibility.Visible;
                break;
        }

        SettingsScrollViewer.ScrollToTop();
    }

    private void RenderMasteryPoolIcons()
    {
        MasteryPoolWrapPanel.Children.Clear();
        var s = AppSettingsService.Instance.Settings;

        foreach (int champId in s.MasteryPoolChampIds.ToList())
        {
            var info = BuildDatabaseService.Instance.GetChampionInfo(champId);
            if (info == null) continue;

            var tile = new Border
            {
                Width = 42,
                Height = 42,
                BorderBrush = BrushFromHex("#2CE5B5"),
                BorderThickness = new Thickness(1),
                Background = BrushFromHex("#030508"),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                ToolTip = $"{info.Name} — Click to remove from Mastery Counter Pool"
            };

            var img = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            SetImageSourceSafe(img, info.IconUrl);
            tile.Child = img;

            int targetId = champId;
            tile.MouseLeftButtonUp += (sender, args) =>
            {
                if (s.MasteryPoolChampIds.Count > 1)
                {
                    s.MasteryPoolChampIds.Remove(targetId);
                    AppSettingsService.Instance.SaveAndNotify();
                }
            };

            MasteryPoolWrapPanel.Children.Add(tile);
        }
    }

    private async Task UpdateLiveStrategyPreviewAsync()
    {
        var preview = await AppSettingsService.Instance.ComputePreviewAsync();

        PreviewPickNameText.Text = preview.PickChampionName.ToUpperInvariant();
        PreviewPickReasonText.Text = preview.PickReasonLabel;
        SetImageSourceSafe(PreviewPickImg, preview.PickChampionIconUrl);
        SetImageSourceSafe(PreviewBackupPickImg, preview.BackupPickChampionIconUrl);

        if (AppSettingsService.Instance.Settings.AutoBanEnabled)
        {
            PreviewBanNameText.Text = preview.BanChampionName.ToUpperInvariant();
            PreviewBanReasonText.Text = preview.BanReasonLabel;
            SetImageSourceSafe(PreviewBanImg, preview.BanChampionIconUrl);
            SetImageSourceSafe(PreviewBackupBanImg, preview.SecondaryBanChampionIconUrl);
        }
        else
        {
            PreviewBanNameText.Text = "DISABLED";
            PreviewBanReasonText.Text = "Auto Ban is currently turned OFF";
            PreviewBanImg.Source = null;
            PreviewBackupBanImg.Source = null;
        }
    }

    // ==================== EVENT HANDLERS ====================

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        OnBackRequested?.Invoke();
    }

    private void ResetDefaultsBtn_Click(object sender, RoutedEventArgs e)
    {
        var fresh = new AppSettings();
        var s = AppSettingsService.Instance.Settings;
        s.AutoAcceptEnabled = fresh.AutoAcceptEnabled;
        s.AutoAcceptOnlyPartyLeader = fresh.AutoAcceptOnlyPartyLeader;
        s.AutoAcceptDelayMs = fresh.AutoAcceptDelayMs;
        s.AutoAcceptRandomJitterMs = fresh.AutoAcceptRandomJitterMs;
        s.AutoAcceptPauseAfterDecline = fresh.AutoAcceptPauseAfterDecline;
        s.AutoAcceptRankedSolo = fresh.AutoAcceptRankedSolo;
        s.AutoAcceptRankedFlex = fresh.AutoAcceptRankedFlex;
        s.AutoAcceptNormalDraft = fresh.AutoAcceptNormalDraft;
        s.AutoAcceptAramAndModes = fresh.AutoAcceptAramAndModes;

        s.AutoChampSelectEnabled = fresh.AutoChampSelectEnabled;
        s.PickStrategy = fresh.PickStrategy;
        s.PreferredRole = fresh.PreferredRole;
        s.PreferredPickChampId = fresh.PreferredPickChampId;
        s.BackupPickChampId = fresh.BackupPickChampId;
        s.MinMasteryLevelForCounter = fresh.MinMasteryLevelForCounter;
        s.MasteryPoolChampIds = fresh.MasteryPoolChampIds.ToList();
        s.AutoHoverPick = fresh.AutoHoverPick;
        s.AutoLockInPick = fresh.AutoLockInPick;
        s.PickLockInDelayMs = fresh.PickLockInDelayMs;

        s.AutoBanEnabled = fresh.AutoBanEnabled;
        s.BanStrategy = fresh.BanStrategy;
        s.PreferredBanChampId = fresh.PreferredBanChampId;
        s.BackupBanChampId = fresh.BackupBanChampId;
        s.ProtectTeammateHover = fresh.ProtectTeammateHover;
        s.BanDelayMs = fresh.BanDelayMs;

        s.AutoExportRunesOnLock = fresh.AutoExportRunesOnLock;
        s.AutoExportSpellsOnLock = fresh.AutoExportSpellsOnLock;
        s.FlashKeySlot = fresh.FlashKeySlot;
        s.AutoExportItemsOnLock = fresh.AutoExportItemsOnLock;
        s.AutoSwitchToMatchupBuild = fresh.AutoSwitchToMatchupBuild;
        s.AutoOpenBuildOnChampSelect = fresh.AutoOpenBuildOnChampSelect;

        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AutoAcceptMasterSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUi) return;
        AppSettingsService.Instance.SetAutoAccept(AutoAcceptMasterSwitch.IsChecked == true);
    }

    private void PersonalizedModeSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUi) return;
        AppSettingsService.Instance.Settings.PickSuggestionPersonalizedMode = PersonalizedModeSwitch.IsChecked == true;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AutoDetectLaneInSuggestionsSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUi) return;
        AppSettingsService.Instance.Settings.AutoDetectLaneInSuggestions = AutoDetectLaneInSuggestionsSwitch.IsChecked == true;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void MinMatchesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUi) return;
        if (int.TryParse(MinMatchesTextBox.Text, out int matches) && matches >= 100)
        {
            AppSettingsService.Instance.Settings.PickSuggestionMinMatches = matches;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void MinMasteryLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi) return;
        int lvl = (int)Math.Round(MinMasteryLevelSlider.Value);
        MinMasteryLevelLabel.Text = $"MINIMUM MASTERY LEVEL REQUIRED FOR PRIORITY: LEVEL {lvl} (M{lvl}+)";
        AppSettingsService.Instance.Settings.PickSuggestionMinMasteryLevel = lvl;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AutoChampSelectMasterSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUi) return;
        AppSettingsService.Instance.SetAutoChampSelect(AutoChampSelectMasterSwitch.IsChecked == true);
    }

    private void AnyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUi) return;
        var s = AppSettingsService.Instance.Settings;
        s.AutoAcceptOnlyPartyLeader = PartyLeaderOnlySwitch.IsChecked == true;
        s.AutoAcceptPauseAfterDecline = PauseAfterDeclineSwitch.IsChecked == true;
        s.AutoAcceptRankedSolo = QueueSoloCheck.IsChecked == true;
        s.AutoAcceptRankedFlex = QueueFlexCheck.IsChecked == true;
        s.AutoAcceptNormalDraft = QueueNormalCheck.IsChecked == true;
        s.AutoAcceptAramAndModes = QueueAramCheck.IsChecked == true;

        s.AutoExportRunesOnLock = AutoRunesSwitch.IsChecked == true;
        s.AutoExportSpellsOnLock = AutoSpellsSwitch.IsChecked == true;
        s.AutoExportItemsOnLock = AutoItemsSwitch.IsChecked == true;
        s.AutoSwitchToMatchupBuild = AutoMatchupBuildSwitch.IsChecked == true;
        s.AutoOpenBuildOnChampSelect = AutoOpenBuildSwitch.IsChecked == true;

        s.AutoHoverPick = AutoHoverPickSwitch.IsChecked == true;
        s.AutoLockInPick = AutoLockInPickSwitch.IsChecked == true;
        s.AutoBanEnabled = AutoBanMasterSwitch.IsChecked == true;
        s.ProtectTeammateHover = ProtectAllyHoverSwitch.IsChecked == true;

        AppSettingsService.Instance.SaveAndNotify();
    }

    // ==================== SLIDER & TEXTBOX SYNCHRONIZED HANDLERS ====================

    private void AcceptDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || AcceptDelayValueText == null) return;
        int val = (int)Math.Round(AcceptDelaySlider.Value);
        AcceptDelayValueText.Text = $"ACCEPT LATENCY SLIDER: {val} MS";
        AppSettingsService.Instance.Settings.AutoAcceptDelayMs = val;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AcceptDelayTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUi || AcceptDelaySlider == null) return;
        if (int.TryParse(AcceptDelayTextBox.Text.Trim(), out int val))
        {
            val = Math.Clamp(val, 0, 8000);
            AppSettingsService.Instance.Settings.AutoAcceptDelayMs = val;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void AcceptJitterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi) return;
        int val = (int)Math.Round(AcceptJitterSlider.Value);
        AppSettingsService.Instance.Settings.AutoAcceptRandomJitterMs = val;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AcceptJitterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUi) return;
        if (int.TryParse(AcceptJitterTextBox.Text.Trim(), out int val))
        {
            val = Math.Clamp(val, 0, 1000);
            AppSettingsService.Instance.Settings.AutoAcceptRandomJitterMs = val;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void PickDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || PickDelayValueText == null) return;
        int val = (int)Math.Round(PickDelaySlider.Value);
        PickDelayValueText.Text = $"PICK LOCK-IN DELAY: {val} MS";
        AppSettingsService.Instance.Settings.PickLockInDelayMs = val;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void PickDelayTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUi || PickDelaySlider == null) return;
        if (int.TryParse(PickDelayTextBox.Text.Trim(), out int val))
        {
            val = Math.Clamp(val, 0, 8000);
            AppSettingsService.Instance.Settings.PickLockInDelayMs = val;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void BanDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || BanDelayValueText == null) return;
        int val = (int)Math.Round(BanDelaySlider.Value);
        BanDelayValueText.Text = $"BAN LOCK-IN DELAY: {val} MS";
        AppSettingsService.Instance.Settings.BanDelayMs = val;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void BanDelayTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUi || BanDelaySlider == null) return;
        if (int.TryParse(BanDelayTextBox.Text.Trim(), out int val))
        {
            val = Math.Clamp(val, 0, 6000);
            AppSettingsService.Instance.Settings.BanDelayMs = val;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void FlashKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            AppSettingsService.Instance.Settings.FlashKeySlot = key;
            AppSettingsService.Instance.SaveAndNotify();
        }
    }

    private void PickStrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || PickStrategyCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string strat)
            return;
        AppSettingsService.Instance.Settings.PickStrategy = strat;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void PreferredRoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || PreferredRoleCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string role)
            return;
        AppSettingsService.Instance.Settings.PreferredRole = role;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void BanStrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || BanStrategyCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string strat)
            return;
        AppSettingsService.Instance.Settings.BanStrategy = strat;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void PrimaryPickCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || PrimaryPickCombo.SelectedValue is not int id) return;
        AppSettingsService.Instance.Settings.PreferredPickChampId = id;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void BackupPickCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || BackupPickCombo.SelectedValue is not int id) return;
        AppSettingsService.Instance.Settings.BackupPickChampId = id;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void PrimaryBanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || PrimaryBanCombo.SelectedValue is not int id) return;
        AppSettingsService.Instance.Settings.PreferredBanChampId = id;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void BackupBanCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || BackupBanCombo.SelectedValue is not int id) return;
        AppSettingsService.Instance.Settings.BackupBanChampId = id;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void SimulatedEnemyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingUi || SimulatedEnemyCombo.SelectedValue is not int id) return;
        AppSettingsService.Instance.Settings.SimulatedEnemyChampId = id;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void MinMasterySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || MasteryLevelBadgeText == null) return;
        int lvl = (int)Math.Round(MinMasterySlider.Value);
        MasteryLevelBadgeText.Text = $"MINIMUM MASTERY LEVEL FOR COUNTER: {lvl}";
        AppSettingsService.Instance.Settings.MinMasteryLevelForCounter = lvl;
        AppSettingsService.Instance.SaveAndNotify();
    }

    private void AddMasteryChampBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AddMasteryChampCombo.SelectedValue is int id && id > 0)
        {
            var pool = AppSettingsService.Instance.Settings.MasteryPoolChampIds;
            if (!pool.Contains(id))
            {
                pool.Insert(0, id);
                AppSettingsService.Instance.SaveAndNotify();
            }
        }
    }

    private async void SyncLcuMasteryBtn_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await AppSettingsService.Instance.SyncMasteryPoolFromLcuAsync();
        if (ok)
        {
            AutomationLiveStatusText.Text = "• SYNCED TOP MASTERY POOL FROM LEAGUE CLIENT";
        }
        else
        {
            AutomationLiveStatusText.Text = "• LCU NOT CONNECTED — USING CUSTOM MASTERY POOL";
        }
    }

    // ==================== UI HELPERS ====================

    private static void SetChampImage(Image img, int champId)
    {
        var info = BuildDatabaseService.Instance.GetChampionInfo(champId);
        SetImageSourceSafe(img, info?.IconUrl ?? "");
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
