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

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize Views
        _dashboardView = new DashboardView();
        _champSelectView = new ChampSelectView();

        // Set initial view to Champion Builds (Blitz UI)
        MainContentArea.Content = _champSelectView;
        
        _watcher = new LockfileWatcher();
        
        _watcher.OnClientConnected += OnLeagueConnected;
        _watcher.OnClientDisconnected += OnLeagueDisconnected;
        
        _watcher.Start();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        NavDashboardBtn.Background = Brushes.Transparent;
        NavDashboardBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E92A4"));
        NavChampSelectBtn.Background = Brushes.Transparent;
        NavChampSelectBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E92A4"));

        var clickedBtn = (Button)sender;
        clickedBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252836"));
        clickedBtn.Foreground = Brushes.White;

        if (clickedBtn == NavDashboardBtn) MainContentArea.Content = _dashboardView;
        else if (clickedBtn == NavChampSelectBtn) MainContentArea.Content = _champSelectView;
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
