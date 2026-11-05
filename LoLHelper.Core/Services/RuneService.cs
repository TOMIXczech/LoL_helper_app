using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class RuneItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public class RuneSlot
{
    public List<RuneItem> Runes { get; set; } = new();
}

public class RuneTree
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<RuneSlot> Slots { get; set; } = new();
}

public class RunePreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PrimaryStyleId { get; set; }
    public int SubStyleId { get; set; }
    public int[] SelectedPerkIds { get; set; } = Array.Empty<int>();
    
    public string PrimaryStyleImageUrl { get; set; } = string.Empty;
    public string SubStyleImageUrl { get; set; } = string.Empty;
    public string KeystoneImageUrl { get; set; } = string.Empty;
}

public class RuneService
{
    public static RuneService Instance { get; } = new RuneService();

    private readonly HttpClient _http = new HttpClient();
    private const string DDragonVersionUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
    
    private Dictionary<int, string> _runeImages = new();
    public List<RuneTree> AllTrees { get; private set; } = new();
    private bool _isInitialized = false;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            var versionResponse = await _http.GetStringAsync(DDragonVersionUrl);
            var versions = JsonSerializer.Deserialize<string[]>(versionResponse);
            string latestVersion = versions?[0] ?? "14.1.1";

            string runesUrl = $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/data/en_US/runesReforged.json";
            var runesJson = await _http.GetStringAsync(runesUrl);
            
            using var doc = JsonDocument.Parse(runesJson);
            foreach (var tree in doc.RootElement.EnumerateArray())
            {
                var newTree = new RuneTree
                {
                    Id = tree.GetProperty("id").GetInt32(),
                    Name = tree.GetProperty("name").GetString() ?? "",
                    Icon = $"https://ddragon.leagueoflegends.com/cdn/img/{tree.GetProperty("icon").GetString()}"
                };
                
                _runeImages[newTree.Id] = newTree.Icon;

                foreach (var slot in tree.GetProperty("slots").EnumerateArray())
                {
                    var newSlot = new RuneSlot();
                    foreach (var rune in slot.GetProperty("runes").EnumerateArray())
                    {
                        var newItem = new RuneItem
                        {
                            Id = rune.GetProperty("id").GetInt32(),
                            Name = rune.GetProperty("name").GetString() ?? "",
                            Icon = $"https://ddragon.leagueoflegends.com/cdn/img/{rune.GetProperty("icon").GetString()}"
                        };
                        newSlot.Runes.Add(newItem);
                        _runeImages[newItem.Id] = newItem.Icon;
                    }
                    newTree.Slots.Add(newSlot);
                }
                AllTrees.Add(newTree);
            }

