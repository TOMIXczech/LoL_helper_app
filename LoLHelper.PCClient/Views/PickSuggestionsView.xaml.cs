using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public partial class PickSuggestionsView : UserControl
{
    public event Action<string, string>? OnOpenChampionBuildRequested;
    public event Action? OnOpenSettingsRequested;

    private string _currentRole = "ALL";
    private bool _isBanMode = false;
    private bool _isPersonalizedMode = true;
    private int? _prepickedChampId = null;
    private bool _isLoading = false;
    private bool _isInitialized = false;

    public PickSuggestionsView()
    {
        InitializeComponent();

        _isPersonalizedMode = AppSettingsService.Instance.Settings.PickSuggestionPersonalizedMode;
        UpdatePersonalizedBtnUi();

        AppSettingsService.Instance.OnChampSelectStateUpdated += OnChampSelectStateUpdated;
        AppSettingsService.Instance.OnSettingsChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_isInitialized)
                {
                    _isPersonalizedMode = AppSettingsService.Instance.Settings.PickSuggestionPersonalizedMode;
                    UpdatePersonalizedBtnUi();
                }
            });
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            await SuggestionService.Instance.InitializeAsync();
            PopulatePrepickCombo();
            _isInitialized = true;
        }

        UpdateModeUi();
        UpdateRolePillStyles();
        await RefreshSuggestionsAsync();
    }

    private void PopulatePrepickCombo()
    {
        var champs = BuildDatabaseService.Instance.AllChampions.OrderBy(c => c.Name).ToList();
        
        // Add "Live Auto-Detect" at index 0 (clean, compact text that fits beautifully)
        var list = new List<ChampionInfo>
        {
            new ChampionInfo { Id = 0, Key = "All", Name = "🌐 Live Auto-Detect (LCU)", IconUrl = "" }
        };
        list.AddRange(champs);

        PrepickChampCombo.ItemsSource = list;
        PrepickChampCombo.SelectedIndex = 0;
        UpdatePrepickUi();
    }

    private void OnChampSelectStateUpdated(int prepickedChampId, string assignedRole)
    {
        Dispatcher.Invoke(() =>
        {
            if (prepickedChampId > 0 || (!string.IsNullOrEmpty(assignedRole) && assignedRole != "NONE"))
            {
                LcuLiveStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x2C, 0xE5, 0xB5));
                LcuLiveStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));
                LcuLiveStatusText.Text = "● LIVE CHAMP SELECT";

                // Auto-detect role if assigned
                if (!string.IsNullOrEmpty(assignedRole) && assignedRole != "NONE")
                {
                    string detected = assignedRole.ToUpperInvariant() switch
                    {
                        "TOP" => "TOP",
                        "JUNGLE" => "JUNGLE",
                        "MIDDLE" or "MID" => "MIDDLE",
                        "BOTTOM" or "BOT" => "BOTTOM",
                        "UTILITY" or "SUPPORT" => "UTILITY",
                        _ => _currentRole
                    };
                    
                    if (detected != _currentRole && AppSettingsService.Instance.Settings.AutoDetectLaneInSuggestions)
                    {
                        _currentRole = detected;
                        UpdateRolePillStyles();
                    }
                }

                // In Ban mode: track prepick intent
                if (_isBanMode && prepickedChampId > 0 && _prepickedChampId != prepickedChampId)
                {
                    _prepickedChampId = prepickedChampId;
                    SelectPrepickComboById(_prepickedChampId.Value);
                    UpdatePrepickUi();
                    _ = RefreshSuggestionsAsync();
                }
            }
            else
            {
                LcuLiveStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x13, 0x2F, 0x2C));
                LcuLiveStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));
                LcuLiveStatusText.Text = "● Blitz Meta (10k+ Games)";
            }
        });
    }

    public async Task RefreshSuggestionsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        SuggestionsListStack.Children.Clear();

        // Loading Indicator
        var loadingText = new TextBlock
        {
            Text = "Analyzing Blitz.gg Overview & Counter Matchup Statistics...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4)),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 40)
        };
        SuggestionsListStack.Children.Add(loadingText);

        try
        {
            List<ChampionSuggestionItem> items;
            if (_isBanMode)
            {
                // Ban suggestions: Karthus BOT style matchup counters vs prepick or 10k+ threats if no prepick
                int? targetChamp = (_prepickedChampId > 0) ? _prepickedChampId : null;
                items = await SuggestionService.Instance.GetBanSuggestionsAsync(targetChamp, _currentRole);
            }
            else
            {
                // Pick suggestions:
                // If enemy laner is specified in combo or detected: get counter pick suggestions!
                // If no enemy laner: get general pick suggestions (10k+ games, top 30 meta, personalized mastery injection)
                int? enemyLaner = (_prepickedChampId > 0) ? _prepickedChampId : null;
                items = await SuggestionService.Instance.GetCounterPickSuggestionsAsync(enemyLaner, _currentRole, _isPersonalizedMode);
            }

            // STRICT REQUIREMENT: NEVER SUGGEST MORE THAN 10 CHAMPIONS
            var top10 = items.Take(10).ToList();

            SuggestionsListStack.Children.Clear();

            if (top10.Count == 0)
            {
                SuggestionsListStack.Children.Add(new TextBlock
                {
                    Text = "No champions found matching the 10,000+ matches threshold for this role.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xAC, 0xC4)),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 30, 0, 30)
                });
                return;
            }

            for (int i = 0; i < top10.Count; i++)
            {
                top10[i].Rank = i + 1; // Enforce exact rank 1-10
                var card = CreateSuggestionCard(top10[i]);
                SuggestionsListStack.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            SuggestionsListStack.Children.Clear();
            SuggestionsListStack.Children.Add(new TextBlock
            {
                Text = $"Error loading recommendations: {ex.Message}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                FontSize = 12,
                Margin = new Thickness(20)
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private UIElement CreateSuggestionCard(ChampionSuggestionItem item)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(item.Rank == 1 
                ? Color.FromArgb(0x28, 0x2C, 0xE5, 0xB5) 
                : (item.IsPersonalizedMastery ? Color.FromArgb(0x24, 0xF0, 0xB2, 0x32) : Color.FromArgb(0x18, 0x1E, 0x23, 0x32))),
            BorderBrush = new SolidColorBrush(item.Rank == 1 
                ? Color.FromRgb(0x2C, 0xE5, 0xB5) 
                : (item.IsPersonalizedMastery ? Color.FromRgb(0x8C, 0x68, 0x20) : Color.FromRgb(0x20, 0x24, 0x33))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });          // Rank
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });         // Champion & Role
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Reason & Badge
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });         // Win Rate
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });         // Pick Rate
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });         // Ban Rate
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });         // Games
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });         // Actions

        // 0. Rank Pill
        var rankBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = item.Rank switch
            {
                1 => new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5)),
                2 => new SolidColorBrush(Color.FromRgb(0x53, 0x83, 0xE8)),
                3 => new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4)),
                _ => new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x29))
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var rankText = new TextBlock
        {
            Text = $"#{item.Rank}",
            FontSize = 13,
            FontWeight = FontWeights.Black,
            Foreground = item.Rank <= 2 ? new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x0B)) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rankBorder.Child = rankText;
        Grid.SetColumn(rankBorder, 0);
        grid.Children.Add(rankBorder);

        // 1. Champion Icon + Name + Role Label
        var champStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var imgBorder = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(item.IsPersonalizedMastery ? Color.FromRgb(0xF0, 0xB2, 0x32) : Color.FromRgb(0x2C, 0xE5, 0xB5)),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var champImg = new Image
        {
            Stretch = Stretch.UniformToFill
        };
        RenderOptions.SetBitmapScalingMode(champImg, BitmapScalingMode.HighQuality);
        SetImageSourceSafe(champImg, item.IconUrl);
        imgBorder.Child = champImg;
        champStack.Children.Add(imgBorder);

        var nameRoleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Text = item.ChampionName,
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        var roleMetaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var roleBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x53, 0x83, 0xE8)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 6, 0)
        };
        roleBadge.Child = new TextBlock
        {
            Text = item.RoleLabel.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x76, 0xA2, 0xFF))
        };
        roleMetaStack.Children.Add(roleBadge);

        var tierBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x2C, 0xE5, 0xB5)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1)
        };
        tierBadge.Child = new TextBlock
        {
            Text = $"TIER {item.TierLabel}",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5))
        };
        roleMetaStack.Children.Add(tierBadge);

        nameRoleStack.Children.Add(nameText);
        nameRoleStack.Children.Add(roleMetaStack);
        champStack.Children.Add(nameRoleStack);
        Grid.SetColumn(champStack, 1);
        grid.Children.Add(champStack);

        // 2. Strategic Reason & Badges
        var reasonStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 16, 0)
        };
        var badgeBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0xF0, 0xB2, 0x32)),
            BorderBrush = new SolidColorBrush(GetColorFromHex(item.BadgeColorHex)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 0, 3)
        };
        badgeBorder.Child = new TextBlock
        {
            Text = item.BadgeText,
            Foreground = new SolidColorBrush(GetColorFromHex(item.BadgeColorHex)),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold
        };
        var descText = new TextBlock
        {
            Text = item.ReasonDescription,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA5, 0xBF)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        reasonStack.Children.Add(badgeBorder);
        reasonStack.Children.Add(descText);
        Grid.SetColumn(reasonStack, 2);
        grid.Children.Add(reasonStack);

        // 3. Win Rate
        var wrStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var wrText = new TextBlock
        {
            Text = item.WinRateFormatted,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(item.WinRate >= 0.52 ? Color.FromRgb(0x2C, 0xE5, 0xB5) : (item.WinRate < 0.49 ? Color.FromRgb(0xFF, 0x63, 0x63) : Color.FromRgb(0xED, 0xF0, 0xF8))),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        wrStack.Children.Add(wrText);
        if (_isBanMode && item.EnemyWinRateAgainstYou > 0)
        {
            wrStack.Children.Add(new TextBlock
            {
                Text = $"vs You: {item.EnemyWinRateFormatted}",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x76, 0x76)),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            });
        }
        Grid.SetColumn(wrStack, 3);
        grid.Children.Add(wrStack);

        // 4. Pick Rate
        var prText = new TextBlock
        {
            Text = item.PickRateFormatted,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xCC, 0xE0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(prText, 4);
        grid.Children.Add(prText);

        // 5. Ban Rate
        var brText = new TextBlock
        {
            Text = item.BanRateFormatted,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xCC, 0xE0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(brText, 5);
        grid.Children.Add(brText);

        // 6. Games Matches
        var gamesStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var gamesText = new TextBlock
        {
            Text = item.MatchesFormatted,
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var gamesSub = new TextBlock
        {
            Text = "matches (10k+)",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x74, 0x8A)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        gamesStack.Children.Add(gamesText);
        gamesStack.Children.Add(gamesSub);
        Grid.SetColumn(gamesStack, 6);
        grid.Children.Add(gamesStack);

        // 7. Actions (⚔️ Build | 🎯 Set Pick / 🚫 Set Ban)
        var actionStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var buildBtn = new Button
        {
            Content = "⚔️ Build",
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x36)),
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = $"View runes, summoners & items for {item.ChampionName} {item.RoleLabel}"
        };
        buildBtn.Click += (s, e) =>
        {
            OnOpenChampionBuildRequested?.Invoke(item.ChampionKey, item.Role);
        };
        actionStack.Children.Add(buildBtn);

        var assignBtn = new Button
        {
            Content = _isBanMode ? "🚫 Set Ban" : "🎯 Set Pick",
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(_isBanMode ? Color.FromRgb(0x3E, 0x1A, 0x20) : Color.FromRgb(0x1A, 0x3A, 0x36)),
            Foreground = new SolidColorBrush(_isBanMode ? Color.FromRgb(0xFF, 0x76, 0x76) : Color.FromRgb(0x2C, 0xE5, 0xB5)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = _isBanMode 
                ? $"Set {item.ChampionName} as Primary Auto Ban champion" 
                : $"Set {item.ChampionName} as Primary Auto Pick champion"
        };
        assignBtn.Click += (s, e) =>
        {
            if (_isBanMode)
            {
                var settings = AppSettingsService.Instance.Settings;
                settings.PreferredBanChampId = item.ChampionId;
                settings.AutoBanEnabled = true;
                AppSettingsService.Instance.SaveAndNotify();

                MessageBox.Show($"{item.ChampionName} is now set as your Primary Auto Ban target!", 
                    "Auto Ban Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var settings = AppSettingsService.Instance.Settings;
                settings.PreferredPickChampId = item.ChampionId;
                settings.AutoChampSelectEnabled = true;
                AppSettingsService.Instance.SaveAndNotify();

                MessageBox.Show($"{item.ChampionName} is now set as your Primary Auto Pick champion!", 
                    "Auto Pick Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        actionStack.Children.Add(assignBtn);

        Grid.SetColumn(actionStack, 7);
        grid.Children.Add(actionStack);

        border.Child = grid;
        return border;
    }

    private static Color GetColorFromHex(string hex)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) return Color.FromRgb(0x2C, 0xE5, 0xB5);
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Color.FromRgb(0x2C, 0xE5, 0xB5);
        }
    }

    private void ModePickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBanMode) return;
        _isBanMode = false;
        UpdateModeUi();
        _ = RefreshSuggestionsAsync();
    }

    private void ModeBanBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isBanMode) return;
        _isBanMode = true;
        UpdateModeUi();
        _ = RefreshSuggestionsAsync();
    }

    private void UpdateModeUi()
    {
        if (_isBanMode)
        {
            ModePickBtn.Background = Brushes.Transparent;
            ModePickBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));

            ModeBanBtn.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x1A, 0x22));
            ModeBanBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));

            PrepickLabelText.Text = "CHAMPION TO PROTECT (PREPICK):";
            PersonalizedModeBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModePickBtn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x3B, 0x38));
            ModePickBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));

            ModeBanBtn.Background = Brushes.Transparent;
            ModeBanBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));

            PrepickLabelText.Text = "TEST AGAINST ENEMY LANER:";
            PersonalizedModeBtn.Visibility = Visibility.Visible;
        }
    }

    private void PersonalizedModeBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPersonalizedMode = !_isPersonalizedMode;
        UpdatePersonalizedBtnUi();

        AppSettingsService.Instance.Settings.PickSuggestionPersonalizedMode = _isPersonalizedMode;
        AppSettingsService.Instance.SaveAndNotify();

        _ = RefreshSuggestionsAsync();
    }

    private void UpdatePersonalizedBtnUi()
    {
        if (PersonalizedBorder == null || PersonalizedModeText == null) return;
        if (_isPersonalizedMode)
        {
            PersonalizedModeText.Text = "Personalized Mastery: ON";
            PersonalizedModeText.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xB2, 0x32));
            PersonalizedBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF0, 0xB2, 0x32));
        }
        else
        {
            PersonalizedModeText.Text = "Personalized Mastery: OFF";
            PersonalizedModeText.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));
            PersonalizedBorder.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x1E, 0x23, 0x32));
        }
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshSuggestionsAsync();
    }

    private void ConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        OnOpenSettingsRequested?.Invoke();
    }

    private void RolePill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string roleTag) return;
        _currentRole = roleTag;
        UpdateRolePillStyles();
        _ = RefreshSuggestionsAsync();
    }

    private void UpdateRolePillStyles()
    {
        var pills = new[] { RoleAllBtn, RoleTopBtn, RoleJungleBtn, RoleMidBtn, RoleAdcBtn, RoleSupportBtn };
        foreach (var pill in pills)
        {
            if (pill == null) continue;
            bool isSelected = string.Equals(pill.Tag as string, _currentRole, StringComparison.OrdinalIgnoreCase);
            pill.Background = isSelected 
                ? new SolidColorBrush(Color.FromRgb(0x22, 0x34, 0x54)) 
                : new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x26));
            pill.Foreground = isSelected 
                ? Brushes.White 
                : new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0xA4));
        }
    }

    private void PrepickChampCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PrepickChampCombo.SelectedItem is ChampionInfo champ)
        {
            _prepickedChampId = champ.Id > 0 ? champ.Id : null;
            UpdatePrepickUi();
            _ = RefreshSuggestionsAsync();
        }
    }

    private void ClearPrepickBtn_Click(object sender, RoutedEventArgs e)
    {
        PrepickChampCombo.SelectedIndex = 0;
    }

    private void UpdatePrepickUi()
    {
        if (_prepickedChampId.HasValue && _prepickedChampId.Value > 0)
        {
            var info = BuildDatabaseService.Instance.AllChampions.FirstOrDefault(c => c.Id == _prepickedChampId.Value);
            if (info != null && !string.IsNullOrEmpty(info.IconUrl))
            {
                SetImageSourceSafe(PrepickChampImg, info.IconUrl);
                PrepickChampImg.Visibility = Visibility.Visible;
                PrepickPlaceholderIcon.Visibility = Visibility.Collapsed;
                PrepickImgBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0xE5, 0xB5));
            }
            else
            {
                PrepickChampImg.Visibility = Visibility.Collapsed;
                PrepickPlaceholderIcon.Visibility = Visibility.Visible;
                PrepickImgBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x45));
            }
            ClearPrepickBtn.Visibility = Visibility.Visible;
        }
        else
        {
            PrepickChampImg.Source = null;
            PrepickChampImg.Visibility = Visibility.Collapsed;
            PrepickPlaceholderIcon.Visibility = Visibility.Visible;
            PrepickImgBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x45));
            ClearPrepickBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectPrepickComboById(int championId)
    {
        if (PrepickChampCombo.ItemsSource is IEnumerable<ChampionInfo> items)
        {
            var match = items.FirstOrDefault(c => c.Id == championId);
            if (match != null)
            {
                PrepickChampCombo.SelectedItem = match;
            }
        }
    }

    private static void SetImageSourceSafe(Image img, string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            img.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.RelativeOrAbsolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            img.Source = bitmap;
        }
        catch
        {
            img.Source = null;
        }
    }
}
