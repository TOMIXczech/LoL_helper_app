using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LoLHelper.Core.Services;
using LoLHelper.PCClient.Views;

namespace LoLHelper.PCClient;

public partial class MainWindow : Window
{
    private readonly LockfileWatcher _watcher;

    // Views
    private readonly DashboardView _dashboardView;
    private readonly ChampSelectView _champSelectView;
    private readonly SettingsView _settingsView;
    private readonly PickSuggestionsView _pickSuggestionsView;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize Views
        _dashboardView = new DashboardView();
        _champSelectView = new ChampSelectView();
        _settingsView = new SettingsView();
        _pickSuggestionsView = new PickSuggestionsView();

        // Allow clicking any match or champion on the Starting Screen to jump straight to its Build
        _dashboardView.OnOpenChampionBuildRequested += async (champId, role) =>
        {
            SwitchToView(NavChampSelectBtn, _champSelectView);
            await _champSelectView.OpenChampionBuildAsync(champId, role);
        };

        // Wire Pick/Ban suggestions -> Champion Build
        _pickSuggestionsView.OnOpenChampionBuildRequested += async (champKey, role) =>
        {
            SwitchToView(NavChampSelectBtn, _champSelectView);
            await _champSelectView.OpenChampionBuildAsync(champKey, role);
        };

        // Wire Home/Suggestions -> Settings and Settings -> Back
        _dashboardView.OnOpenSettingsRequested += () =>
        {
            SwitchToView(NavSettingsBtn, _settingsView);
        };

        _pickSuggestionsView.OnOpenSettingsRequested += () =>
        {
            SwitchToView(NavSettingsBtn, _settingsView);
        };

        _settingsView.OnBackRequested += () =>
        {
            SwitchToView(NavDashboardBtn, _dashboardView);
        };

        // Wire AutoOpenBuildOnChampSelect from LCU Automation
        AppSettingsService.Instance.OnAutoOpenChampionBuild += (champId, role) =>
        {
            Dispatcher.Invoke(async () =>
            {
                SwitchToView(NavChampSelectBtn, _champSelectView);
                await _champSelectView.OpenChampionBuildAsync(champId, role);
            });
        };

        // Keep top-bar status pills synchronized with AppSettingsService
        AppSettingsService.Instance.OnSettingsChanged += () =>
        {
            Dispatcher.Invoke(SyncTopBarAutomationPills);
        };
        SyncTopBarAutomationPills();

        // Set initial starting screen to Home & Match History Dashboard
        NavSettingsBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B1E2B"));
        NavSettingsBtn.Foreground = Brushes.White;
        MainContentArea.Content = _dashboardView;
        
        _watcher = new LockfileWatcher();
        
        _watcher.OnClientConnected += OnLeagueConnected;
        _watcher.OnClientDisconnected += OnLeagueDisconnected;
        
        _watcher.Start();
    }

    private void SyncTopBarAutomationPills()
    {
        var s = AppSettingsService.Instance.Settings;
        TopAutoAcceptStateText.Text = s.AutoAcceptEnabled ? $"ON ({s.AutoAcceptDelayMs}ms)" : "OFF";
        TopAutoAcceptStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.AutoAcceptEnabled ? "#2CE5B5" : "#8E92A4"));

        TopAutoSelectStateText.Text = s.AutoChampSelectEnabled ? $"ON ({AppSettingsService.Instance.FormatPickStrategyShort()})" : "OFF";
        TopAutoSelectStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.AutoChampSelectEnabled ? "#5383E8" : "#8E92A4"));
    }

    private void TopAutoAcceptPillBtn_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.Instance.SetAutoAccept(!AppSettingsService.Instance.Settings.AutoAcceptEnabled);
    }

    private void TopAutoSelectPillBtn_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.Instance.SetAutoChampSelect(!AppSettingsService.Instance.Settings.AutoChampSelectEnabled);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var clickedBtn = (Button)sender;
        if (clickedBtn == NavDashboardBtn)
            SwitchToView(NavDashboardBtn, _dashboardView);
        else if (clickedBtn == NavChampSelectBtn)
            SwitchToView(NavChampSelectBtn, _champSelectView);
        else if (clickedBtn == NavSuggestionsBtn)
            SwitchToView(NavSuggestionsBtn, _pickSuggestionsView);
        else if (clickedBtn == NavSettingsBtn)
            SwitchToView(NavSettingsBtn, _settingsView);
    }

    private void SwitchToView(Button activeBtn, UserControl view)
    {
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E92A4"));
        NavDashboardBtn.Background = Brushes.Transparent;
        NavDashboardBtn.Foreground = inactiveFg;
        NavChampSelectBtn.Background = Brushes.Transparent;
        NavChampSelectBtn.Foreground = inactiveFg;
        NavSuggestionsBtn.Background = Brushes.Transparent;
        NavSuggestionsBtn.Foreground = inactiveFg;
        NavSettingsBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B1E2B"));
        NavSettingsBtn.Foreground = Brushes.White;

        activeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252836"));
        activeBtn.Foreground = Brushes.White;
        MainContentArea.Content = view;
    }

    private async void OnLeagueConnected(LcuCredentials credentials)
    {
        try
        {
            LcuApiClient.Instance.SetCredentials(credentials);
            
            Dispatcher.Invoke(() => 
            {
                _dashboardView.UpdateConnection("Connected to League Client!", Brushes.LightGreen);
                LcuStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2CE5B5"));
                LcuStatusLabel.Text = "LCU Connected";
                LcuStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2CE5B5"));
                _ = _champSelectView.AutoInjectBuildAsync();
            });

            try
            {
                var json = await LcuApiClient.Instance.GetCurrentSummonerAsync();
                
                Dispatcher.Invoke(() => 
                {
                    _dashboardView.UpdateLogs(json);
                    
                    try 
                    {
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("displayName", out var displayNameProp) && !string.IsNullOrEmpty(displayNameProp.GetString()))
                        {
                            string name = displayNameProp.GetString()!;
                            _dashboardView.UpdateSummoner(name);
                            LcuStatusLabel.Text = $"LCU: {name}";
                        }
                        else if (doc.RootElement.TryGetProperty("gameName", out var gameNameProp))
                        {
                            string name = gameNameProp.GetString() ?? "Connected";
                            _dashboardView.UpdateSummoner(name);
                            LcuStatusLabel.Text = $"LCU: {name}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _dashboardView.UpdateSummoner("Parse error: " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => 
                {
                    _dashboardView.UpdateSummoner("API Error");
                    _dashboardView.UpdateLogs(ex.Message);
                });
            }
        }
        catch { }
    }

    private void OnLeagueDisconnected()
    {
        Dispatcher.Invoke(() => 
        {
            var gray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E92A4"));
            _dashboardView.UpdateConnection("Waiting for League of Legends...", gray);
            _dashboardView.UpdateSummoner("Not Available");
            LcuStatusDot.Fill = gray;
            LcuStatusLabel.Text = "Waiting for League Client...";
            LcuStatusLabel.Foreground = gray;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher.Stop();
        base.OnClosed(e);
    }
}
