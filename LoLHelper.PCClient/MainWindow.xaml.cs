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
    private OverlayWindow? _overlayWindow;

    // Views
    private readonly DashboardView _dashboardView;
    private readonly ScoutingView _scoutingView;
    private readonly ChampSelectView _champSelectView;
    private readonly PostMatchView _postMatchView;
    private readonly MobilePairingView _mobilePairingView;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize Views
        _dashboardView = new DashboardView();
        _scoutingView = new ScoutingView();
        _champSelectView = new ChampSelectView();
        _postMatchView = new PostMatchView();
        _mobilePairingView = new MobilePairingView();
        _settingsView = new SettingsView();

        // Set initial view
        MainContentArea.Content = _dashboardView;
        
        _watcher = new LockfileWatcher();
        
        _watcher.OnClientConnected += OnLeagueConnected;
        _watcher.OnClientDisconnected += OnLeagueDisconnected;
        
        _watcher.Start();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        // Reset all buttons to inactive style

        NavDashboardBtn.Background = Brushes.Transparent;
        NavDashboardBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));
        NavChampSelectBtn.Background = Brushes.Transparent;
        NavChampSelectBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));
        NavScoutingBtn.Background = Brushes.Transparent;
        NavScoutingBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));
        NavPostMatchBtn.Background = Brushes.Transparent;
        NavPostMatchBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));
        NavMobileBtn.Background = Brushes.Transparent;
        NavMobileBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));
        NavSettingsBtn.Background = Brushes.Transparent;
        NavSettingsBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3"));

        // Highlight clicked button and switch view
        var clickedBtn = (Button)sender;
        clickedBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D35"));
        clickedBtn.Foreground = Brushes.White;

        if (clickedBtn == NavDashboardBtn) MainContentArea.Content = _dashboardView;
        else if (clickedBtn == NavChampSelectBtn) MainContentArea.Content = _champSelectView;
        else if (clickedBtn == NavScoutingBtn) MainContentArea.Content = _scoutingView;
        else if (clickedBtn == NavPostMatchBtn) MainContentArea.Content = _postMatchView;
        else if (clickedBtn == NavMobileBtn) MainContentArea.Content = _mobilePairingView;
        else if (clickedBtn == NavSettingsBtn) MainContentArea.Content = _settingsView;
    }

    private async void OnLeagueConnected(LcuCredentials credentials)
    {
        try
        {
            LcuApiClient.Instance.SetCredentials(credentials);
            
            Dispatcher.Invoke(() => 
            {
                _dashboardView.UpdateConnection("Connected to League Client!", Brushes.LightGreen);
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
                        if (doc.RootElement.TryGetProperty("displayName", out var displayNameProp))
                        {
                            _dashboardView.UpdateSummoner(displayNameProp.GetString() ?? "Unknown");
                        }
                        else if (doc.RootElement.TryGetProperty("gameName", out var gameNameProp))
                        {
                            _dashboardView.UpdateSummoner(gameNameProp.GetString() ?? "Unknown");
                        }
                        else
                        {
                            _dashboardView.UpdateSummoner("Name not found in JSON");
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
        catch (Exception fatalEx)
        {
            Dispatcher.Invoke(() => 
            {
                MessageBox.Show("Fatal error during connection sequence:\n" + fatalEx.ToString(), "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    private void OnLeagueDisconnected()
    {
        Dispatcher.Invoke(() => 
        {
            _dashboardView.UpdateConnection("Waiting for League of Legends...", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3")));
            _dashboardView.UpdateSummoner("Not Available");
            
            _overlayWindow?.Close();
            _overlayWindow = null;
        });
    }

    private void ShowOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null || !_overlayWindow.IsLoaded)
        {
            _overlayWindow = new OverlayWindow();
            _overlayWindow.Show();
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _watcher.Stop();
        _overlayWindow?.Close();
        base.OnClosed(e);
    }
}