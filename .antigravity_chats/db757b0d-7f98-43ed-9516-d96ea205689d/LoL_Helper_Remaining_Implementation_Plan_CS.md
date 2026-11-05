## Popis Cíle (Goal Description)
Dokončujeme implementační strategii pro zbývající funkce aplikace LoL Helper. Již jsme vytvořili C# WPF klienta, základní herní překryv (Overlay), integraci Pengu Loaderu a automatické vkládání run. Tento plán popisuje technické kroky k implementaci **Živého sledování zápasu (Live Scouting)**, **Mobilní doprovodné aplikace** a **Živých dat pro herní překryv**.

## Vyžadována kontrola uživatelem (User Review Required)
> [!IMPORTANT]
> **Vyžadován Riot API klíč**: Pro živé sledování zápasu jste si vybrali použití vývojářského klíče Riot Games (Developer API Key). Budete se muset zaregistrovat a získat osobní API klíč na portálu `developer.riotgames.com`. Bez tohoto klíče nebude stránka pro sledování zápasu schopna načítat ranky a procenta výher (winrates).

## Otevřené otázky (Open Questions)
Žádné! Během našeho rozhovoru jsme úspěšně vyřešili všechny větve návrhu.

---

## Navrhované změny (Proposed Changes)

### 1. Externí data: Riot Web API (Živé sledování zápasu)
Abychom mohli naplnit obrazovku sledování 10 hráčů, budeme stahovat data z globálních serverů Riotu.
#### [NEW] `LoLHelper.Core/Services/RiotWebApiClient.cs`
- Obalovací třída `HttpClient` nakonfigurovaná s API klíčem poskytnutým uživatelem (`X-Riot-Token`).
- Metody pro volání `/lol/spectator/v5/active-games/by-summoner/{puuid}`, které získají 10 hráčů aktuálně hrajících ve vaší hře.
- Metody pro volání `/lol/league/v4/entries/by-summoner/{summonerId}`, které získají Solo/Duo ranky a procenta výher pro tyto hráče.
#### [MODIFY] `LoLHelper.PCClient/Views/ScoutingView.xaml.cs`
- Propojení existujícího uživatelského rozhraní s novým `RiotWebApiClient` pro zobrazení reálných ranků.

### 2. Lokální komunikace: ASP.NET Core Minimal API (PC Server)
Aby mobilní aplikace mohla ovládat PC klienta.
#### [MODIFY] `LoLHelper.PCClient.csproj`
- Změna SDK na `Microsoft.NET.Sdk.Web` nebo ruční přidání referencí na ASP.NET Core knihovny, aby WPF aplikace mohla hostovat webový server.
#### [NEW] `LoLHelper.PCClient/Services/LocalWebServer.cs`
- Spustí lehký lokální HTTP server na adrese `0.0.0.0:8080`.
- Koncové body (Endpoints): 
  - `POST /api/queue/accept` (Řekne LCU, aby přijal zápas).
  - `POST /api/champ/pick/{id}` (Řekne LCU, aby vybral šampiona).
- WebSocket cesta: Bude vysílat aktuální stav hry (V lobby, Ve frontě, Výběr šampionů) do telefonu.

### 3. Mobilní doprovodná aplikace (.NET MAUI)
Aplikace pro dálkové ovládání pro iOS/Android.
#### [NEW] `LoLHelper.Mobile/` (Nový projekt)
- Multiplatformní .NET 9 MAUI aplikace sdílející projekt `LoLHelper.Core`.
- Obsahuje čtečku QR kódů pro rychlé naskenování obrazovky PC aplikace a uložení lokální IP adresy.
- Připojuje se k WebSocketu na PC pro zobrazení stavu fronty v reálném čase a uživatelského rozhraní pro výběr šampionů na telefonu.

### 4. Data ze hry: Live Client Data API (Herní překryv / Overlay)
Pro napájení průhledného herního překryvu skutečnými daty během zápasu.
#### [NEW] `LoLHelper.Core/Services/LiveGameDataService.cs`
- Pravidelně dotazuje `https://127.0.0.1:2999/liveclientdata/allgamedata` (Oficiální herní API Riotu).
- Extrahuje výhodu v goldech, zabití Draků/Baronů a základní časovače zápasu.
#### [MODIFY] `LoLHelper.PCClient/OverlayWindow.xaml.cs`
- Napojí se na `LiveGameDataService` a v reálném čase aktualizuje průhledné prvky uživatelského rozhraní.

---

## Plán ověření (Verification Plan)

### Automatizované testy
- Spuštění `dotnet build` na všech třech projektech (`Core`, `PCClient` a `Mobile`), abychom zajistili, že se sdílené závislosti úspěšně zkompilují.

### Manuální ověření
1. **Nastavení API klíče**: Uživatel vloží svůj Riot Dev klíč do nastavení PC aplikace. Aplikace musí úspěšně načíst jeho vlastní Solo/Duo rank.
2. **Směrování lokálního serveru**: Uživatel přejde ve webovém prohlížeči na `http://localhost:8080/health`, aby ověřil, že WPF aplikace úspěšně hostuje Minimal API server.
3. **Párování mobilu**: Uživatel zkompiluje MAUI aplikaci do svého lokálního Android emulátoru/zařízení a ověří, že úspěšně odešle požadavek "Přijmout zápas" (Accept Match) do PC.
