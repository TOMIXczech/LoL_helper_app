import requests, json

# Stats
stats_url = "https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats?queue_id=RANKED_SOLO_5X5&region=WORLD&columns=all_roles_total_num_games%2Cban_num_games%2Cchampion_ban_rate%2Cchampion_id%2Cchampion_role_pickrate%2Cchampion_role_tier%2Cchampion_role_tier_filtered%2Cgames%2Cindividual_position%2Cis_most_popular_role%2Cpatch%2Cpick_rate%2Cpostmatch_damage%2Cprevious_patch_stats%2Cqueue_id%2Cregion%2Crole_champion_pickrate%2Crole_pct_games%2Crole_total_num_games%2Cstrong_against%2Ctier%2Ctotal_num_games%2Cweak_against%2Cwin_rate&tier=EMERALD%2B&champion_id=30"
res = requests.get(stats_url)
data = res.json().get('data', [])
if data:
    row = [r for r in data if r['individual_position'] == 'BOTTOM'][0]
    print("previous_patch_stats:")
    print(json.dumps(row.get('previous_patch_stats'), indent=2))

# Builds
build_url = "https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds_tags?champion_id=30&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position=BOTTOM"
res_build = requests.get(build_url)
bdata = res_build.json().get('data', [])
if bdata:
    b = bdata[0]
    print("\nrunes:")
    print(json.dumps(b.get('runes'), indent=2))
    print("\ncoreItems:")
    print(json.dumps(b.get('coreItems'), indent=2))
