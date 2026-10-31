# 🌴 GTA VI OBS Overlays Suite (v0.0.4)

A modular, customizable OBS Studio Browser Source overlay suite inspired by **Grand Theft Auto VI**. Powered by **Streamer.bot WebSockets** and built for **YouTube Live** creators.

🔗 **Live Setup & Testing Hub:** [https://2smokinbarrels.com/GTA-VI-WalkOn-Card/setup.html](https://2smokinbarrels.com/GTA-VI-WalkOn-Card/setup.html)

---

## ✨ What's New in v0.0.4
* **Module 3 — Shoutout Toast (`shoutout`):**
  * Triggered via your `!so @username` command in Streamer.bot.
  * Listens automatically to both the `viShoutOuts` Global Variable (`Misc.GlobalVariableUpdated`) and custom WebSocket broadcasts (`GTA_Shoutout`).
  * Includes the signature **Roll & Pop** avatar spin animation, left/right avatar flip layout, custom header title text, and solid fill option.
* **Module 2 — YouTube Alerts (`alerts`):**
  * Supports **New Subscriber**, **New Member** (`New Sponsor`), **Member Milestone**, **Membership Gift**, **Gift Membership Received**, **Super Chat**, **Super Sticker**, and **Jewels Gifted**.
* **Module 1 — Member Walk-On Card (`walkon`):**
  * Dynamic stat row builder, 3D avatar coin flip/spin, and fixed **Solid Fill (No Background Image)** toggle.
* **Flexible OBS Browser Source Output:**
  * Use the **Include Modules** dropdown in `setup.html` to generate a single **All Modules (Global Combined)** URL or separate URLs for **Walk-On Card Only**, **Alerts Box Only**, or **Shoutout Toast Only**.

---

## 🟣 Twitch Support (v0.0.5)
* **Twitch events and the alert each maps to:**
  * Follow -> **New Follower**
  * Sub -> **New Subscriber**
  * ReSub -> **Member Milestone**
  * GiftBomb -> **Membership Gift**
  * GiftSub -> **Gift Membership Received**
  * Cheer -> **Bits Cheered**
  * Raid -> **Incoming Raid**
* **New URL toggles:** `al_fol` (Follower), `al_cht` (Cheer) and `al_rd` (Raid), available in the Twitch Events panel of the Alerts tab.
* **New and updated Streamer.bot scripts:**
  * `GTA_WalkOnTwitch.cs` - Twitch walk-on card.
  * `GTA_LookupTwitchUser.cs` - Twitch user lookup for the setup hub (choose Twitch in "Lookup Platform").
  * `GTA_Master_Alerts.cs` and `GTA_Shoutout.cs` - updated with a Twitch path (`userType=twitch`).
* **Requirement:** Streamer.bot must have your Twitch account connected.
* Simulate Twitch alerts from the Test tab using "Simulate As Platform" and the Twitch alert types.

---

## 📁 Project Folder Structure

```text
gta6-obs-overlays/
├── css/
│   └── overlays.css                   # Master stylesheet (Card + Alerts + Shoutout Toast)
├── js/
│   ├── overlays.js                    # Shared WebSocket client, drag/resize studio & queue engines
│   └── twitch-events.js               # Twitch event normalization for alerts
├── assets/
│   └── gta6-bg.webp                   # GTA VI background texture asset
├── streamerbot/
│   ├── GTA_Master_Alerts.cs           # C# script for YouTube Alerts blocking queue
│   ├── GTA_Shoutout.cs                # C# script for !so Shoutout Toast command
│   ├── GTA_LookupYouTubeUser.cs       # C# script for live User Variable testing in setup.html
│   ├── GTA_WalkOnTwitch.cs            # C# script for Twitch walk-on cards
│   └── GTA_LookupTwitchUser.cs        # C# script for Twitch user lookup in setup.html
├── gta-overlay.html                   # Unified OBS Browser Source
├── setup.html                         # 6-Tab Universal Setup & Testing Hub
└── README.md                          # Documentation
```
