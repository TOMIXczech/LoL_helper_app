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

        public async Task<string> GetCurrentRankedStatsAsync()
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-ranked/v1/current-ranked-stats");
            if (!response.IsSuccessStatusCode) return string.Empty;
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetMatchHistoryAsync(string? puuid = null, int begIndex = 0, int endIndex = 19)
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync($"/lol-match-history/v1/products/lol/current-summoner/matches?begIndex={begIndex}&endIndex={endIndex}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            if (!string.IsNullOrEmpty(puuid))
            {
                var fallback = await _httpClient!.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}");
                if (fallback.IsSuccessStatusCode)
                    return await fallback.Content.ReadAsStringAsync();
            }

            return string.Empty;
        }

        public async Task<string> GetTopChampionMasteryAsync(long summonerId = 0)
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-champion-mastery/v1/local-player/champion-mastery");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync();

            if (summonerId > 0)
            {
                var fallback = await _httpClient!.GetAsync($"/lol-collections/v1/inventories/{summonerId}/champion-mastery/top?limit=5");
                if (fallback.IsSuccessStatusCode)
                    return await fallback.Content.ReadAsStringAsync();
            }

            return string.Empty;
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

        // --- AUTO ACCEPT & CHAMP SELECT AUTOMATION ENDPOINTS ---

        public async Task<string> GetReadyCheckAsync()
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-matchmaking/v1/ready-check");
            if (!response.IsSuccessStatusCode) return string.Empty;
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> AcceptReadyCheckAsync()
        {
            if (_credentials == null) return false;
            var response = await _httpClient!.PostAsync("/lol-matchmaking/v1/ready-check/accept", new StringContent("", System.Text.Encoding.UTF8, "application/json"));
            return response.IsSuccessStatusCode;
        }

        public async Task<string> GetLobbyAsync()
        {
            if (_credentials == null) return string.Empty;
            var response = await _httpClient!.GetAsync("/lol-lobby/v2/lobby");
            if (!response.IsSuccessStatusCode) return string.Empty;
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> PatchChampSelectActionAsync(int actionId, int championId, bool completed, string actionType = "ban")
        {
            if (completed)
            {
                var res = await CompleteChampSelectActionAsync(actionId, championId, actionType);
                return res.Success;
            }
            return await HoverChampSelectActionAsync(actionId, championId, actionType);
        }

        public async Task<(bool Success, string Log)> CompleteChampSelectActionAsync(int actionId, int championId, string actionType = "ban")
        {
            if (_credentials == null || _httpClient == null)
                return (false, "Not connected to LCU");

            var errors = new System.Collections.Generic.List<string>();

            // Attempt 1: PATCH /lol-champ-select/v1/session/actions/{id} with type and completed=true
            try
            {
                var payload = new { championId = championId, type = actionType, completed = true };
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var resp = await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content);
                if (resp.IsSuccessStatusCode)
                    return (true, $"PATCH actions/{actionId} with type succeeded (HTTP {(int)resp.StatusCode})");

                string body = await resp.Content.ReadAsStringAsync();
                errors.Add($"PATCH with type: HTTP {(int)resp.StatusCode} {body}");
            }
            catch (Exception ex)
            {
                errors.Add($"PATCH with type ex: {ex.Message}");
            }

            // Attempt 2: PATCH /lol-champ-select/v1/session/actions/{id} with completed=true (without type)
            try
            {
                var payload = new { championId = championId, completed = true };
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var resp = await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content);
                if (resp.IsSuccessStatusCode)
                    return (true, $"PATCH actions/{actionId} succeeded (HTTP {(int)resp.StatusCode})");

                string body = await resp.Content.ReadAsStringAsync();
                errors.Add($"PATCH without type: HTTP {(int)resp.StatusCode} {body}");
            }
            catch (Exception ex)
            {
                errors.Add($"PATCH without type ex: {ex.Message}");
            }

            // Attempt 3: POST /lol-champ-select/v1/session/actions/{id}/complete
            try
            {
                var hoverPayload = new { championId = championId, type = actionType };
                var hoverContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(hoverPayload), System.Text.Encoding.UTF8, "application/json");
                await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", hoverContent);

                var emptyContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                var resp = await _httpClient.PostAsync($"/lol-champ-select/v1/session/actions/{actionId}/complete", emptyContent);
                if (resp.IsSuccessStatusCode)
                    return (true, $"POST actions/{actionId}/complete succeeded (HTTP {(int)resp.StatusCode})");

                string body = await resp.Content.ReadAsStringAsync();
                errors.Add($"POST complete: HTTP {(int)resp.StatusCode} {body}");
            }
            catch (Exception ex)
            {
                errors.Add($"POST complete ex: {ex.Message}");
            }

            // Attempt 4: Fallback lobby team builder endpoints
            try
            {
                var payload = new { championId = championId, type = actionType, completed = true };
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var resp = await _httpClient.PatchAsync($"/lol-lobby-team-builder/champ-select/v1/session/actions/{actionId}", content);
                if (resp.IsSuccessStatusCode)
                    return (true, $"PATCH team-builder actions/{actionId} succeeded");

                var postResp = await _httpClient.PostAsync($"/lol-lobby-team-builder/champ-select/v1/session/actions/{actionId}/complete", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
                if (postResp.IsSuccessStatusCode)
                    return (true, $"POST team-builder complete succeeded");
            }
            catch { }

            return (false, string.Join("; ", errors));
        }

        public async Task<bool> HoverChampSelectActionAsync(int actionId, int championId, string actionType = "ban")
        {
            if (_credentials == null || _httpClient == null) return false;
            try
            {
                var payload = new { championId = championId, type = actionType, completed = false };
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var resp = await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", content);
                if (resp.IsSuccessStatusCode) return true;

                // Fallback without completed flag
                var fallbackPayload = new { championId = championId, type = actionType };
                var fallbackContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(fallbackPayload), System.Text.Encoding.UTF8, "application/json");
                var respFallback = await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", fallbackContent);
                if (respFallback.IsSuccessStatusCode) return true;

                // Fallback bare championId
                var barePayload = new { championId = championId };
                var bareContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(barePayload), System.Text.Encoding.UTF8, "application/json");
                var respBare = await _httpClient.PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", bareContent);
                return respBare.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
