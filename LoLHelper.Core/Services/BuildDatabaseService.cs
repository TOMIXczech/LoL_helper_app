using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services;

public class ChampionBuildPreset
{
    public int ChampionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public RunePreset RunePreset { get; set; } = new();
    
    // New Categorized Item Blocks
    public List<int> StartingItemIds { get; set; } = new();
    public List<int> CoreBuildOrderIds { get; set; } = new();
    public List<int> CoreFinalBuildIds { get; set; } = new();
    public List<int> SituationalItemIds { get; set; } = new();
    public List<int> BootsOptionsIds { get; set; } = new();
    
    public int Spell1Id { get; set; } = 4; // Default Flash
    public int Spell2Id { get; set; } = 14; // Default Ignite
}

public class BuildDatabaseService
{
    public static BuildDatabaseService Instance { get; } = new BuildDatabaseService();
    private List<ChampionBuildPreset> _database = new();

    public async Task InitializeAsync()
    {
        if (_database.Any()) return;

        // Initialize dependencies
        await RuneService.Instance.InitializeAsync();
        await ItemService.Instance.InitializeAsync();

        try
        {
            using var http = new System.Net.Http.HttpClient();
            var versionResponse = await http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            var versions = System.Text.Json.JsonSerializer.Deserialize<string[]>(versionResponse);
            string latestVersion = versions?[0] ?? "14.1.1";

            string champsUrl = $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/data/en_US/champion.json";
            var champsJson = await http.GetStringAsync(champsUrl);
            
            using var doc = System.Text.Json.JsonDocument.Parse(champsJson);
            var data = doc.RootElement.GetProperty("data");
            
            // Look for blitz_champions_data.json
            string[] possiblePaths = {
                System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "crawler", "blitz_champions_data.json"),
                @"C:\Users\ufo\Downloads\TP\LoL_Helper_app\crawler\blitz_champions_data.json",
                System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "blitz_champions_data.json"),
                @"C:\Users\ufo\Downloads\TP\crawler\blitz_champions_data.json"
            };
            
            string blitzPath = possiblePaths.FirstOrDefault(p => System.IO.File.Exists(p)) ?? "";
            System.Text.Json.JsonElement blitzRoot = default;
            if (!string.IsNullOrEmpty(blitzPath) && System.IO.File.Exists(blitzPath))
            {
                var blitzJson = System.IO.File.ReadAllText(blitzPath);
                using var blitzDocObj = System.Text.Json.JsonDocument.Parse(blitzJson);
                blitzRoot = blitzDocObj.RootElement.Clone();
            }
            
