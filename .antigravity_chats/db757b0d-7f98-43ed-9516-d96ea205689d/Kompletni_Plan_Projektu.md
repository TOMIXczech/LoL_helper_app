# Kompletní Plán Projektu: LoL Helper App

Tento dokument slouží jako hlavní architektonický a projektový plán pro aplikaci LoL Helper. Shrnuje vše, co již bylo úspěšně vyvinuto, a detailně popisuje kroky pro funkce, které zbývají dokončit.

## 🎯 Hlavní cíle projektu
1. **Žádný Electron:** Aplikace musí být rychlá, zkompilovaná (nativní) a zabírat naprosté minimum RAM. Proto byl zvolen jazyk **C#** a framework **WPF** (.NET 9).
2. **Plná ToS shoda (Vanguard Safe):** Aplikace nesmí číst ani zapisovat do paměti hry. Všechna data jsou získávána výhradně přes oficiální lokální nebo webová API Riot Games.
3. **Funkce jako Blitz.gg / Porofessor:** Sledování zápasu v reálném čase, automatické runy, herní overlay a pozápasové statistiky.
4. **Dálkové ovládání přes Mobil:** Doprovodná aplikace pro iOS a Android pro přijímání zápasů a výběr šampionů z gauče.
5. **Úprava klienta:** Možnost změnit vzhled League of Legends klienta pomocí Pengu Loaderu.

---

## ✅ FÁZE 1-4: Již Dokončeno (Aktuální stav)

### 1. Jádro a LCU Připojení
Aplikace umí automaticky sledovat soubor `lockfile` ve složce s hrou. Jakmile zapnete League of Legends, aplikace přečte heslo a port a bezpečně se připojí k internímu **LCU API** na adrese `127.0.0.1`. (Bypass SSL certifikátu byl úspěšně vyřešen). Ošetřili jsme také pády při zapínání hry.

### 2. Průhledný Herní Overlay
Vytvořili jsme speciální `OverlayWindow` ve WPF. Pomocí nativních **Win32 API** (`WS_EX_TRANSPARENT`) jsme okno udělali kompletně průchozí pro kliknutí myší. Okno sedí vždy navrchu a nezavazí při hraní. Vzhledově je stylizované jako čistý, poloprůhledný widget.

### 3. Automatické Runy (Data Dragon API)
Naprogramovali jsme dynamický stahovač dat z oficiálního Riot CDN (Data Dragon). Aplikace si sama stáhne nejnovější ID a obrázky run.
- Uživatelské rozhraní ukazuje krásné předvolby (např. *Bruiser (Conqueror)*).
- Po kliknutí aplikace přes LCU API smaže staré stránky run a **okamžitě vloží nové runy přímo do klienta**.

### 4. Customizace Přes Pengu Loader
Přidali jsme sekci "Theme/Settings", kde si uživatel může vybrat z počítače libovolný obrázek. Aplikace obrázek automaticky zkopíruje, vygeneruje správný CSS kód a nainstaluje ho jako plugin pro Pengu Loader (ve stylu ElainaV4).

---

## 🚀 FÁZE 5-8: Zbývající Části Plánu

### Fáze 5: Živé sledování hráčů (Live Match Scouting)
**Cíl:** Ukázat ranky (Solo/Duo) a procenta výher (winrates) všech 10 hráčů v lobby.
- **Zdroj dat:** Použijeme oficiální **Riot Developer Web API** (vyžaduje vložení vašeho API klíče v aplikaci).
- **Provedení:** 
  1. Z LCU API získáme ID 10 hráčů ve vaší probíhající hře.
  2. Přes Web API pošleme dotaz na koncový bod `/lol/league/v4/entries/by-summoner/{summonerId}`.
  3. Data zobrazíme v již připraveném UI `ScoutingView.xaml`.

### Fáze 6: Živá herní data pro Overlay (Live Client Data API)
**Cíl:** Oživit průhledný herní Overlay skutečnými daty během hry (zobrazení náskoku ve zlatě, zabití draků atd.).
- **Zdroj dat:** Během hry běží na portu `2999` lokální webový server přímo od Riotu (tzv. *Live Client Data API*).
- **Provedení:** Vytvoříme `LiveGameDataService.cs`, který se bude každé 2 vteřiny dotazovat na adresu `https://127.0.0.1:2999/liveclientdata/allgamedata`. Z JSON odpovědi vyčteme data a pošleme je do Overlay okna.

### Fáze 7: Mobilní doprovodná aplikace a PC Server
**Cíl:** Aplikace do mobilu, přes kterou můžete přijmout zápas nebo vybrat šampiona, i když zrovna nejste u klávesnice.
- **PC Strana:** Pomocí **ASP.NET Core Minimal APIs** spustíme ve WPF aplikaci lehký webový server na portu `8080`.
- **Mobilní Strana:** Vytvoříme nový projekt v **.NET MAUI** (lze kompilovat pro iOS i Android). Mobilní aplikace bude sdílet stejný kód (modely) jako PC aplikace.
- **Párování:** PC aplikace ukáže QR kód s vaší lokální Wi-Fi IP adresou. Mobil kód naskenuje, připojí se přes WebSocket a začne přijímat notifikace o stavu fronty.

### Fáze 8: Pozápasová Analýza (Post-Match Stats)
**Cíl:** Zobrazit grafy (KDA, CS/min, Poškození do šampionů) po skončení zápasu.
- **Zdroj dat:** Lokální LCU API po zápase nabízí kompletní statistiky o dohrané hře.
- **Provedení:** Vytěžíme data ihned po návratu z herní obrazovky do lobby a zakreslíme je do připravených vizuálních progress barů v `PostMatchView.xaml`.
