# LoL Helper App 🎮

A lightweight, fully Riot ToS-compliant League of Legends companion application. Built entirely in C# (.NET 9), this app avoids heavy web frameworks like Electron to keep RAM usage incredibly low. 

It features an in-game click-through overlay, automatic rune injection, Pengu Loader theme customization, and an upcoming iOS/Android remote control companion app.

## ✨ Features

### ✅ Currently Implemented
* **Zero-Config LCU Connection:** Automatically detects your League Client lockfile and connects to the internal LCU API securely.
* **Low-RAM Native Overlay:** A completely transparent, click-through WPF overlay using native Win32 APIs (No Electron/Chromium bloat).
* **Auto-Rune Injector:** Instantly downloads the latest DataDragon rune images and injects perfect rune pages directly into your client with one click.
* **Pengu Loader Theme Manager:** Dynamically pick any local image from your PC and instantly generate a custom background theme for your League Client.

### 🚀 Planned / Work In Progress
* **Live Match Scouting:** Fetches solo/duo ranks and winrates for all 10 players in your lobby using the official Riot Developer API.
* **Live In-Game Overlay Data:** Hooks into the local `Live Client Data API` (Port 2999) to display real-time gold advantages, jungle timers, and match events on your screen.
* **Mobile Companion App (iOS/Android):** A `.NET MAUI` cross-platform app that connects to the PC client over local Wi-Fi via WebSockets. Scan a QR code to remotely accept queue pops and pick champions from your phone!
* **Post-Match Analysis:** Detailed graphs analyzing your CS/min, vision score, and damage dealt compared to your team.

## 🛠️ Tech Stack
* **Desktop App:** C#, .NET 9, WPF (Windows Presentation Foundation)
* **Mobile App (Planned):** C#, .NET MAUI
* **Local Server (Planned):** ASP.NET Core Minimal APIs & WebSockets
* **APIs Used:** Riot LCU (League Client Update API), Riot Data Dragon, Live Client Data API, Riot Web API.

## ⚠️ ToS Compliance
This application is **100% compliant with Riot Games Terms of Service and Vanguard Anti-Cheat**. 
- It **does not** read or inject into League of Legends game memory.
- It **does not** modify game files.
- It strictly relies on official APIs provided by Riot (LCU, Live Client Data, and Web APIs).

## 🚀 How to Run (Development)
1. Ensure you have the [.NET 9.0 SDK](https://dotnet.microsoft.com/) installed.
2. Clone this repository.
3. Open a terminal in the root directory and run:
```bash
dotnet run --project LoLHelper.PCClient
```
4. Launch League of Legends!
