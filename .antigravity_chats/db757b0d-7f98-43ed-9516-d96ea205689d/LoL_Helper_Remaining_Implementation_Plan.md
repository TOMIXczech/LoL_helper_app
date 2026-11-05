## Goal Description
We are finalizing the implementation strategy for the remaining features of the LoL Helper application. We have already built the C# WPF Client, the basic Overlay, Pengu Loader integration, and Rune Injection. This plan outlines the technical steps to implement **Live Match Scouting**, the **Mobile Companion App**, and the **Live In-Game Overlay Data**.

## User Review Required
> [!IMPORTANT]
> **Riot API Key Required**: You elected to use a Riot Games Developer API Key for the Live Scouting. You will need to register for a personal API key at `developer.riotgames.com`. Without this key, the Live Scouting page will not be able to fetch ranks and winrates. 

## Open Questions
None! We have successfully resolved all design branches during our interview.

---

## Proposed Changes

### 1. External Data: Riot Web API (Live Scouting)
To populate the 10-player scouting screen, we will fetch data from Riot's global servers.
#### [NEW] `LoLHelper.Core/Services/RiotWebApiClient.cs`
- An `HttpClient` wrapper configured with a user-provided API key (`X-Riot-Token`).
- Methods to fetch `/lol/spectator/v5/active-games/by-summoner/{puuid}` to get the 10 players currently in your game.
- Methods to fetch `/lol/league/v4/entries/by-summoner/{summonerId}` to retrieve Solo/Duo ranks and winrates for those players.
#### [MODIFY] `LoLHelper.PCClient/Views/ScoutingView.xaml.cs`
- Connect the mock UI to the new `RiotWebApiClient` to display real-time ranks.

### 2. Local Communication: ASP.NET Core Minimal API (PC Server)
To allow the mobile app to control the PC client.
#### [MODIFY] `LoLHelper.PCClient.csproj`
- Change the SDK to `Microsoft.NET.Sdk.Web` or manually reference ASP.NET Core libraries so the WPF app can host a web server.
#### [NEW] `LoLHelper.PCClient/Services/LocalWebServer.cs`
- Starts a lightweight local HTTP listener on `0.0.0.0:8080`.
- Endpoints: 
  - `POST /api/queue/accept` (Tells the LCU to accept match).
  - `POST /api/champ/pick/{id}` (Tells the LCU to lock in a champ).
- WebSocket route: broadcasts current game state (In Lobby, In Queue, Champ Select) to the phone.

### 3. Mobile Companion App (.NET MAUI)
The iOS/Android remote control application.
#### [NEW] `LoLHelper.Mobile/` (New Project)
- A cross-platform .NET 9 MAUI app sharing the `LoLHelper.Core` project.
- Features a QR Code Scanner to quickly scan the PC app's screen and save the local IP address.
- Connects to the PC's WebSocket to display real-time queue status and champion select UI on the phone.

### 4. In-Game Data: Live Client Data API (Overlay)
To power the click-through overlay with real data during the match.
#### [NEW] `LoLHelper.Core/Services/LiveGameDataService.cs`
- Polls `https://127.0.0.1:2999/liveclientdata/allgamedata` (Riot's official in-game API).
- Extracts Gold Advantage, Dragon/Baron kills, and basic match timers.
#### [MODIFY] `LoLHelper.PCClient/OverlayWindow.xaml.cs`
- Binds to the `LiveGameDataService` to update the translucent UI elements in real-time.

---

## Verification Plan

### Automated Tests
- Run `dotnet build` on all three projects (`Core`, `PCClient`, and `Mobile`) to ensure the shared dependencies compile successfully.

### Manual Verification
1. **API Key Setup**: The user will paste their Riot Dev Key into the PC app settings. The app must successfully retrieve their own Solo/Duo rank.
2. **Local Server Routing**: The user will navigate to `http://localhost:8080/health` in their web browser to verify the WPF app is successfully hosting the Minimal API server.
3. **Mobile Pairing**: The user will compile the MAUI app to their local Android emulator/device and verify it successfully sends an "Accept Match" payload to the PC.