            var blitzDict = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>();
            if (blitzRoot.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var blitzChamp in blitzRoot.EnumerateObject())
                {
                    string cleanName = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(blitzChamp.Name, c => char.IsLetterOrDigit(c)))).ToLower();
                    blitzDict[cleanName] = blitzChamp.Value;
                }
            }

            foreach (var champ in data.EnumerateObject())
            {
                var prop = champ.Value;
                int champId = int.Parse(prop.GetProperty("key").GetString() ?? "0");
                string champName = prop.GetProperty("name").GetString() ?? "Unknown";
                string cleanName = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(champName, c => char.IsLetterOrDigit(c)))).ToLower();

                                if (blitzDict.TryGetValue(cleanName, out var rolesElement))
                {
                    foreach (var roleProp in rolesElement.EnumerateObject())
                    {
                        string roleName = roleProp.Name;
                        if (!roleProp.Value.TryGetProperty("Builds", out var buildsData) || buildsData.ValueKind != System.Text.Json.JsonValueKind.Array) continue;

                        int buildIndex = 1;
                        foreach (var buildData in buildsData.EnumerateArray())
                        {
                            if (buildIndex > 3) break; // Limit to max 3 builds per role

                            var starts = new List<int>();
                            if (buildData.TryGetProperty("starting_items", out var startsProp) && startsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var item in startsProp.EnumerateArray())
                                {
                                    var id = ItemService.Instance.GetItemIdByName(item.GetString() ?? "");
                                    if (id.HasValue) starts.Add(id.Value);
                                }
                            }

                            // Provide default starts if empty
                            if (starts.Count == 0)
                            {
                                if (roleName == "JUNGLE") { starts.Add(1102); starts.Add(2003); }
                                else if (roleName == "BOTTOM") { starts.Add(1055); starts.Add(2003); }
                                else if (roleName == "UTILITY") { starts.Add(3864); starts.Add(2003); starts.Add(2003); }
                                else { starts.Add(1056); starts.Add(2003); starts.Add(2003); }
                            }

                            var cores = new List<int>();
                            if (buildData.TryGetProperty("core_items", out var coresProp) && coresProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var item in coresProp.EnumerateArray())
                                {
                                    var id = ItemService.Instance.GetItemIdByName(item.GetString() ?? "");
                                    if (id.HasValue) cores.Add(id.Value);
                                }
                            }

                            // Pad cores to 6 items (as requested in the previous prompt, 7 for Support)
                            int targetCount = (roleName == "BOTTOM" || roleName == "UTILITY") ? 7 : 6;
                            if (roleName == "BOTTOM" || roleName == "UTILITY")
                            {
                                if (!cores.Contains(3864)) cores.Insert(0, 3864); // Runic Compass
                                if (!cores.Contains(3364)) cores.Add(3364); // Oracle Lens
                            }
                            if (cores.Count < targetCount)
                            {
                                var defaults = new int[] { 3026, 3156, 3072, 3139, 3143, 3065 };
                                foreach (var d in defaults)
                                {
                                    if (!cores.Contains(d)) cores.Add(d);
                                    if (cores.Count >= targetCount) break;
                                }
                            }

                            var perkIds = new List<int>();
                            if (buildData.TryGetProperty("runes", out var runesData))
                            {
                                if (runesData.TryGetProperty("keystone", out var ksProp))
                                {
                                    var ksId = RuneService.Instance.GetPerkIdByName(ksProp.GetString() ?? "");
                                    if (ksId.HasValue) perkIds.Add(ksId.Value);
                                }
                                if (runesData.TryGetProperty("secondary_and_other", out var secProp) && secProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var sec in secProp.EnumerateArray())
                                    {
                                        var id = RuneService.Instance.GetPerkIdByName(sec.GetString() ?? "");
                                        if (id.HasValue) perkIds.Add(id.Value);
                                    }
                                }
                            }

                            while (perkIds.Count < 6) perkIds.Add(5008);
                            if (perkIds.Count == 6) { perkIds.Add(5008); perkIds.Add(5008); perkIds.Add(5002); }

                            int pStyle = 8000, sStyle = 8100;
                            if (perkIds.Count > 0)
                            {
                                var t = RuneService.Instance.GetTreeIdByPerkId(perkIds[0]);
                                if (t.HasValue) pStyle = t.Value;
                            }
                            if (perkIds.Count > 4)
                            {
                                var t = RuneService.Instance.GetTreeIdByPerkId(perkIds[4]);
                                if (t.HasValue) sStyle = t.Value;
                            }

                            int spell1 = 4;
                            int spell2 = 14;
                            if (buildData.TryGetProperty("summoner_spells", out var spellsProp) && spellsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                var sArr = System.Linq.Enumerable.ToArray(spellsProp.EnumerateArray());
                                if (sArr.Length >= 2)
                                {
                                    spell1 = GetSpellId(sArr[0].GetString());
                                    spell2 = GetSpellId(sArr[1].GetString());
                                }
                            }
                            else
                            {
                                if (roleName == "JUNGLE") spell2 = 11;
                                else if (roleName == "TOP") spell2 = 12;
                                else if (roleName == "BOTTOM") spell2 = 7;
                            }

                            string buildName = buildData.TryGetProperty("build_name", out var bnProp) ? bnProp.GetString() ?? $"Blitz Core Build #{buildIndex}" : $"Blitz Core Build #{buildIndex}";
                            double winRate = buildData.TryGetProperty("overall_win_rate", out var wrProp) ? wrProp.GetDouble() : 0.0;
                            string formattedName = winRate > 0 ? $"{buildName} ({(winRate * 100):F1}% WR)" : buildName;

                            var expandedCores = new List<int>();
                            foreach (var cid in cores)
                            {
                                var gItem = ItemService.Instance.GetItem(cid);
                                if (gItem != null && gItem.From != null)
                                {
                                    foreach (var fid in gItem.From)
                                        if (!expandedCores.Contains(fid)) expandedCores.Add(fid);
                                }
                                if (!expandedCores.Contains(cid)) expandedCores.Add(cid);
                            }

                            var sitIds = new List<int>();
                            if (buildData.TryGetProperty("situational_items", out var sitProp) && sitProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var sitObj in sitProp.EnumerateArray())
                                {
                                    if (sitObj.TryGetProperty("item", out var itemNameProp))
                                    {
                                        var id = ItemService.Instance.GetItemIdByName(itemNameProp.GetString() ?? "");
                                        if (id.HasValue && !sitIds.Contains(id.Value)) sitIds.Add(id.Value);
                                    }
                                }
                            }

                            _database.Add(CreatePreset(champId, champName, roleName, formattedName, 
                                pStyle, sStyle, perkIds.ToArray(), 
                                starts, expandedCores, cores, sitIds, new List<int>(), spell1, spell2));
                                
                            buildIndex++;
                        }
                    }
                }

                else
                {
                    // Fallback
                    _database.Add(CreatePreset(champId, champName, "Flex", "Standard Output", 
                        8000, 8100, new[] { 8008, 9111, 9104, 8014, 8126, 8106, 5008, 5008, 5002 },
                        new List<int> { 1055, 2003 }, new List<int> { 1036, 1001, 3057, 3078, 3006, 1053, 1043, 3153 }, 
                        new List<int> { 3078, 3006, 3153, 3026, 3072, 3026 },
                        new List<int> { 3026, 3156 }, new List<int> { 3006, 3111 }, 4, 14
                    ));
                }
            }
        }
        catch { }
    }

    private ChampionBuildPreset CreatePreset(int champId, string champName, string role, string buildName, 
        int pStyle, int sStyle, int[] perks, List<int> starts, List<int> core, List<int> final, List<int> sit, List<int> boots, int spell1 = 4, int spell2 = 14)
    {
        return new ChampionBuildPreset
        {
            ChampionId = champId,
            Role = role,
            RunePreset = new RunePreset
            {
                Name = $"{champName} - {buildName}",
                Description = $"{role} | {buildName}",
                PrimaryStyleId = pStyle,
                SubStyleId = sStyle,
                SelectedPerkIds = perks,
                PrimaryStyleImageUrl = RuneService.Instance.GetImageUrl(pStyle),
                SubStyleImageUrl = RuneService.Instance.GetImageUrl(sStyle),
                KeystoneImageUrl = RuneService.Instance.GetImageUrl(perks[0])
            },
            StartingItemIds = starts,
            CoreBuildOrderIds = core,
            CoreFinalBuildIds = final,
            SituationalItemIds = sit,
            BootsOptionsIds = boots,
            Spell1Id = spell1,
            Spell2Id = spell2
        };
    }

    public List<ChampionBuildPreset> GetPresetsForChampion(int championId)
    {
        return _database.Where(p => p.ChampionId == championId).ToList();
    }

    private int GetSpellId(string? name)
    {
        if (name == null) return 4;
        return name.ToLower() switch
        {
            "cleanse" => 1,
            "exhaust" => 3,
            "flash" => 4,
            "ghost" => 6,
            "heal" => 7,
            "smite" => 11,
            "teleport" => 12,
            "ignite" => 14,
            "barrier" => 21,
            _ => 4
        };
    }
}