            // Fallback for missing/new runes and stat shards via Community Dragon
            string cdragonPerksUrl = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perks.json";
            try 
            {
                var cdragonJson = await _http.GetStringAsync(cdragonPerksUrl);
                using var cdragonDoc = JsonDocument.Parse(cdragonJson);
                foreach (var perk in cdragonDoc.RootElement.EnumerateArray())
                {
                    if (perk.TryGetProperty("id", out var idProp) && perk.TryGetProperty("iconPath", out var iconProp))
                    {
                        int id = idProp.GetInt32();
                        string iconPath = iconProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            // Convert /lol-game-data/assets/... to Community Dragon URL format
                            // Example: /lol-game-data/assets/v1/perk-images/Styles/Inspiration/FirstStrike/FirstStrike.png
                            string cleanPath = iconPath.Replace("/lol-game-data/assets/", "").ToLower();
                            string fullUrl = $"https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/{cleanPath}";
                            
                            // Only overwrite if it wasn't populated or if we want CDragon as reliable fallback
                            if (!_runeImages.ContainsKey(id))
                            {
                                _runeImages[id] = fullUrl;
                            }
                        }
                    }
                }
            }
            catch { }

            // Stat Shards manual fallbacks (just in case)
            string cdragonBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/";
            if (!_runeImages.ContainsKey(5008)) _runeImages[5008] = cdragonBase + "statmodsadaptiveforceicon.png";
            if (!_runeImages.ContainsKey(5005)) _runeImages[5005] = cdragonBase + "statmodsattackspeedicon.png";
            if (!_runeImages.ContainsKey(5007)) _runeImages[5007] = cdragonBase + "statmodscdrscalingicon.png";
            if (!_runeImages.ContainsKey(5002)) _runeImages[5002] = cdragonBase + "statmodsarmoricon.png";
            if (!_runeImages.ContainsKey(5003)) _runeImages[5003] = cdragonBase + "statmodsmagicresicon.png";
            if (!_runeImages.ContainsKey(5001)) _runeImages[5001] = cdragonBase + "statmodshealthscalingicon.png";
            if (!_runeImages.ContainsKey(5011)) _runeImages[5011] = cdragonBase + "statmodshealthplusicon.png";
            if (!_runeImages.ContainsKey(5013)) _runeImages[5013] = cdragonBase + "statmodstenacityicon.png";
            if (!_runeImages.ContainsKey(5010)) _runeImages[5010] = cdragonBase + "statmodsmovementspeedicon.png";

            _isInitialized = true;
        }
        catch { }
    }

    public string GetImageUrl(int id)
    {
        return _runeImages.TryGetValue(id, out var url) ? url : "";
    }

    public int? GetPerkIdByName(string name)
    {
        foreach (var tree in AllTrees)
        {
            foreach (var slot in tree.Slots)
            {
                foreach (var rune in slot.Runes)
                {
                    if (rune.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return rune.Id;
                }
            }
        }
        return null;
    }

    public int? GetTreeIdByPerkId(int perkId)
    {
        foreach (var tree in AllTrees)
        {
            foreach (var slot in tree.Slots)
            {
                foreach (var rune in slot.Runes)
                {
                    if (rune.Id == perkId)
                        return tree.Id;
                }
            }
        }
        return null;
    }

    public async Task<List<RunePreset>> GetPresetsAsync()
    {
        await InitializeAsync();

        return new List<RunePreset>
        {
            new RunePreset
            {
                Name = "The Assassin (Electrocute)",
                Description = "High burst damage (Zed, Talon, Ahri)",
                PrimaryStyleId = 8100, SubStyleId = 8000,
                SelectedPerkIds = new[] { 8112, 8143, 8138, 8106, 8009, 8014, 5008, 5008, 5002 },
                PrimaryStyleImageUrl = GetImageUrl(8100), SubStyleImageUrl = GetImageUrl(8000), KeystoneImageUrl = GetImageUrl(8112)
            },
            new RunePreset
            {
                Name = "The Bruiser (Conqueror)",
                Description = "Extended fights (Yasuo, Darius, Aatrox)",
                PrimaryStyleId = 8000, SubStyleId = 8400,
                SelectedPerkIds = new[] { 8010, 9111, 9104, 8014, 8453, 8242, 5005, 5008, 5002 },
                PrimaryStyleImageUrl = GetImageUrl(8000), SubStyleImageUrl = GetImageUrl(8400), KeystoneImageUrl = GetImageUrl(8010)
            },
            new RunePreset
            {
                Name = "The Mage (Arcane Comet)",
                Description = "Poke and control (Lux, Syndra, Xerath)",
                PrimaryStyleId = 8200, SubStyleId = 8300,
                SelectedPerkIds = new[] { 8229, 8226, 8210, 8236, 8304, 8345, 5008, 5008, 5002 },
                PrimaryStyleImageUrl = GetImageUrl(8200), SubStyleImageUrl = GetImageUrl(8300), KeystoneImageUrl = GetImageUrl(8229)
            },
            new RunePreset
            {
                Name = "The Tank (Grasp)",
                Description = "Sustain and scaling (Ornn, Sion, Shen)",
                PrimaryStyleId = 8400, SubStyleId = 8000,
                SelectedPerkIds = new[] { 8437, 8446, 8429, 8451, 9111, 9104, 5005, 5002, 5002 },
                PrimaryStyleImageUrl = GetImageUrl(8400), SubStyleImageUrl = GetImageUrl(8000), KeystoneImageUrl = GetImageUrl(8437)
            }
        };
    }
}
