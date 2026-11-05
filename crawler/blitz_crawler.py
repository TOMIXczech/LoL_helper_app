import requests
import json
import time
import concurrent.futures
import os

class BlitzCrawler:
    def __init__(self):
        self.version = self._get_latest_version()
        self.champions = self._get_champions()
        self.items = self._get_items()
        self.runes = self._get_runes()
        self.summoners = self._get_summoners()
        
    def _get_latest_version(self):
        return requests.get('https://ddragon.leagueoflegends.com/api/versions.json').json()[0]
        
    def _get_champions(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/champion.json')
        data = res.json()['data']
        champs = {}
        for champ_key, champ_info in data.items():
            champs[int(champ_info['key'])] = champ_info['name']
        return champs
        
    def _get_items(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/item.json')
        data = res.json()['data']
        return {int(k): v['name'] for k, v in data.items()}
        
    def _get_runes(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/runesReforged.json')
        data = res.json()
        runes = {}
        for tree in data:
            runes[tree['id']] = tree['name']
            for slot in tree['slots']:
                for rune in slot['runes']:
                    runes[rune['id']] = rune['name']
        return runes
        
    def _get_summoners(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/summoner.json')
        data = res.json()['data']
        return {int(v['key']): v['name'] for k, v in data.items()}

    def get_champion_roles(self, champ_id):
        url = f"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats?queue_id=RANKED_SOLO_5X5&region=WORLD&tier=EMERALD%2B&champion_id={champ_id}&columns=all_roles_total_num_games%2Cban_num_games%2Cchampion_ban_rate%2Cchampion_id%2Cchampion_role_pickrate%2Cchampion_role_tier%2Cchampion_role_tier_filtered%2Cgames%2Cindividual_position%2Cis_most_popular_role%2Cpatch%2Cpick_rate%2Cpostmatch_damage%2Cprevious_patch_stats%2Cqueue_id%2Cregion%2Crole_champion_pickrate%2Crole_pct_games%2Crole_total_num_games%2Cstrong_against%2Ctier%2Ctotal_num_games%2Cweak_against%2Cwin_rate"
        res = requests.get(url)
        if res.status_code != 200:
            return []
        data = res.json().get('data', [])
        
        valid_roles = []
        for r in data:
            if r.get('individual_position') == 'ALL':
                continue
            if r.get('games', 0) > 0:
                valid_roles.append(r)
        return valid_roles
        
    def get_role_build(self, champ_id, role_name, matchup_id=None):
        url_std = f"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds?champion_id={champ_id}&queue_id=RANKED_SOLO_5X5&build_tier=Standard&individual_position={role_name}"
        url_otp = f"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_builds?champion_id={champ_id}&queue_id=RANKED_SOLO_5X5&is_otp=true&individual_position={role_name}"
        if matchup_id:
            url_std += f"&match_up_champion_id={matchup_id}"
            url_otp += f"&match_up_champion_id={matchup_id}"
            
        all_builds = []
        try:
            res = requests.get(url_std, timeout=10)
            if res.status_code == 200:
                data = res.json().get('data', [])
                all_builds.extend(data)
        except:
            pass
            
        try:
            res_otp = requests.get(url_otp, timeout=10)
            if res_otp.status_code == 200:
                data_otp = res_otp.json().get('data', [])
                all_builds.extend(data_otp)
        except:
            pass
            
        return all_builds

    def resolve_items(self, item_ids):
        if not item_ids:
            return []
        if isinstance(item_ids, str):
            parts = item_ids.split(',')
        elif isinstance(item_ids, list):
            parts = item_ids
        else:
            return []
        return [self.items.get(int(p), str(p)) for p in parts if str(p).isdigit()]
        
    def resolve_rune(self, rune_id):
        return self.runes.get(int(rune_id), str(rune_id))
        
    def resolve_summoners(self, summ_ids):
        return [self.summoners.get(int(s), str(s)) for s in summ_ids]

    def extract_builds(self, build_data_list):
        if not build_data_list:
            return []
            
        builds = []
        otp_counter = 1
        
        for bdata in build_data_list:
            if str(bdata.get('is_otp', '')).lower() == 'true':
                build_name = f"OTP Build {otp_counter}"
                otp_counter += 1
            else:
                build_name = " + ".join(bdata.get('name_tags', []))
                if not build_name:
                    build_name = "Standard"
                
            build_obj = {
                'build_name': build_name,
                'overall_win_rate': bdata.get('win_rate'),
                'overall_matches': bdata.get('games')
            }
            
            # Starting Items
            start_items_data = sorted(bdata.get('startingItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            if start_items_data:
                build_obj['starting_items'] = self.resolve_items(start_items_data[0].get('itemIds', []))
                build_obj['starting_items_matches'] = start_items_data[0].get('games')
                build_obj['starting_items_win_rate'] = start_items_data[0].get('win_rate')
                
            # Situational Items (Top 5)
            sit_items = sorted(bdata.get('situationalItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            if sit_items:
                situational_list = []
                for si in sit_items[:5]:
                    item_name = self.items.get(int(si.get('itemId')), str(si.get('itemId')))
                    situational_list.append({
                        "item": item_name,
                        "matches": si.get('games'),
                        "win_rate": si.get('win_rate')
                    })
                build_obj['situational_items'] = situational_list
            
            # Top Core Items
            core_items = sorted(bdata.get('coreItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            if core_items:
                build_obj['core_items'] = self.resolve_items(core_items[0].get('itemIds', ''))
                build_obj['core_items_win_rate'] = core_items[0].get('win_rate')
                
                # Component Build Order for these Core Items
                component_orders = {}
                item_orders = bdata.get('itemOrders') or {}
                core_ids_str = core_items[0].get('itemIds', '')
                core_ids = [p for p in str(core_ids_str).split(',') if p.isdigit()]
                for cid in core_ids:
                    cname = self.items.get(int(cid), str(cid))
                    if cid in item_orders:
                        top_path = sorted(item_orders[cid], key=lambda x: x.get('games', 0), reverse=True)
                        if top_path:
                            comps = top_path[0].get('items_order', [])
                            component_orders[cname] = [self.items.get(int(p), str(p)) for p in comps]
                if component_orders:
                    build_obj['core_items_components'] = component_orders
                
            # Top Runes
            keystones = sorted(bdata.get('keystone') or [], key=lambda x: x.get('games', 0), reverse=True)
            rune_setup = {}
            if keystones:
                rune_setup['keystone'] = self.resolve_rune(keystones[0].get('keystone_id'))
                rune_setup['keystone_win_rate'] = keystones[0].get('win_rate')
                
            runes_list = bdata.get('runes') or []
            indices = set(r.get('index') for r in runes_list if r.get('index') is not None)
            other_runes = []
            for idx in indices:
                slot_runes = sorted([r for r in runes_list if r.get('index') == idx], key=lambda x: x.get('games', 0), reverse=True)
                if slot_runes:
                    other_runes.append(self.resolve_rune(slot_runes[0].get('runeId')))
            if other_runes:
                rune_setup['secondary_and_other'] = other_runes
                
            build_obj['runes'] = rune_setup
            
            # Top Summoner Spells
            summoners = sorted(bdata.get('summonerSpells') or [], key=lambda x: x.get('games', 0), reverse=True)
            if summoners:
                build_obj['summoner_spells'] = self.resolve_summoners(summoners[0].get('summonerSpellIds', []))
                build_obj['summoner_spells_win_rate'] = summoners[0].get('win_rate')
                
            builds.append(build_obj)
            
        return builds

    def fetch_matchup_wrapper(self, champ_id, role, m_id, m_name):
        m_build_data_list = self.get_role_build(champ_id, role, matchup_id=m_id)
        if m_build_data_list:
            m_builds = self.extract_builds(m_build_data_list)
            if m_builds:
                return m_name, m_builds
        return m_name, None

    def crawl(self, output_file=None, fetch_matchups=True, max_matchups=None):
        if output_file is None:
            output_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'blitz_champions_data.json')
            
        results = {}
        total = len(self.champions)
        count = 0
        
        MAX_WORKERS = 20
        
        for champ_id, champ_name in self.champions.items():
            count += 1
            print(f"[{count}/{total}] Crawling {champ_name}...")
            
            roles_data = self.get_champion_roles(champ_id)
            champ_dict = {}
            
            for role_obj in roles_data:
                role = role_obj.get('individual_position')
                
                tier = role_obj.get('champion_role_tier')
                win_rate = role_obj.get('win_rate')
                pick_rate = role_obj.get('pick_rate')
                ban_rate = role_obj.get('champion_ban_rate')
                matches = role_obj.get('games')
                
                prev_stats = role_obj.get('previous_patch_stats', {})
                prev_win_rate = prev_stats.get('win_rate') if prev_stats else None
                win_rate_change = (win_rate - prev_win_rate) if (win_rate is not None and prev_win_rate is not None) else None
                
                build_data_list = self.get_role_build(champ_id, role)
                general_builds = self.extract_builds(build_data_list)
                
                matchups_dict = {}
                if fetch_matchups:
                    opponents = list(self.champions.items())
                    if max_matchups:
                        opponents = opponents[:max_matchups]
                        
                    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
                        futures = []
                        for m_id, m_name in opponents:
                            futures.append(executor.submit(self.fetch_matchup_wrapper, champ_id, role, m_id, m_name))
                            
                        for future in concurrent.futures.as_completed(futures):
                            m_name, m_builds = future.result()
                            if m_builds:
                                matchups_dict[m_name] = m_builds
                                
                champ_dict[role] = {
                    "Tier": tier,
                    "Win Rate": win_rate,
                    "Win Rate Change": win_rate_change,
                    "Pick Rate": pick_rate,
                    "Ban Rate": ban_rate,
                    "Matches": matches,
                    "Builds": general_builds,
                    "Matchups": matchups_dict if fetch_matchups else "Matchups disabled"
                }
                
            results[champ_name] = champ_dict
            
            with open(output_file, 'w', encoding='utf-8') as f:
                json.dump(results, f, indent=2, ensure_ascii=False)
            
        print(f"\nDone! Data saved to {output_file}")

if __name__ == "__main__":
    crawler = BlitzCrawler()
    print("Starting fast concurrent crawl...")
    crawler.crawl(fetch_matchups=True, max_matchups=None)
