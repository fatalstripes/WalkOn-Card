# Twitch Support — Design

## Goal
Bring the GTA VI overlay suite to Twitch with parity to YouTube: alerts, walk-on card, shoutout toast, user lookup, and setup hub options. YouTube and Twitch events are handled simultaneously (simulcast), with no mode switch.

## Current state
- Walk-On (`GTA_WalkOn` custom broadcast) is already platform-neutral.
- Shoutout (`GTA_Shoutout.cs`) has a partial Twitch branch (reads Twitch user vars, replies via `CPH.SendMessage`).
- Alerts are YouTube-only: `overlays.js` subscribes to `YouTube: ['*']` and hooks `YouTube.*`; `GTA_Master_Alerts.cs` maps YouTube trigger names.
- `GTA_LookupYouTubeUser.cs` and `setup.html` labels are YouTube-specific.

## Design

### 1. Alerts (`js/overlays.js`, `streamerbot/GTA_Master_Alerts.cs`, `css/overlays.css`)
- Subscribe to `Twitch: ['*']` in addition to YouTube.
- Native hooks map Twitch events to alert types:
  - `Twitch.Follow` -> `follow` (new)
  - `Twitch.Sub` -> `sub`
  - `Twitch.ReSub` -> `milestone`
  - `Twitch.GiftBomb` -> `gift` (count)
  - `Twitch.GiftSub` -> `giftRec` (recipient shown; subs that are part of a gift bomb are dropped to avoid flooding)
  - `Twitch.Cheer` -> `cheer` (new, shows bits)
  - `Twitch.Raid` -> `raid` (new, shows viewer count)
- Every alert payload carries `platform` (`youtube` | `twitch`), set by both the JS hooks and the C# script.
- `GTA_Master_Alerts.cs` maps Twitch trigger names to the same alert types, alongside the YouTube mapping.
- CSS: Twitch accent for new alert types; per-type enable toggles in `alertsConfig`.
- Existing alert types and YouTube behavior are unchanged.

### 2. Walk-On and Shoutout
- Walk-On: no overlay change. Add `streamerbot/GTA_WalkOnTwitch.cs` and setup notes for triggering from a Twitch chat/redeem trigger.
- Shoutout: complete the Twitch branch in `GTA_Shoutout.cs` (avatar/link lookup via Twitch user vars, fallback to the Twitch profile image, reply via Twitch chat).

### 3. Lookup and setup hub
- Add `streamerbot/GTA_LookupTwitchUser.cs` mirroring the YouTube lookup.
- `setup.html`: platform selector for test payloads, Twitch alert toggles.
- README: document Twitch setup and the new files.

## Error handling
Missing Twitch fields fall back to the existing default avatar/labels; unknown `platform` values are treated as `youtube` for backward compatibility.

## Testing
Streamer.bot is unavailable in CI. Verify the overlay by injecting mock `Twitch.*` payloads via the existing `postMessage` test bridge in a headless browser, and syntax-check the C# scripts.
