import requests

champion_stats_url = "https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats?queue_id=RANKED_SOLO_5X5&region=WORLD&columns=all_roles_total_num_games%2Cban_num_games%2Cchampion_ban_rate%2Cchampion_id%2Cchampion_role_pickrate%2Cchampion_role_tier%2Cchampion_role_tier_filtered%2Cgames%2Cindividual_position%2Cis_most_popular_role%2Cpatch%2Cpick_rate%2Cpostmatch_damage%2Cprevious_patch_stats%2Cqueue_id%2Cregion%2Crole_champion_pickrate%2Crole_pct_games%2Crole_total_num_games%2Cstrong_against%2Ctier%2Ctotal_num_games%2Cweak_against%2Cwin_rate&tier=EMERALD%2B&champion_id=30"
res = requests.get(champion_stats_url)
print("Stats for Karthus (ID 30):")
print(res.json()['data'][0].keys() if res.json().get('data') else "No data")
for row in res.json().get('data', []):
    print(f"Role: {row.get('individual_position')}, WinRate: {row.get('win_rate')}, PickRate: {row.get('pick_rate')}, Matches: {row.get('games')}, Tier: {row.get('champion_role_tier')}")

builds_url = "https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id=30&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position=BOT"
res2 = requests.get(builds_url)
print("\nBuilds for Karthus BOT:")
data = res2.json()
print("Keys:", data.keys() if data else "No data")
print("Data length:", len(data.get('data', [])))
if data.get('data'):
    print("Sample build item:", list(data['data'][0].keys()))
