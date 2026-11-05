using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services
{
    public class LcuApiClient
    {
        public static LcuApiClient Instance { get; } = new LcuApiClient();

        private HttpClient? _httpClient;
        private LcuCredentials? _credentials;

        public LcuApiClient()
        {
        }

        public void SetCredentials(LcuCredentials credentials)
        {
            _credentials = credentials;
            
            // Dispose the old client if it exists (e.g. League restarted and changed ports)
            _httpClient?.Dispose();

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(credentials.BaseUrl)
            };
            
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", 
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"riot:{credentials.Password}")));
        }

        public bool IsConnected => _credentials != null;

        public async Task<string> GetCurrentSummonerAsync()
        {
            if (_credentials == null) throw new InvalidOperationException("Not connected to LCU.");
            var response = await _httpClient!.GetAsync("/lol-summoner/v1/current-summoner");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetCurrentRunePageJsonAsync()
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-perks/v1/currentpage");
            if (!response.IsSuccessStatusCode) return string.Empty;
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetChampSelectSessionAsync()
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-champ-select/v1/session");
            if (!response.IsSuccessStatusCode) return string.Empty;
            return await response.Content.ReadAsStringAsync();
        }

        // --- SUMMONER SPELL INJECTION ---

        public async Task<bool> SetSummonerSpellsAsync(int spell1Id, int spell2Id)
        {
            if (_credentials == null) return false;
            var payload = new { spell1Id = spell1Id, spell2Id = spell2Id };
            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient!.PatchAsync("/lol-champ-select/v1/session/my-selection", content);
            return response.IsSuccessStatusCode;
        }

        // --- RUNE INJECTION ---

        public async Task<bool> DeleteAllRunePagesAsync()
        {
            if (_credentials == null) return false;
            var response = await _httpClient!.DeleteAsync("/lol-perks/v1/pages");
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SetRunePageAsync(string name, int primaryStyleId, int subStyleId, int[] perkIds)
        {
            if (_credentials == null) return false;

            // 1. Delete all existing pages (LCU only allows a few custom pages)
            await DeleteAllRunePagesAsync();

            // 2. Create the new page
            var payload = new
            {
                name = name,
                primaryStyleId = primaryStyleId,
                subStyleId = subStyleId,
                selectedPerkIds = perkIds,
                current = true
            };

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient!.PostAsync("/lol-perks/v1/pages", content);
            
            return response.IsSuccessStatusCode;
        }

        // --- ITEM INJECTION ---

        public async Task<bool> SetItemBuildAsync(ChampionBuildPreset preset)
        {
            if (_credentials == null || string.IsNullOrEmpty(_credentials.InstallDirectory)) return false;

            try
            {
                var globalRecommendedPath = System.IO.Path.Combine(_credentials.InstallDirectory, "Config", "Global", "Recommended");
                if (!System.IO.Directory.Exists(globalRecommendedPath))
                {
                    System.IO.Directory.CreateDirectory(globalRecommendedPath);
                }

                var blocks = new System.Text.Json.Nodes.JsonArray();
                
                void AddBlock(string type, System.Collections.Generic.List<int> itemIds)
                {
                    if (itemIds == null || itemIds.Count == 0) return;
                    var items = new System.Text.Json.Nodes.JsonArray();
                    foreach (var id in itemIds)
                    {
                        items.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id.ToString(), ["count"] = 1 });
                    }
                    blocks.Add(new System.Text.Json.Nodes.JsonObject { ["type"] = type, ["items"] = items });
                }

                AddBlock("Starting Items", preset.StartingItemIds);
                AddBlock("Core Build Order", preset.CoreBuildOrderIds);
                AddBlock("Core Final Build", preset.CoreFinalBuildIds);
                AddBlock("Situational", preset.SituationalItemIds);
                AddBlock("Boots Options", preset.BootsOptionsIds);

                var newSet = new System.Text.Json.Nodes.JsonObject
                {
                    ["title"] = "[LoLHelper] " + preset.RunePreset.Name,
                    ["associatedChampions"] = new System.Text.Json.Nodes.JsonArray(preset.ChampionId > 0 ? preset.ChampionId : 1),
                    ["associatedMaps"] = new System.Text.Json.Nodes.JsonArray(11),
                    ["map"] = "any",
                    ["mode"] = "any",
                    ["type"] = "custom",
                    ["sortrank"] = 0,
                    ["blocks"] = blocks
                };

                if (preset.ChampionId == 0)
                {
                    newSet["associatedChampions"] = new System.Text.Json.Nodes.JsonArray();
                }

                var filePath = System.IO.Path.Combine(globalRecommendedPath, "LoLHelper.json");
                await System.IO.File.WriteAllTextAsync(filePath, newSet.ToJsonString());

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
