import requests
import json
import time
import concurrent.futures
import os

BOOT_IDS = {1001, 3005, 3006, 3009, 3010, 3013, 3020, 3047, 3111, 3117, 3158, 3170, 3171, 3172, 3173, 3174, 3175}

class BlitzCrawler:
    def __init__(self):
        self.version = self._get_latest_version()
        self.champions = self._get_champions()
        self.items = self._get_items()
        self.runes, self.rune_to_tree, self.rune_to_slot = self._get_runes()
        self.summoners = self._get_summoners()

    def _get_latest_version(self):
        return requests.get('https://ddragon.leagueoflegends.com/api/versions.json', timeout=10).json()[0]

    def _get_champions(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/champion.json', timeout=10)
        data = res.json()['data']
        champs = {}
        for champ_key, champ_info in data.items():
            champs[int(champ_info['key'])] = champ_info['name']
        return champs

    def _get_items(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/item.json', timeout=10)
        data = res.json()['data']
        return {int(k): v['name'] for k, v in data.items() if k.isdigit()}

    def _get_runes(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/runesReforged.json', timeout=10)
        data = res.json()
        runes = {}
        rune_to_tree = {}
        rune_to_slot = {}
        for tree in data:
            tid = int(tree['id'])
            runes[tid] = tree['name']
            for s_idx, slot in enumerate(tree['slots']):
                for rune in slot['runes']:
                    rid = int(rune['id'])
                    runes[rid] = rune['name']
                    rune_to_tree[rid] = tid
                    rune_to_slot[rid] = s_idx
        return runes, rune_to_tree, rune_to_slot

    def _get_summoners(self):
        res = requests.get(f'https://ddragon.leagueoflegends.com/cdn/{self.version}/data/en_US/summoner.json', timeout=10)
        data = res.json()['data']
        return {int(v['key']): v['name'] for k, v in data.items()}

    def format_patch(self, raw_patch):
        if not raw_patch:
            return "26.22"
        parts = str(raw_patch).split('.')
        if len(parts) >= 2 and parts[0].isdigit():
            major = int(parts[0])
            if major == 16:
                major = 26
            return f"{major}.{parts[1]}"
        return str(raw_patch)

    def get_champion_roles(self, champ_id, tier="EMERALD%2B", region="WORLD"):
        url = (
            f"https://data.v2.iesdev.com/api/v1/query_objects/prod/lol/champion_postmatch_stats"
            f"?queue_id=RANKED_SOLO_5X5&region={region}&tier={tier}&champion_id={champ_id}"
            f"&columns=all_roles_total_num_games%2Cban_num_games%2Cchampion_ban_rate%2Cchampion_id"
            f"%2Cchampion_role_pickrate%2Cchampion_role_tier%2Cchampion_role_tier_filtered%2Cgames"
            f"%2Cindividual_position%2Cis_most_popular_role%2Cpatch%2Cpick_rate%2Cpostmatch_damage"
            f"%2Cprevious_patch_stats%2Cqueue_id%2Cregion%2Crole_champion_pickrate%2Crole_pct_games"
            f"%2Crole_total_num_games%2Cstrong_against%2Ctier%2Ctotal_num_games%2Cweak_against%2Cwin_rate"
        )
        try:
            res = requests.get(url, timeout=10)
            if res.status_code != 200:
                return []
            data = res.json().get('data', [])
        except Exception:
            return []

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
        except Exception:
            pass

        try:
            res_otp = requests.get(url_otp, timeout=10)
            if res_otp.status_code == 200:
                data_otp = res_otp.json().get('data', [])
                all_builds.extend(data_otp)
        except Exception:
            pass

        return all_builds

    def resolve_items(self, item_ids):
        ids = self.parse_item_ids(item_ids)
        return [self.items.get(i, str(i)) for i in ids]

    def parse_item_ids(self, item_ids):
        if not item_ids:
            return []
        if isinstance(item_ids, str):
            parts = item_ids.split(',')
        elif isinstance(item_ids, list):
            parts = item_ids
        else:
            return []
        return [int(p) for p in parts if str(p).strip().isdigit()]

    def resolve_rune(self, rune_id):
        if rune_id is None:
            return ""
        return self.runes.get(int(rune_id), str(rune_id))

    def resolve_summoners(self, summ_ids):
        return [self.summoners.get(int(s), str(s)) for s in (summ_ids or []) if str(s).isdigit()]

    def extract_builds(self, build_data_list):
        if not build_data_list:
            return []

        builds = []
        used_otp_puuids = set()
        otp_counter = 1

        for bdata in build_data_list:
            is_otp = str(bdata.get('is_otp', '')).lower() == 'true'
            raw_wr = bdata.get('win_rate') or 0.5
            adj_wr = bdata.get('adjusted_win_rate') or raw_wr
            wr_offset = raw_wr - adj_wr

            otp_player_info = None
            if is_otp:
                otp_players = bdata.get('otp_players') or []
                sorted_otps = sorted(
                    otp_players,
                    key=lambda p: (
                        1 if p.get('region') in ('KR', 'EUW1', 'NA1') else 0,
                        p.get('score') or 0,
                        p.get('overall_games') or 0
                    ),
                    reverse=True
                )
                chosen = None
                for p in sorted_otps:
                    puuid = p.get('participant_puuid') or f"{p.get('name')}#{p.get('tag')}"
                    if puuid not in used_otp_puuids:
                        used_otp_puuids.add(puuid)
                        chosen = p
                        break
                if not chosen and sorted_otps:
                    chosen = sorted_otps[0]

                if chosen:
                    p_name = chosen.get('name') or f"OTP Player {otp_counter}"
                    p_tag = chosen.get('tag') or ""
                    build_name = f"{p_name} #{p_tag}" if p_tag else p_name
                    otp_player_info = {
                        "name": p_name,
                        "tag": p_tag,
                        "region": chosen.get('region') or "KR",
                        "rank_tier": chosen.get('rank_tier') or "MASTER",
                        "rank_division": chosen.get('rank_division') or "I",
                        "lp": chosen.get('lp') or 0,
                        "overall_games": chosen.get('overall_games') or bdata.get('games') or 1,
                        "overall_wr": chosen.get('overall_wr') or adj_wr
                    }
                else:
                    build_name = f"OTP Build {otp_counter}"
                otp_counter += 1
            else:
                tags = bdata.get('name_tags') or []
                build_name = " / ".join(tags) if tags else "Standard"

            build_obj = {
                'build_name': build_name,
                'is_otp': is_otp,
                'overall_win_rate': adj_wr,
                'raw_win_rate': raw_wr,
                'overall_matches': bdata.get('games') or 0
            }
            if otp_player_info:
                build_obj['otp_player'] = otp_player_info

            # Starting Items
            start_items_data = sorted(bdata.get('startingItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            if start_items_data:
                start_ids = self.parse_item_ids(start_items_data[0].get('itemIds', []))
                build_obj['starting_item_ids'] = start_ids
                build_obj['starting_items'] = [self.items.get(i, str(i)) for i in start_ids]
                build_obj['starting_items_matches'] = start_items_data[0].get('games')
                build_obj['starting_items_win_rate'] = start_items_data[0].get('win_rate')

            # Core Items & Component Build Order
            core_items = sorted(bdata.get('coreItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            core_ids = []
            build_order_ids = []
            if core_items:
                core_ids = self.parse_item_ids(core_items[0].get('itemIds', ''))
                build_obj['core_item_ids'] = core_ids
                build_obj['core_items'] = [self.items.get(i, str(i)) for i in core_ids]
                build_obj['core_items_win_rate'] = core_items[0].get('win_rate')

                component_orders = {}
                item_orders = bdata.get('itemOrders') or {}
                for cid in core_ids:
                    cid_str = str(cid)
                    cname = self.items.get(cid, cid_str)
                    if cid_str in item_orders and item_orders[cid_str]:
                        top_path = sorted(item_orders[cid_str], key=lambda x: x.get('games', 0), reverse=True)
                        if top_path:
                            comps = [int(p) for p in (top_path[0].get('items_order') or []) if str(p).isdigit()]
                            component_orders[cname] = [self.items.get(p, str(p)) for p in comps]
                            build_order_ids.extend(comps)
                    build_order_ids.append(cid)
                if component_orders:
                    build_obj['core_items_components'] = component_orders
                if build_order_ids:
                    build_obj['build_order_ids'] = build_order_ids

            # Completed Items (6 items: 3 core + top 3 non-boot situational) & Situational Items (next 7)
            sit_items_sorted = sorted(bdata.get('situationalItems') or [], key=lambda x: x.get('games', 0), reverse=True)
            completed_ids = list(core_ids)
            has_boots = any(cid in BOOT_IDS for cid in completed_ids)

            remaining_sit = []
            for si in sit_items_sorted:
                raw_id = si.get('itemId')
                if raw_id is None or not str(raw_id).isdigit():
                    continue
                iid = int(raw_id)
                if iid in completed_ids:
                    continue
                if has_boots and iid in BOOT_IDS:
                    continue
                if iid in BOOT_IDS:
                    has_boots = True
                if len(completed_ids) < 6:
                    completed_ids.append(iid)
                else:
                    remaining_sit.append(si)

            build_obj['completed_item_ids'] = completed_ids
            situational_list = []
            for si in remaining_sit[:7]:
                iid = int(si.get('itemId'))
                item_name = self.items.get(iid, str(iid))
                situational_list.append({
                    "id": iid,
                    "item": item_name,
                    "matches": si.get('games'),
                    "win_rate": si.get('win_rate')
                })
            build_obj['situational_items'] = situational_list

            # Shards
            shards_obj = bdata.get('shards') or {}
            shard_ids = []
            for shard_key, default_shard in (('offenseShard', 5008), ('flexShard', 5008), ('defenseShard', 5001)):
                s_list = sorted(shards_obj.get(shard_key) or [], key=lambda x: x.get('games', 0), reverse=True)
                if s_list and str(s_list[0].get('shard_id', '')).isdigit():
                    shard_ids.append(int(s_list[0].get('shard_id')))
                else:
                    shard_ids.append(default_shard)

            # Runes & Multiple Selectable Rune Options (1 to 3 based on >= 5% build pick rate rule)
            keystones = sorted(bdata.get('keystone') or [], key=lambda x: x.get('games', 0), reverse=True)
            runes_list = bdata.get('runes') or []
            total_b_games = bdata.get('games') or 0
            min_ks_games = max(1, int(total_b_games * 0.05))
            top_keystones = [k for k in keystones if (k.get('games') or 0) >= min_ks_games][:3]
            if not top_keystones and keystones:
                top_keystones = [keystones[0]]

            rune_options = []
            for ks_obj in top_keystones:
                ks_id = int(ks_obj.get('keystone_id') or 8010)
                p_tree_id = int(ks_obj.get('treeId') or self.rune_to_tree.get(ks_id, 8000))
                ks_wr = max(0.0, min(1.0, (ks_obj.get('win_rate') or raw_wr) - wr_offset))
                ks_matches = ks_obj.get('games') or total_b_games

                primary_perks = []
                for idx in (0, 1, 2):
                    candidates = [
                        r for r in runes_list
                        if r.get('index') == idx and int(r.get('treeId') or 0) == p_tree_id
                    ]
                    if not candidates:
                        candidates = [r for r in runes_list if r.get('index') == idx]
                    candidates.sort(key=lambda x: x.get('games', 0), reverse=True)
                    if candidates:
                        primary_perks.append(int(candidates[0].get('runeId')))

                sec_candidates_3 = [
                    r for r in runes_list
                    if r.get('index') == 3 and int(r.get('treeId') or 0) not in (0, p_tree_id)
                ]
                sec_candidates_3.sort(key=lambda x: x.get('games', 0), reverse=True)
                s_tree_id = 8400 if p_tree_id != 8400 else 8000
                sec_perks = []
                if sec_candidates_3:
                    sec1_id = int(sec_candidates_3[0].get('runeId'))
                    s_tree_id = int(sec_candidates_3[0].get('treeId') or self.rune_to_tree.get(sec1_id, s_tree_id))
                    sec_perks.append(sec1_id)
                    sec1_slot = self.rune_to_slot.get(sec1_id, -1)

                    sec_candidates_4 = [
                        r for r in runes_list
                        if r.get('index') == 4
                        and int(r.get('treeId') or 0) == s_tree_id
                        and int(r.get('runeId') or 0) != sec1_id
                        and self.rune_to_slot.get(int(r.get('runeId') or 0), -2) != sec1_slot
                    ]
                    sec_candidates_4.sort(key=lambda x: x.get('games', 0), reverse=True)
                    if sec_candidates_4:
                        sec_perks.append(int(sec_candidates_4[0].get('runeId')))

                other_runes_ids = primary_perks + sec_perks
                rune_options.append({
                    'keystone': self.resolve_rune(ks_id),
                    'keystone_id': ks_id,
                    'keystone_win_rate': ks_wr,
                    'keystone_matches': ks_matches,
                    'primary_style_id': p_tree_id,
                    'sub_style_id': s_tree_id,
                    'perk_ids': [ks_id] + other_runes_ids + shard_ids,
                    'secondary_and_other': [self.resolve_rune(rid) for rid in other_runes_ids]
                })

            if rune_options:
                build_obj['runes'] = rune_options[0]
                build_obj['rune_options'] = rune_options

            # Summoner Spells (Top 2 options)
            summoners = sorted(bdata.get('summonerSpells') or [], key=lambda x: x.get('games', 0), reverse=True)
            if summoners:
                top_s_ids = [int(s) for s in (summoners[0].get('summonerSpellIds') or [4, 14]) if str(s).isdigit()]
                build_obj['summoner_spell_ids'] = top_s_ids
                build_obj['summoner_spells'] = self.resolve_summoners(top_s_ids)
                build_obj['summoner_spells_win_rate'] = max(0.0, min(1.0, (summoners[0].get('win_rate') or raw_wr) - wr_offset))

                spell_options = []
                for s_entry in summoners[:2]:
                    s_ids = [int(s) for s in (s_entry.get('summonerSpellIds') or []) if str(s).isdigit()]
                    if len(s_ids) >= 2:
                        s_wr = max(0.0, min(1.0, (s_entry.get('win_rate') or raw_wr) - wr_offset))
                        spell_options.append({
                            "ids": s_ids[:2],
                            "spells": self.resolve_summoners(s_ids[:2]),
                            "win_rate": s_wr,
                            "matches": s_entry.get('games') or 0
                        })
                build_obj['summoner_spells_options'] = spell_options

            # Skill Orders (Ability Max Order)
            skill_orders = sorted(bdata.get('skillOrders') or [], key=lambda x: x.get('games', 0), reverse=True)
            if skill_orders:
                top_so = skill_orders[0]
                build_obj['skill_order'] = {
                    "order": [int(x) for x in (top_so.get('skillOrder') or []) if str(x).isdigit()],
                    "win_rate": top_so.get('win_rate') or 0.5,
                    "matches": top_so.get('games') or 0
                }

            builds.append(build_obj)

        return builds

    def fetch_matchup_wrapper(self, champ_id, role, m_id, m_name):
        m_build_data_list = self.get_role_build(champ_id, role, matchup_id=m_id)
        if m_build_data_list:
            m_builds = self.extract_builds(m_build_data_list)
            if m_builds:
                return m_name, m_builds
        return m_name, None

    def crawl_single_champion(self, champ_id, champ_name, existing_champ_data=None, fetch_matchups=False, max_matchups=None):
        roles_data = self.get_champion_roles(champ_id)
        champ_dict = {}
        for role_obj in roles_data:
            role = role_obj.get('individual_position')
            tier = role_obj.get('champion_role_tier')
            win_rate = role_obj.get('win_rate')
            pick_rate = role_obj.get('pick_rate')
            ban_rate = role_obj.get('champion_ban_rate')
            matches = role_obj.get('games')
            patch = self.format_patch(role_obj.get('patch'))
            is_most_popular = bool(role_obj.get('is_most_popular_role'))

            prev_stats = role_obj.get('previous_patch_stats') or {}
            prev_win_rate = prev_stats.get('win_rate') if isinstance(prev_stats, dict) else None
            win_rate_change = (win_rate - prev_win_rate) if (win_rate is not None and prev_win_rate is not None) else 0.0

            build_data_list = self.get_role_build(champ_id, role)
            general_builds = self.extract_builds(build_data_list)

            matchups_dict = {}
            if fetch_matchups:
                opponents = list(self.champions.items())
                if max_matchups:
                    opponents = opponents[:max_matchups]
                with concurrent.futures.ThreadPoolExecutor(max_workers=20) as executor:
                    futures = [
                        executor.submit(self.fetch_matchup_wrapper, champ_id, role, m_id, m_name)
                        for m_id, m_name in opponents
                    ]
                    for future in concurrent.futures.as_completed(futures):
                        m_name, m_builds = future.result()
                        if m_builds:
                            matchups_dict[m_name] = m_builds
            elif existing_champ_data and role in existing_champ_data and isinstance(existing_champ_data[role].get('Matchups'), dict):
                matchups_dict = existing_champ_data[role]['Matchups']

            champ_dict[role] = {
                "Patch": patch,
                "IsMostPopular": is_most_popular,
                "Tier": tier,
                "Win Rate": win_rate,
                "Win Rate Change": win_rate_change,
                "Pick Rate": pick_rate,
                "Ban Rate": ban_rate,
                "Matches": matches,
                "Builds": general_builds,
                "Matchups": matchups_dict
            }
        return champ_name, champ_dict

    def crawl(self, output_file=None, fetch_matchups=False, max_matchups=None):
        if output_file is None:
            output_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'blitz_champions_data.json')

        existing_data = {}
        if os.path.exists(output_file):
            try:
                with open(output_file, 'r', encoding='utf-8') as f:
                    existing_data = json.load(f)
            except Exception:
                existing_data = {}

        results = dict(existing_data)
        total = len(self.champions)
        completed = 0

        with concurrent.futures.ThreadPoolExecutor(max_workers=16) as executor:
            futures = {
                executor.submit(
                    self.crawl_single_champion,
                    champ_id,
                    champ_name,
                    existing_data.get(champ_name),
                    fetch_matchups,
                    max_matchups
                ): champ_name
                for champ_id, champ_name in self.champions.items()
            }
            for future in concurrent.futures.as_completed(futures):
                completed += 1
                champ_name = futures[future]
                try:
                    c_name, c_dict = future.result()
                    if c_dict:
                        results[c_name] = c_dict
                    print(f"[{completed}/{total}] Updated {c_name} ({len(c_dict)} roles)")
                except Exception as ex:
                    print(f"[{completed}/{total}] Error on {champ_name}: {ex}")

        with open(output_file, 'w', encoding='utf-8') as f:
            json.dump(results, f, separators=(',', ':'), ensure_ascii=False)

        print(f"\nDone! Data saved to {output_file}")

if __name__ == "__main__":
    crawler = BlitzCrawler()
    print("Updating blitz_champions_data.json with full Blitz build, OTP, skill order, and stats data...")
    crawler.crawl(fetch_matchups=False)
