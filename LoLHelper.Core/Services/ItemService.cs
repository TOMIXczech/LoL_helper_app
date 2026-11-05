using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class GameItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int Gold { get; set; }
    public List<int> From { get; set; } = new();
}

public class ItemService
{
    public static ItemService Instance { get; } = new ItemService();

    private readonly HttpClient _http = new HttpClient();
    public List<GameItem> AllItems { get; private set; } = new();
    private Dictionary<int, GameItem> _itemDict = new();
    private bool _isInitialized = false;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            var versionResponse = await _http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            var versions = JsonSerializer.Deserialize<string[]>(versionResponse);
            string latestVersion = versions?[0] ?? "14.1.1";

            string itemsUrl = $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/data/en_US/item.json";
            var itemsJson = await _http.GetStringAsync(itemsUrl);
            
            using var doc = JsonDocument.Parse(itemsJson);
            var data = doc.RootElement.GetProperty("data");
            
            var itemsByName = new Dictionary<string, GameItem>();

            foreach (var item in data.EnumerateObject())
            {
                if (int.TryParse(item.Name, out int itemId))
                {
                    var prop = item.Value;
                    bool inStore = true;
                    if (prop.TryGetProperty("inStore", out var inStoreProp)) {
                        inStore = inStoreProp.GetBoolean();
                    }
                    if (!inStore) continue;

                    if (prop.TryGetProperty("maps", out var mapsProp) && mapsProp.TryGetProperty("11", out var map11Prop))
                    {
                        if (!map11Prop.GetBoolean()) continue;
                    }

                    var gold = prop.GetProperty("gold").GetProperty("total").GetInt32();
                    if (gold < 150) continue; // Filter out weird tokens

                    var itemName = prop.GetProperty("name").GetString() ?? "";

                    var fromIds = new List<int>();
                    if (prop.TryGetProperty("from", out var fromProp) && fromProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in fromProp.EnumerateArray())
                        {
                            if (int.TryParse(f.GetString(), out int fid)) fromIds.Add(fid);
                        }
                    }

                    var newItem = new GameItem
                    {
                        Id = itemId,
                        Name = itemName,
                        Description = prop.GetProperty("plaintext").GetString() ?? "",
                        IconUrl = $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/img/item/{prop.GetProperty("image").GetProperty("full").GetString()}",
                        Gold = gold,
                        From = fromIds
                    };
                    
                    if (itemsByName.TryGetValue(itemName, out var existingItem))
                    {
                        if (itemId < existingItem.Id)
                        {
                            itemsByName[itemName] = newItem;
                        }
                    }
                    else
                    {
                        itemsByName[itemName] = newItem;
                    }
                }
            }

            AllItems = new List<GameItem>(itemsByName.Values);
            foreach (var gameItem in AllItems)
            {
                _itemDict[gameItem.Id] = gameItem;
            }

            _isInitialized = true;
        }
        catch { }
    }

    public GameItem? GetItem(int id)
    {
        if (_itemDict.TryGetValue(id, out var item)) return item;
        return null;
    }

    public int? GetItemIdByName(string name)
    {
        foreach (var item in AllItems)
        {
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return item.Id;
        }
        return null;
    }
}
