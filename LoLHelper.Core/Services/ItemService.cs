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
    public string IconUrl { get; set; } = string.Empty;
    public List<int> From { get; set; } = new();
}

public class ItemService
{
    public static ItemService Instance { get; } = new ItemService();

    private readonly HttpClient _http = new HttpClient();
    public List<GameItem> AllItems { get; private set; } = new();
    private Dictionary<int, GameItem> _itemDict = new();
    private bool _isInitialized = false;
    public string LatestVersion { get; private set; } = "14.1.1";

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            var versionResponse = await _http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            var versions = JsonSerializer.Deserialize<string[]>(versionResponse);
            LatestVersion = versions?[0] ?? "14.1.1";

            string itemsUrl = $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/data/en_US/item.json";
            var itemsJson = await _http.GetStringAsync(itemsUrl);
            
            using var doc = JsonDocument.Parse(itemsJson);
            var data = doc.RootElement.GetProperty("data");
            
            var itemsByName = new Dictionary<string, GameItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in data.EnumerateObject())
            {
                if (int.TryParse(item.Name, out int itemId))
                {
                    var prop = item.Value;
                    var itemName = prop.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(itemName)) continue;

                    var fromIds = new List<int>();
                    if (prop.TryGetProperty("from", out var fromProp) && fromProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in fromProp.EnumerateArray())
                        {
                            if (int.TryParse(f.GetString(), out int fid)) fromIds.Add(fid);
                        }
                    }

                    string imgFull = prop.TryGetProperty("image", out var imgProp) && imgProp.TryGetProperty("full", out var fullProp)
                        ? fullProp.GetString() ?? $"{itemId}.png"
                        : $"{itemId}.png";

                    var newItem = new GameItem
                    {
                        Id = itemId,
                        Name = itemName,
                        IconUrl = $"https://ddragon.leagueoflegends.com/cdn/{LatestVersion}/img/item/{imgFull}",
                        From = fromIds
                    };

                    // Always register in _itemDict by ID so potions, jungle pets, wards, and components always resolve
                    _itemDict[itemId] = newItem;

                    bool inStore = true;
                    if (prop.TryGetProperty("inStore", out var inStoreProp))
                        inStore = inStoreProp.GetBoolean();

                    bool onSummonersRift = true;
                    if (prop.TryGetProperty("maps", out var mapsProp) && mapsProp.TryGetProperty("11", out var map11Prop))
                        onSummonersRift = map11Prop.GetBoolean();

                    if (!inStore || !onSummonersRift) continue;

                    if (itemsByName.TryGetValue(itemName, out var existingItem))
                    {
                        if (itemId < existingItem.Id)
                            itemsByName[itemName] = newItem;
                    }
                    else
                    {
                        itemsByName[itemName] = newItem;
                    }
                }
            }

            AllItems = new List<GameItem>(itemsByName.Values);
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
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var item in AllItems)
        {
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return item.Id;
        }
        foreach (var kv in _itemDict)
        {
            if (kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return null;
    }
}
