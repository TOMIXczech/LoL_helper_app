# League of Legends Helper App - Architecture & Implementation Plan

This document outlines the complete technical plan for building a lightweight, RAM-efficient League of Legends helper application, including PC client customizations and a remote-control mobile companion app. **It operates entirely as a local client application without any custom backend servers.**

## 1. System Architecture

The system consists of a native Windows PC client and a mobile companion app. It relies exclusively on the user's running League Client to fetch data, entirely bypassing the need for a Riot Developer API Key or proxy servers.

```mermaid
flowchart TD
    subgraph Local PC
        Client[C# Native PC Client]
        LoL[League of Legends Client (LCU)]
        Game[League of Legends Game (In-Game)]
        Overlay[Transparent Overlay Window]
        Pengu[Pengu Loader Subsystem]
        
        Client <-->|1. Local REST / WS (Piggyback auth)| LoL
        Client -->|2. Polling (Live Data)| Game
        Client -->|3. Window Management| Overlay
        Client -->|4. Inject JS/CSS| Pengu
        Pengu -->|5. Modifies| LoL
    end

    subgraph Mobile Device (Local WiFi)
        Mobile[Mobile App iOS/Android]
    end
    
    subgraph Riot Games
        RiotServers[Riot Internal Servers]
    end

    Mobile <-->|6. Local WebSocket| Client
    LoL <-->|7. Official Client Traffic| RiotServers
```

## 2. Technology Stack

### The PC Client (Frontend & Core Logic)
*   **Language & UI Framework:** C# with WPF (Windows Presentation Foundation) or WinUI 3. 
*   **Overlay Tech:** Native Windows transparent borders (Win32 API `WS_EX_TRANSPARENT`, `WS_EX_TOPMOST`). Click-through window floating over the game.
*   **Client Customization:** **Pengu Loader** integration. The C# app will manage Pengu Loader, injecting custom JavaScript and CSS into the Chromium-embedded League Client to alter its theme and UI.

### The Mobile App (Companion)
*   **Framework:** **.NET MAUI** (Multi-platform App UI) or React Native.
*   **Connectivity:** Connects to the PC client via a local network (Wi-Fi) WebSocket or HTTP server hosted by the PC client.

## 3. API & Data Strategy (Serverless & Proxy-Free)

Because we are not using a proxy server and cannot embed a secret Riot API Key into the app, we will use a technique known as **LCU Piggybacking**.

1.  **League Client Update (LCU) API:** 
    *   Normally used just for local state (like Champ Select), the LCU also has undocumented internal endpoints (e.g., `/lol-match-history/v1/` or `/lol-summoner/v1/`).
    *   By querying the LCU on localhost, we force the *official League Client* to fetch global data (win rates, match history) from Riot using the player's own secure authentication token.
    *   **Advantage:** No API Key needed, no servers needed, fully compliant (Riot allows LCU interaction).
2.  **Live Client Data API:** 
    *   Available at `https://127.0.0.1:2999/liveclientdata/allgamedata` during an active match. Provides live gold, level timers, and drake/baron kills for the overlay.
3.  **Local PC Relay Server:** 
    *   The C# PC app will host a lightweight local WebSocket server. The mobile app connects to this to send commands ("Start Queue", "Lock Yasuo") which the PC client forwards to the LCU.

## 4. Development Roadmap

### Phase 1: The PC Foundation
*   **Task 1:** Set up the C# WPF/WinUI 3 project.
*   **Task 2:** Write the `LockfileWatcher` to connect to the League Client (LCU) securely.
*   **Task 3:** Fetch basic player data and match history via LCU internal routing (piggybacking).

### Phase 2: Mobile Companion App & Remote Control
*   **Task 4:** Add a local HTTP/WebSocket server to the PC C# app to accept connections.
*   **Task 5:** Create the .NET MAUI mobile app (iOS/Android).
*   **Task 6:** Implement LCU Remote Control from the phone (queueing, accepting matches, picking champs).

### Phase 3: Champ Select & Auto-Runes
*   **Task 7:** Implement Champ Select scouting (pulling ally stats via LCU).
*   **Task 8:** Implement Auto-Runes via LCU API.

### Phase 4: In-Game Overlay
*   **Task 9:** Create the transparent C# Window tracking the game window.
*   **Task 10:** Poll the Live Client Data API for timers and live stats.

### Phase 5: Pengu Loader Integration
*   **Task 11:** Package Pengu Loader core files with the application.
*   **Task 12:** Build a UI in the PC app to browse and inject LCU themes.

## 5. Potential Risks & Riot ToS Warnings

*   **LCU Rate Limiting:** Because we are piggybacking on the League Client to fetch global stats (like looking up an ally's match history), doing it too fast can cause the League Client itself to get temporarily rate-limited by Riot's internal servers. We must implement careful pacing in the code.
*   **Pengu Loader:** Modifying client files technically violates the ToS, even if Riot historically tolerates cosmetic mods. 
*   **Remote Drafting:** Automating actions remotely via the LCU API must simulate normal human response times to avoid being flagged as a bot.
