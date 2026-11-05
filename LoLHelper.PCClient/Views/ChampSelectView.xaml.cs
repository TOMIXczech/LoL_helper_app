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
using System.Windows.Shapes;
using System.Windows.Threading;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public partial class ChampSelectView : UserControl
{
    private DispatcherTimer _pollTimer;
    private int _lastPageHash = -1;
    private bool _isUpdating = false;

    // Local Interactive State
    private string _activePageName = "Custom Page";
    private int _primaryTreeId = 8000;
    private int _secondaryTreeId = 8100;
    private List<int> _selectedPerks = new(); // 9 items: 4 primary, 2 secondary, 3 shards

    private string _currentRoleFilter = "";
    private List<ChampionBuildPreset> _allCurrentPresets = new();
    
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

    public ChampSelectView()
    {
        InitializeComponent();
        
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (s, e) => await PollActiveRunesAsync();
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        
        InitSpellMenus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await BuildDatabaseService.Instance.InitializeAsync();
            // Default to Annie (1) if not in champ select to show it works
            LoadPresetsForChampion(1);
            
            _pollTimer.Start();
            await PollActiveRunesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load data: " + ex.Message);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
    }

    private int _lastHoveredChampId = -1;

    private void LoadPresetsForChampion(int champId)
    {
        if (_lastHoveredChampId == champId) return;
        _lastHoveredChampId = champId;
        
        _allCurrentPresets = BuildDatabaseService.Instance.GetPresetsForChampion(champId);
        
        if (!_allCurrentPresets.Any())
        {
            HoveredChampText.Text = "AVAILABLE PRESETS (CUSTOM)";
            _allCurrentPresets.Add(new ChampionBuildPreset
            {
                ChampionId = champId,
                Role = "Flex",
                RunePreset = new RunePreset
                {
                    Name = $"Custom Champ {champId}",
                    Description = "Create your own custom build",
                    PrimaryStyleId = 8000,
                    SubStyleId = 8100,
                    SelectedPerkIds = new[] { 8008, 9111, 9104, 8014, 8126, 8106, 5008, 5002, 5002 }
                },
                StartingItemIds = new System.Collections.Generic.List<int>(),
                CoreBuildOrderIds = new System.Collections.Generic.List<int>(),
                CoreFinalBuildIds = new System.Collections.Generic.List<int>(),
                SituationalItemIds = new System.Collections.Generic.List<int>(),
                BootsOptionsIds = new System.Collections.Generic.List<int>()
            });
        }
        else
        {
            HoveredChampText.Text = $"AVAILABLE PRESETS (CHAMP {champId})";
        }

        ApplyRoleFilter();
    }

    private void RoleFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton btn)
        {
            _currentRoleFilter = btn.IsChecked == true ? btn.Tag?.ToString() ?? "" : "";
            
            // Uncheck others
            FilterTop.IsChecked = btn == FilterTop && btn.IsChecked == true;
            FilterJgl.IsChecked = btn == FilterJgl && btn.IsChecked == true;
            FilterMid.IsChecked = btn == FilterMid && btn.IsChecked == true;
            FilterBot.IsChecked = btn == FilterBot && btn.IsChecked == true;
            FilterSup.IsChecked = btn == FilterSup && btn.IsChecked == true;

            ApplyRoleFilter();
        }
    }

    private void ApplyRoleFilter()
    {
        var presets = string.IsNullOrEmpty(_currentRoleFilter) 
            ? _allCurrentPresets 
            : _allCurrentPresets.Where(p => p.Role.Equals(_currentRoleFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            
        PresetsList.ItemsSource = presets;
        if (presets.Any()) PresetsList.SelectedIndex = 0;
    }

    private async Task PollActiveRunesAsync()
    {
        if (_isUpdating || !LcuApiClient.Instance.IsConnected) return;

        _isUpdating = true;
        try
        {
            // 1. Poll Session for hovered champion
            string sessionJson = await LcuApiClient.Instance.GetChampSelectSessionAsync();
            if (!string.IsNullOrEmpty(sessionJson))
            {
                using var sDoc = JsonDocument.Parse(sessionJson);
                var sRoot = sDoc.RootElement;
                if (sRoot.TryGetProperty("localPlayerCellId", out var cellIdProp))
                {
                    int myCell = cellIdProp.GetInt32();
                    if (sRoot.TryGetProperty("myTeam", out var myTeamProp))
                    {
                        foreach (var p in myTeamProp.EnumerateArray())
                        {
                            if (p.TryGetProperty("cellId", out var pCell) && pCell.GetInt32() == myCell)
                            {
                                int champId = p.TryGetProperty("championId", out var cId) ? cId.GetInt32() : 0;
                                if (champId == 0) champId = p.TryGetProperty("championPickIntent", out var intentId) ? intentId.GetInt32() : 0;
                                
                                string assignedRole = p.TryGetProperty("assignedPosition", out var pos) ? pos.GetString() ?? "" : "";
                                
                                if (!string.IsNullOrEmpty(assignedRole) && _currentRoleFilter == "")
                                {
                                    string tag = assignedRole.ToUpper() switch
                                    {
                                        "TOP" => "TOP",
                                        "JUNGLE" => "JUNGLE",
                                        "MIDDLE" => "MIDDLE",
                                        "BOTTOM" => "BOTTOM",
                                        "UTILITY" => "UTILITY",
                                        _ => ""
                                    };
                                    
                                    if (tag != "")
                                    {
                                        _currentRoleFilter = tag;
                                        FilterTop.IsChecked = tag == "TOP";
                                        FilterJgl.IsChecked = tag == "JUNGLE";
                                        FilterMid.IsChecked = tag == "MIDDLE";
                                        FilterBot.IsChecked = tag == "BOTTOM";
                                        FilterSup.IsChecked = tag == "UTILITY";
                                    }
                                }

                                if (champId > 0)
                                {
                                    LoadPresetsForChampion(champId);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // 2. Poll Runes
            string json = await LcuApiClient.Instance.GetCurrentRunePageJsonAsync();
            if (string.IsNullOrEmpty(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("id", out var idProp)) return;
            if (!root.TryGetProperty("selectedPerkIds", out var perksArray)) return;
            
            int hash = idProp.GetInt32();
            var newPerks = new List<int>();
            foreach (var perk in perksArray.EnumerateArray())
            {
                int p = perk.GetInt32();
                newPerks.Add(p);
                hash ^= p;
            }
            
            int primary = root.TryGetProperty("primaryStyleId", out var pStyleProp) ? pStyleProp.GetInt32() : 0;
            int sub = root.TryGetProperty("subStyleId", out var sStyleProp) ? sStyleProp.GetInt32() : 0;
            hash ^= primary ^ sub;

            if (hash == _lastPageHash) return; // No change
            
            _lastPageHash = hash;
            
            _activePageName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Custom Page" : "Custom Page";
            _primaryTreeId = primary;
            _secondaryTreeId = sub;
            _selectedPerks = newPerks;

            ActivePageNameText.Text = $"({_activePageName})";
            RenderRuneBuilder();
        }
        catch { }
        finally
        {
            _isUpdating = false;
        }
    }

    private ChampionBuildPreset? _activePreset;
    private Action<int?>? _onItemPickedCallback;
    private bool _itemPickerInitialized = false;

    private void PresetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetsList.SelectedItem is ChampionBuildPreset selectedBuild)
        {
            _activePreset = selectedBuild;
            RenderItemBuilder(_activePreset);
            
            _currentSpell1Id = selectedBuild.Spell1Id > 0 ? selectedBuild.Spell1Id : 4;
            _currentSpell2Id = selectedBuild.Spell2Id > 0 ? selectedBuild.Spell2Id : 14;
            UpdateSpellUI();
        }
    }

    private void RenderItemBuilder(ChampionBuildPreset? preset = null)
    {
        InteractiveItemBuilder.Children.Clear();
        if (preset == null) return;
        
        RenderItemSection("STARTING ITEMS", preset.StartingItemIds);
        RenderItemSection("CORE BUILD ORDER", preset.CoreBuildOrderIds, showArrows: true);
        RenderItemSection("CORE FINAL BUILD", preset.CoreFinalBuildIds);
        RenderItemSection("SITUATIONAL", preset.SituationalItemIds);
    }

    private void RenderItemSection(string title, List<int> itemIds, bool showArrows = false)
    {
        if (itemIds == null) return;

        var sectionLabel = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F91A3")),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 5)
        };
        InteractiveItemBuilder.Children.Add(sectionLabel);

        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,15) };
        
        for (int i = 0; i < itemIds.Count; i++)
        {
            int index = i;
            int itemId = itemIds[i];
            var item = ItemService.Instance.GetItem(itemId);
            if (item == null) continue;

            var img = new Image
            {
                Width = 36, Height = 36,
                Cursor = Cursors.Hand,
                ToolTip = new ToolTip { Content = item.Name, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C21")), Foreground = Brushes.White, BorderThickness = new Thickness(0) }
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            
            try { img.Source = new BitmapImage(new Uri(item.IconUrl)); } catch { }

            img.MouseEnter += (s, e) => img.Opacity = 0.8;
            img.MouseLeave += (s, e) => img.Opacity = 1.0;
            
            var border = new Border
            {
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D45")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, showArrows ? 8 : 6, 0),
                Child = img
            };
            
            border.MouseLeftButtonDown += (s, e) => {
                OpenItemPicker(async newId => {
                    if (newId == null) itemIds.RemoveAt(index);
                    else itemIds[index] = newId.Value;
                    RenderItemBuilder(_activePreset);
                    if (_activePreset != null && LcuApiClient.Instance.IsConnected)
                        await LcuApiClient.Instance.SetItemBuildAsync(_activePreset);
                });
            };

            panel.Children.Add(border);

            if (showArrows && i < itemIds.Count - 1)
            {
                var arrow = new TextBlock
                {
                    Text = "➔",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C5C66")),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                panel.Children.Add(arrow);
            }
        }
        
        var addBtn = CreateIconBlock("+", "#A3BE8C", "#2D2D35");
        addBtn.MouseLeftButtonDown += (s, e) => {
            OpenItemPicker(async newId => {
                if (newId != null) {
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

    private Border CreateIconBlock(string text, string fgColor, string bgColor)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgColor)),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -3, 0, 0)
        };
        var border = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Child = tb,
            ToolTip = new ToolTip { Content = text == "+" ? "Add Item" : "Remove Item", Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C21")), Foreground = Brushes.White, BorderThickness = new Thickness(0) }
        };
        border.MouseEnter += (s, e) => border.Opacity = 0.8;
        border.MouseLeave += (s, e) => border.Opacity = 1.0;
        return border;
    }

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
            
            var removeBtn = CreateIconBlock("-", "#BF616A", "#2D2D35");
            removeBtn.MouseLeftButtonDown += (s, e) => {
                ItemPickerOverlay.Visibility = Visibility.Collapsed;
                _onItemPickedCallback?.Invoke(null);
            };
            removeBtn.Tag = "REMOVE";
            AllItemsWrapPanel.Children.Add(removeBtn);

            foreach (var item in ItemService.Instance.AllItems.OrderBy(x => x.Name))
            {
                var img = new Image
                {
                    Width = 40, Height = 40,
                    Cursor = Cursors.Hand,
                    ToolTip = new ToolTip { Content = item.Name, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C21")), Foreground = Brushes.White, BorderThickness = new Thickness(0) }
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                try { img.Source = new BitmapImage(new Uri(item.IconUrl)); } catch { }

                img.MouseEnter += (s, e) => img.Opacity = 0.8;
                img.MouseLeave += (s, e) => img.Opacity = 1.0;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D45")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(4),
                    Child = img,
                    Tag = item
                };

                int capturedId = item.Id;
                border.MouseLeftButtonDown += (s, e) => {
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

    private void RenderRuneBuilder()
    {
        InteractiveRuneBuilder.Children.Clear();
        
        var allTrees = RuneService.Instance.AllTrees;
        if (allTrees.Count == 0) return;

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Slim Divider
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        
        // --- LEFT COLUMN: PRIMARY TREE ---
        var primaryPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        
        // Primary Tree Selector Row
        var primaryTreeSelector = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,10) };
        foreach (var tree in allTrees)
        {
            var img = CreateClickableImage(tree.Icon, 32, tree.Id == _primaryTreeId, () => OnTreeClicked(tree.Id, true), false, tree.Name);
            img.Margin = new Thickness(6);
            primaryTreeSelector.Children.Add(img);
        }
        primaryPanel.Children.Add(primaryTreeSelector);
        primaryPanel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D35")), Margin = new Thickness(0,0,0,20) });

        var primaryTreeData = allTrees.FirstOrDefault(t => t.Id == _primaryTreeId);
        if (primaryTreeData != null)
        {
            foreach (var slot in primaryTreeData.Slots)
            {
                bool isKeystone = primaryTreeData.Slots.IndexOf(slot) == 0;
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,15) };
                
                foreach (var rune in slot.Runes)
                {
                    bool isSelected = _selectedPerks.Contains(rune.Id);
                    var img = CreateClickableImage(rune.Icon, isKeystone ? 48 : 38, isSelected, () => OnRuneClicked(rune.Id, true), isKeystone, rune.Name);
                    img.Margin = new Thickness(isKeystone ? 12 : 8, 0, isKeystone ? 12 : 8, 0);
                    rowPanel.Children.Add(img);
                }
                primaryPanel.Children.Add(rowPanel);
            }
        }
        
        Grid.SetColumn(primaryPanel, 0);
        mainGrid.Children.Add(primaryPanel);

        // --- MIDDLE: DIVIDER ---
        var divider = new Rectangle { Width = 2, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D35")), Margin = new Thickness(0, 10, 0, 10), RadiusX = 1, RadiusY = 1 };
        Grid.SetColumn(divider, 1);
        mainGrid.Children.Add(divider);

        // --- RIGHT COLUMN: SECONDARY TREE & SHARDS ---
        var secondaryPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        
        // Secondary Tree Selector Row
        var secondaryTreeSelector = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,10) };
        foreach (var tree in allTrees)
        {
            bool isPrimary = tree.Id == _primaryTreeId;
            var img = CreateClickableImage(tree.Icon, 30, tree.Id == _secondaryTreeId && !isPrimary, () => { if (!isPrimary) OnTreeClicked(tree.Id, false); }, false, tree.Name);
            img.Margin = new Thickness(5);
            if (isPrimary)
            {
                img.Visibility = Visibility.Collapsed;
            }
            secondaryTreeSelector.Children.Add(img);
        }
        secondaryPanel.Children.Add(secondaryTreeSelector);
        secondaryPanel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D35")), Margin = new Thickness(0,0,0,20) });

        var secondaryTreeData = allTrees.FirstOrDefault(t => t.Id == _secondaryTreeId);
        if (secondaryTreeData != null)
        {
            // Secondary skips the keystone row (index 0)
            for (int i = 1; i < secondaryTreeData.Slots.Count; i++)
            {
                int captureSlotIndex = i;
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,15) };
                foreach (var rune in secondaryTreeData.Slots[i].Runes)
                {
                    int captureRuneId = rune.Id;
                    bool isSelected = _selectedPerks.Contains(captureRuneId);
                    var img = CreateClickableImage(rune.Icon, 38, isSelected, () => OnRuneClicked(captureRuneId, false, captureSlotIndex), false, rune.Name);
                    img.Margin = new Thickness(8, 0, 8, 0);
                    rowPanel.Children.Add(img);
                }
                secondaryPanel.Children.Add(rowPanel);
            }
        }

        // Shards Rows
        secondaryPanel.Children.Add(new Rectangle { Height = 15 });
        int[][] shardRows = new[]
        {
            new[] { 5008, 5005, 5007 },
            new[] { 5008, 5010, 5001 },
            new[] { 5011, 5013, 5001 }
        };

        for (int i = 0; i < shardRows.Length; i++)
        {
            int captureShardIndex = i;
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,10) };
            foreach (int shardId in shardRows[i])
            {
                int captureShardId = shardId;
                bool isSelected = _selectedPerks.Count > 6 + captureShardIndex && _selectedPerks[6 + captureShardIndex] == captureShardId;
                
                string iconUrl = RuneService.Instance.GetImageUrl(captureShardId);
                var img = CreateClickableImage(iconUrl, 30, isSelected, () => OnShardClicked(captureShardId, captureShardIndex), false, GetShardName(captureShardId));
                img.Margin = new Thickness(8, 0, 8, 0);
                
                var border = new Border { CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Child = img };
                rowPanel.Children.Add(border);
            }
            secondaryPanel.Children.Add(rowPanel);
        }

        Grid.SetColumn(secondaryPanel, 2);
        mainGrid.Children.Add(secondaryPanel);

        InteractiveRuneBuilder.Children.Add(mainGrid);
    }

    private string GetShardName(int id) => id switch
    {
        5008 => "Adaptive Force",
        5005 => "Attack Speed",
        5007 => "Ability Haste",
        5002 => "Armor",
        5003 => "Magic Resist",
        5001 => "Scaling Health",
        5011 => "Flat Health",
        5013 => "Tenacity",
        5010 => "Movement Speed",
        _ => "Stat Shard"
    };

    private FrameworkElement CreateClickableImage(string url, double size, bool isSelected, Action onClick, bool isKeystone = false, string tooltip = "")
    {
        var img = new Image
        {
            Width = size,
            Height = size,
            Opacity = isSelected ? 1.0 : 0.3,
            Cursor = Cursors.Hand
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        
        try { img.Source = new BitmapImage(new Uri(url)); } catch { }

        if (isSelected)
        {
            img.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = isKeystone ? (Color)ColorConverter.ConvertFromString("#EBCB8B") : (Color)ColorConverter.ConvertFromString("#007ACC"),
                BlurRadius = isKeystone ? 20 : 12,
                ShadowDepth = 0,
                Opacity = 0.9
            };
        }

        var container = new Border
        {
            Background = Brushes.Transparent,
            Child = img,
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : new ToolTip { Content = tooltip, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C21")), Foreground = Brushes.White, BorderThickness = new Thickness(0) }
        };

        container.MouseEnter += (s, e) => { if (!isSelected) img.Opacity = 0.7; };
        container.MouseLeave += (s, e) => { if (!isSelected) img.Opacity = 0.3; };
        container.MouseLeftButtonDown += (s, e) => onClick();

        return container;
    }

    private void OnTreeClicked(int treeId, bool isPrimary)
    {
        if (isPrimary)
        {
            if (_primaryTreeId == treeId) return;
            _primaryTreeId = treeId;
            if (_secondaryTreeId == treeId)
            {
                // Swap or pick another
                _secondaryTreeId = RuneService.Instance.AllTrees.First(t => t.Id != treeId).Id;
            }
            // Auto-pick defaults for primary
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == treeId);
            if (_selectedPerks.Count < 4) _selectedPerks.AddRange(new int[4 - _selectedPerks.Count]);
            for(int i=0; i<4; i++) _selectedPerks[i] = tree.Slots[i].Runes[0].Id;
        }
        else
        {
            if (_secondaryTreeId == treeId) return;
            _secondaryTreeId = treeId;
            
            // Auto-pick 2 defaults for secondary
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == treeId);
            if (_selectedPerks.Count < 6) _selectedPerks.AddRange(new int[6 - _selectedPerks.Count]);
            _selectedPerks[4] = tree.Slots[1].Runes[0].Id;
            _selectedPerks[5] = tree.Slots[2].Runes[0].Id;
        }
        
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
            // Secondary logic: Max 2 selections from different rows.
            var tree = RuneService.Instance.AllTrees.First(t => t.Id == _secondaryTreeId);
            
            // Get current active rows
            List<int> activeRows = new();
            for(int i=1; i<tree.Slots.Count; i++)
            {
                if (_selectedPerks.Skip(4).Take(2).Any(id => tree.Slots[i].Runes.Any(r => r.Id == id)))
                {
                    activeRows.Add(i);
                }
            }

            if (activeRows.Contains(secondarySlotIndex))
            {
                // Replace in same row
                for(int i=4; i<6; i++)
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
                // New row. We must drop one of the existing ones if we already have 2.
                // Just overwrite index 4, and move 4 to 5 (FIFO)
                if (_selectedPerks.Count < 6) _selectedPerks.AddRange(new int[6 - _selectedPerks.Count]);
                _selectedPerks[5] = _selectedPerks[4];
                _selectedPerks[4] = runeId;
            }
        }

        RenderRuneBuilder();
        _ = PushRunesToClientAsync();
    }

    private void OnShardClicked(int shardId, int rowIndex)
    {
        while (_selectedPerks.Count <= 6 + rowIndex) _selectedPerks.Add(0);
        _selectedPerks[6 + rowIndex] = shardId;
        
        RenderRuneBuilder();
        _ = PushRunesToClientAsync();
    }

    private async Task PushRunesToClientAsync()
    {
        if (!LcuApiClient.Instance.IsConnected) return;

        // Ensure we have exactly 9 perks
        if (_selectedPerks.Count < 9) return;

        _lastPageHash = -1; // Force next poll to refresh
        await LcuApiClient.Instance.SetRunePageAsync(_activePageName, _primaryTreeId, _secondaryTreeId, _selectedPerks.Take(9).ToArray());
    }

    private async void InjectRunesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PresetsList.SelectedItem is not ChampionBuildPreset selectedBuild) return;
        if (!LcuApiClient.Instance.IsConnected) return;

        var selectedPreset = selectedBuild.RunePreset;

        InjectRunesBtn.Content = "Injecting...";
        InjectRunesBtn.IsEnabled = false;

        bool success = await LcuApiClient.Instance.SetRunePageAsync(
            selectedPreset.Name, selectedPreset.PrimaryStyleId, selectedPreset.SubStyleId, selectedPreset.SelectedPerkIds
        );
        
        await LcuApiClient.Instance.SetItemBuildAsync(selectedBuild);
        
        int s1 = _currentSpell1Id;
        int s2 = _currentSpell2Id;
        await LcuApiClient.Instance.SetSummonerSpellsAsync(s1, s2);

        if (success) { _lastPageHash = -1; await PollActiveRunesAsync(); }
        
        InjectRunesBtn.Content = "Inject Selected Preset";
        InjectRunesBtn.IsEnabled = true;
    }

    private int _currentSpell1Id = 4;
    private int _currentSpell2Id = 14;

    private void InitSpellMenus()
    {
        foreach (var spell in _summonerSpells)
        {
            var item1 = new MenuItem { Header = spell.Name, Tag = spell.Id };
            item1.Click += (s, e) => SelectSpell(1, spell.Id);
            Spell1Menu.Items.Add(item1);
            
            var item2 = new MenuItem { Header = spell.Name, Tag = spell.Id };
            item2.Click += (s, e) => SelectSpell(2, spell.Id);
            Spell2Menu.Items.Add(item2);
        }
        UpdateSpellUI();
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
        UpdateSpellUI();
    }
    
    private void UpdateSpellUI()
    {
        var s1 = _summonerSpells.FirstOrDefault(s => s.Id == _currentSpell1Id);
        if (s1 != null)
        {
            Spell1Img.Source = new BitmapImage(new Uri(s1.IconUrl));
            Spell1Img.ToolTip = s1.Name;
        }
        
        var s2 = _summonerSpells.FirstOrDefault(s => s.Id == _currentSpell2Id);
        if (s2 != null)
        {
            Spell2Img.Source = new BitmapImage(new Uri(s2.IconUrl));
            Spell2Img.ToolTip = s2.Name;
        }
    }

    private void Spell1_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Spell1Menu.PlacementTarget = sender as UIElement;
        Spell1Menu.IsOpen = true;
    }
    
    private void Spell2_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Spell2Menu.PlacementTarget = sender as UIElement;
        Spell2Menu.IsOpen = true;
    }
}
