# Twitch Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the GTA VI overlay suite to Twitch with parity to YouTube (alerts, walk-on, shoutout, user lookup, setup hub), handling both platforms at once.

**Architecture:** A pure, unit-testable mapper (`js/twitch-events.js`) turns Streamer.bot `Twitch.*` WebSocket payloads into the overlay's existing alert shape plus a `platform` field. `overlays.js` subscribes to those events and feeds the existing alert queue; Streamer.bot C# scripts get matching Twitch paths so the custom-broadcast route works too.

**Tech Stack:** Vanilla JS (browser, no build step), Streamer.bot C# inline scripts, Node 22 (`node:test`) for unit tests, Playwright (pre-installed) for the browser check.

## Global Constraints

- Work on branch `feature/twitch-support`; never push to or open a PR against `IronJeff80/GTA-VI-WalkOn-Card`.
- No build step or new runtime dependencies: files stay plain `.js`/`.css`/`.html`/`.cs`.
- Existing YouTube behavior and URL params must not change; unknown `platform` values are treated as `youtube`.
- New URL toggles follow the existing pattern (`!== '0'` means on): `al_fol` (follow), `al_cht` (cheer), `al_rd` (raid). `sub`, `milestone`, `gift`, `giftRec` reuse `al_sub`, `al_ms`, `al_gft`, `al_grc`.
- Streamer.bot Twitch payload field names are not verifiable offline. The mapper reads several candidate paths per field and Task 6 verifies against real payloads.
- C# cannot be compiled in this environment (no dotnet/mono). Each C# task ends with a manual compile in Streamer.bot (sub-action editor → Compile).
- Every commit message ends with these two lines:
  ```
  Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr
  ```

## File Structure

| File | Responsibility |
|---|---|
| `js/twitch-events.js` (create) | Pure `mapTwitchEvent(name, data)` + `tierLabel`. Works in browser (`window.GTATwitchEvents`) and Node (`module.exports`). |
| `tests/twitch-events.test.js` (create) | Unit tests for the mapper. |
| `tests/overlay-twitch.e2e.js` (create) | Playwright check that Twitch alerts render in `gta-overlay.html`. |
| `js/overlays.js` (modify) | Config toggles, Twitch subscription/hooks, `platform` class, follow/cheer/raid rendering, HTML escaping. |
| `css/overlays.css` (modify) | Twitch accent for new alert types. |
| `gta-overlay.html` (modify) | Load `twitch-events.js`. |
| `streamerbot/GTA_Master_Alerts.cs` (modify) | Twitch trigger mapping + `platform` in payload. |
| `streamerbot/GTA_Shoutout.cs` (modify) | Finish Twitch lookup and chat reply. |
| `streamerbot/GTA_WalkOnTwitch.cs` (create) | Twitch walk-on broadcaster. |
| `streamerbot/GTA_LookupTwitchUser.cs` (create) | Twitch counterpart of the YouTube lookup. |
| `setup.html` (modify) | Twitch toggles, simulator types and platform, lookup platform selector. |
| `README.md`, spec (modify) | Docs. |

---

### Task 1: Twitch event mapper

**Files:**
- Create: `js/twitch-events.js`
- Create: `tests/twitch-events.test.js`
- Modify: `docs/superpowers/specs/2026-10-31-twitch-support-design.md` (GiftSub mapping)

**Interfaces:**
- Produces: `GTATwitchEvents.mapTwitchEvent(name: string, data: object): object|null` where `name` is one of `Follow|Sub|ReSub|GiftSub|GiftBomb|Cheer|Raid`. Returns `{ platform:'twitch', alertType, triggerName, userName, userProfileUrl, levelName?, months?, count?, gifterUser?, amount?, message? }` or `null` (unknown event, or a `GiftSub` that belongs to a gift bomb).
- Produces: `GTATwitchEvents.tierLabel(raw): string`.

Mapping decision: `GiftBomb` → `gift` (count). A single `GiftSub` → `giftRec` (recipient is `userName`, gifter in `gifterUser`); `GiftSub`s flagged as part of a community gift are dropped so one bomb doesn't flood the queue. This refines the spec's "GiftSub → gift".

- [ ] **Step 1: Write the failing tests**

Create `tests/twitch-events.test.js`:

```js
const test = require('node:test');
const assert = require('node:assert/strict');
const { mapTwitchEvent, tierLabel } = require('../js/twitch-events.js');

test('tierLabel normalizes Streamer.bot and EventSub tiers', () => {
  assert.equal(tierLabel('tier 1'), 'Tier 1');
  assert.equal(tierLabel('Tier 3'), 'Tier 3');
  assert.equal(tierLabel('2000'), 'Tier 2');
  assert.equal(tierLabel('prime'), 'Prime');
  assert.equal(tierLabel(undefined), '');
});

test('Follow maps to follow', () => {
  const a = mapTwitchEvent('Follow', { user_name: 'Lucia' });
  assert.deepEqual(
    { p: a.platform, t: a.alertType, n: a.userName, trig: a.triggerName },
    { p: 'twitch', t: 'follow', n: 'Lucia', trig: 'New Follower' }
  );
});

test('Sub maps to sub with tier', () => {
  const a = mapTwitchEvent('Sub', { user: { name: 'Jason' }, sub_tier: 'tier 2' });
  assert.equal(a.alertType, 'sub');
  assert.equal(a.userName, 'Jason');
  assert.equal(a.levelName, 'Tier 2');
});

test('ReSub maps to milestone with months and message', () => {
  const a = mapTwitchEvent('ReSub', { user: { name: 'Jason' }, cumulative: 14, sub_tier: 'prime', text: 'hi' });
  assert.equal(a.alertType, 'milestone');
  assert.equal(a.months, 14);
  assert.equal(a.levelName, 'Prime');
  assert.equal(a.message, 'hi');
});

test('GiftBomb maps to gift with count', () => {
  const a = mapTwitchEvent('GiftBomb', { user: { name: 'Tommy' }, gifts: 10 });
  assert.equal(a.alertType, 'gift');
  assert.equal(a.count, 10);
  assert.equal(a.userName, 'Tommy');
});

test('single GiftSub maps to giftRec; bomb-part GiftSub is dropped', () => {
  const one = mapTwitchEvent('GiftSub', { user: { name: 'Tommy' }, recipient: { name: 'Lucia' } });
  assert.equal(one.alertType, 'giftRec');
  assert.equal(one.userName, 'Lucia');
  assert.equal(one.gifterUser, 'Tommy');
  assert.equal(mapTwitchEvent('GiftSub', { user: { name: 'Tommy' }, recipient: { name: 'Lucia' }, fromCommunitySubGift: true }), null);
});

test('Cheer maps to cheer with bits and message', () => {
  const a = mapTwitchEvent('Cheer', { message: { displayName: 'Lucia', bits: 500, message: 'take it' } });
  assert.equal(a.alertType, 'cheer');
  assert.equal(a.amount, 500);
  assert.equal(a.message, 'take it');
  assert.equal(a.userName, 'Lucia');
});

test('Raid maps to raid with viewers', () => {
  const a = mapTwitchEvent('Raid', { from_broadcaster_user_name: 'BigStreamer', viewers: 42 });
  assert.equal(a.alertType, 'raid');
  assert.equal(a.userName, 'BigStreamer');
  assert.equal(a.count, 42);
});

test('unknown event and empty data are safe', () => {
  assert.equal(mapTwitchEvent('Nope', {}), null);
  assert.equal(mapTwitchEvent('Follow', undefined).userName, 'Someone');
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd /home/user/walkon-card && node --test tests/`
Expected: FAIL with `Cannot find module '../js/twitch-events.js'`.

- [ ] **Step 3: Implement the mapper**

Create `js/twitch-events.js`:

```js
/* Maps Streamer.bot Twitch.* WebSocket payloads to the overlay alert shape. */
(function (root) {
  function get(obj, path) {
    return path.split('.').reduce((o, k) => (o && o[k] !== undefined ? o[k] : undefined), obj);
  }

  function first(obj, paths) {
    for (const p of paths) {
      const v = get(obj, p);
      if (v !== undefined && v !== null && v !== '') return v;
    }
    return undefined;
  }

  function tierLabel(raw) {
    if (raw === undefined || raw === null || raw === '') return '';
    const s = String(raw).toLowerCase();
    if (s.includes('prime')) return 'Prime';
    const m = s.match(/^(\d)000$/) || s.match(/(\d)/);
    return m ? `Tier ${m[1]}` : '';
  }

  const USER = ['user.name', 'user.display_name', 'user.login', 'user_name', 'userName', 'user'];
  const AVATAR = ['user.profileImageUrl', 'user.profile_image_url', 'profileImageUrl', 'avatarUrl'];

  function mapTwitchEvent(name, data) {
    const d = data || {};
    const base = {
      platform: 'twitch',
      userName: first(d, USER) || 'Someone',
      userProfileUrl: first(d, AVATAR) || ''
    };
    const tier = tierLabel(first(d, ['sub_tier', 'subTier', 'tier']));

    switch (name) {
      case 'Follow':
        return { ...base, alertType: 'follow', triggerName: 'New Follower' };
      case 'Sub':
        return { ...base, alertType: 'sub', triggerName: 'New Subscriber', levelName: tier };
      case 'ReSub':
        return {
          ...base, alertType: 'milestone', triggerName: 'Resub Milestone', levelName: tier,
          months: first(d, ['cumulative', 'cumulativeMonths', 'months']) || 1,
          message: first(d, ['text', 'message.message', 'message']) || ''
        };
      case 'GiftBomb':
        return {
          ...base, alertType: 'gift', triggerName: 'Gift Subs',
          count: first(d, ['gifts', 'total', 'totalGifts', 'count']) || 1
        };
      case 'GiftSub': {
        if (first(d, ['fromCommunitySubGift', 'from_community_sub_gift'])) return null;
        return {
          ...base, alertType: 'giftRec', triggerName: 'Gift Sub Received',
          userName: first(d, ['recipient.name', 'recipient.display_name', 'recipient.login', 'recipientUser']) || 'Someone',
          gifterUser: first(d, USER) || 'Anonymous'
        };
      }
      case 'Cheer':
        return {
          ...base, alertType: 'cheer', triggerName: 'Bits Cheered',
          userName: first(d, ['message.displayName', 'message.username', ...USER]) || 'Someone',
          amount: first(d, ['message.bits', 'bits']) || 0,
          message: first(d, ['message.message', 'text']) || ''
        };
      case 'Raid':
        return {
          ...base, alertType: 'raid', triggerName: 'Incoming Raid',
          userName: first(d, ['from_broadcaster_user_name', 'fromBroadcasterUserName', ...USER]) || 'Someone',
          count: first(d, ['viewers', 'viewerCount']) || 0
        };
      default:
        return null;
    }
  }

  const api = { mapTwitchEvent, tierLabel };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.GTATwitchEvents = api;
})(typeof window !== 'undefined' ? window : globalThis);
```

- [ ] **Step 4: Run to verify pass**

Run: `cd /home/user/walkon-card && node --test tests/`
Expected: `# pass 9`, `# fail 0`.

- [ ] **Step 5: Update the spec mapping line and commit**

In `docs/superpowers/specs/2026-10-31-twitch-support-design.md` replace the lines `  - \`Twitch.GiftSub\`, \`Twitch.GiftBomb\` -> \`gift\`` and `  - gifted-sub recipient event -> \`giftRec\`` with:

```
  - `Twitch.GiftBomb` -> `gift` (count)
  - `Twitch.GiftSub` -> `giftRec` (recipient shown; subs that are part of a gift bomb are dropped to avoid flooding)
```

```bash
git add js/twitch-events.js tests/twitch-events.test.js docs/superpowers/specs
git commit -m "feat: add Twitch event mapper with unit tests

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr"
```

---

### Task 2: Overlay engine, rendering, and browser check

**Files:**
- Modify: `gta-overlay.html:28` (script tag), `js/overlays.js` (config ~66-75, subscribe ~507-510, hooks ~556-585, playAlert ~725-782), `css/overlays.css` (~321)
- Create: `tests/overlay-twitch.e2e.js`

**Interfaces:**
- Consumes: `window.GTATwitchEvents.mapTwitchEvent` (Task 1).
- Produces: `GTAOverlayEngine.handleTwitchAlert(name, data)`; alert types `follow`, `cheer`, `raid`; DOM class `platform-twitch|platform-youtube` on `#alert-box`.

- [ ] **Step 1: Write the failing browser check**

Create `tests/overlay-twitch.e2e.js`:

```js
// Run: node tests/overlay-twitch.e2e.js  (starts its own static server)
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');
const assert = require('assert/strict');

const root = path.resolve(__dirname, '..');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.webp': 'image/webp' };
const server = http.createServer((req, res) => {
  const file = path.join(root, decodeURIComponent(req.url.split('?')[0]));
  if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
});

(async () => {
  await new Promise(r => server.listen(0, r));
  const base = `http://127.0.0.1:${server.address().port}`;
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.route(u => !u.href.startsWith(base), r => r.abort());

  async function fire(name, data, url = `${base}/gta-overlay.html?modules=alerts&aDur=1`) {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await page.evaluate(([n, d]) => window.gtaOverlay.handleTwitchAlert(n, d), [name, data]);
    await page.waitForFunction(() => document.getElementById('alert-container').classList.contains('animate-in'));
    return page.evaluate(() => ({
      cls: document.getElementById('alert-box').className,
      title: document.getElementById('alert-event-name').textContent,
      user: document.getElementById('alert-user-name').textContent,
      body: document.getElementById('alert-body').textContent
    }));
  }

  let r = await fire('Raid', { from_broadcaster_user_name: 'BigStreamer', viewers: 42 });
  assert.match(r.cls, /alert-raid/); assert.match(r.cls, /platform-twitch/);
  assert.equal(r.user, 'BigStreamer'); assert.match(r.body, /42/);

  r = await fire('Cheer', { message: { displayName: 'Lucia', bits: 500, message: '<b>x</b> hi' } });
  assert.match(r.cls, /alert-cheer/); assert.match(r.body, /500/);
  assert.equal(r.body.includes('<b>'), true, 'message must be shown as text, not parsed as HTML');

  r = await fire('Follow', { user_name: 'Jason' });
  assert.match(r.cls, /alert-follow/);

  r = await fire('Sub', { user: { name: 'Tommy' }, sub_tier: 'tier 3' });
  assert.match(r.body, /Tier 3/);

  // Disabled toggle suppresses the alert
  await page.goto(`${base}/gta-overlay.html?modules=alerts&al_rd=0`, { waitUntil: 'domcontentloaded' });
  await page.evaluate(() => window.gtaOverlay.handleTwitchAlert('Raid', { from_broadcaster_user_name: 'X', viewers: 1 }));
  await page.waitForTimeout(300);
  assert.equal(await page.evaluate(() => window.gtaOverlay.alertQueue.length + (window.gtaOverlay.isAlertAnimating ? 1 : 0)), 0);

  await browser.close(); server.close();
  console.log('overlay-twitch e2e: OK');
})().catch(e => { console.error(e); process.exit(1); });
```

- [ ] **Step 2: Run to verify failure**

Run: `cd /home/user/walkon-card && node tests/overlay-twitch.e2e.js`
Expected: FAIL (`window.gtaOverlay.handleTwitchAlert is not a function`).

- [ ] **Step 3: Load the mapper in the overlay page**

In `gta-overlay.html`, replace `  <script src="js/overlays.js"></script>` with:

```html
  <script src="js/twitch-events.js"></script>
  <script src="js/overlays.js"></script>
```

- [ ] **Step 4: Config toggles**

In `js/overlays.js` replace `      jewels: params.get('al_jwl') !== '0',` with:

```js
      jewels: params.get('al_jwl') !== '0',
      follow: params.get('al_fol') !== '0',
      cheer: params.get('al_cht') !== '0',
      raid: params.get('al_rd') !== '0',
```

- [ ] **Step 5: Subscribe and hook Twitch events**

In `initWebSocket`, replace `        YouTube: ['*'],` with:

```js
        YouTube: ['*'],
        Twitch: ['Follow', 'Sub', 'ReSub', 'GiftSub', 'GiftBomb', 'Cheer', 'Raid'],
```

Replace the block that ends the native-hook section

```js
      this.client.on('YouTube.SuperSticker', (p) => this.handleNativeAlert('supersticker', 'Super Sticker', p.data));
    }
  }
```

with:

```js
      this.client.on('YouTube.SuperSticker', (p) => this.handleNativeAlert('supersticker', 'Super Sticker', p.data));

      ['Follow', 'Sub', 'ReSub', 'GiftSub', 'GiftBomb', 'Cheer', 'Raid'].forEach((name) => {
        this.client.on(`Twitch.${name}`, (p) => this.handleTwitchAlert(name, p.data));
      });
    }
  }

  handleTwitchAlert(name, data) {
    const alertData = window.GTATwitchEvents && window.GTATwitchEvents.mapTwitchEvent(name, data);
    if (alertData) this.enqueueAlert(alertData);
  }
```

In `handleNativeAlert`, replace `    const alertData = { alertType: type, triggerName: title };` with:

```js
    const alertData = { platform: 'youtube', alertType: type, triggerName: title };
```

- [ ] **Step 6: Render platform class, new types, and escape viewer text**

In `playAlert`, replace

```js
    this.dom.alertBox.className = `alert-box alert-${type}`;
```

with

```js
    const platform = raw.platform === 'twitch' ? 'twitch' : 'youtube';
    this.dom.alertBox.className = `alert-box alert-${type} platform-${platform}`;
```

Replace `    if (type === 'member' && levelName) {` with `    if ((type === 'member' || type === 'sub') && levelName) {`.

Replace the line `    } else if (type === 'jewels') {` with these new branches followed by the original line:

```js
    } else if (type === 'cheer') {
      bodyEl.innerHTML += `<div class="alert-detail-row"><span>Bits</span><span class="alert-highlight amount">${escapeHtml(amount)}</span></div>`;
    } else if (type === 'raid') {
      bodyEl.innerHTML += `<div class="alert-detail-row"><span>Raiders</span><span class="alert-highlight amount">${escapeHtml(count)} Viewers</span></div>`;
    } else if (type === 'jewels') {
```

Replace `      bodyEl.innerHTML += \`<div class="alert-message">${message}</div>\`;` with `      bodyEl.innerHTML += \`<div class="alert-message">${escapeHtml(message)}</div>\`;`.

Add above `const GTAOverlayConfig`:

```js
const escapeHtml = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
```

- [ ] **Step 7: Twitch accent CSS**

In `css/overlays.css`, add `--twitch-purple: #9146ff;` and `--glow-twitch: color-mix(in srgb, var(--twitch-purple) 45%, transparent);` next to the other `--glow-*` variables (top of file, after line 21), then after the `.alert-box.alert-superchat, .alert-box.alert-jewels {...}` rule add:

```css
.alert-box.alert-follow, .alert-box.alert-cheer, .alert-box.alert-raid { border-top-color: var(--twitch-purple); box-shadow: 0 16px 40px rgba(0,0,0,0.8), 0 0 25px var(--glow-twitch), inset 0 0 0 2px var(--gta-icon-stroke); }
```

- [ ] **Step 8: Run tests**

Run: `cd /home/user/walkon-card && node --test tests/ && node tests/overlay-twitch.e2e.js`
Expected: unit tests pass; `overlay-twitch e2e: OK`.

- [ ] **Step 9: Commit**

```bash
git add gta-overlay.html js/overlays.js css/overlays.css tests/overlay-twitch.e2e.js
git commit -m "feat: render Twitch alerts in overlay (follow, sub, resub, gifts, cheer, raid)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr"
```

---

### Task 3: Master Alerts script (Twitch path)

**Files:**
- Modify: `streamerbot/GTA_Master_Alerts.cs:11-32` (mapping), `:34-56` (payload/user), `:59-134` (cases)

**Interfaces:**
- Produces: broadcast payload with `platform` plus the same fields Task 1's mapper emits (`follow`, `cheer`, `raid` types; `sub` carries `levelName`).

- [ ] **Step 1: Detect platform and map Twitch triggers**

Replace the lines from `string alertType = "generic";` through the end of the `else if (tNameLower.Contains("jewel")) alertType = "jewels";` line with:

```csharp
        string alertType = "generic";

        string eventSource = args.ContainsKey("eventSource") ? args["eventSource"].ToString().ToLower() : "";
        string tNameLower0 = triggerName.ToLower();
        bool isTwitch = eventSource.Contains("twitch") || tNameLower0.Contains("twitch")
                     || tNameLower0.Contains("cheer") || tNameLower0.Contains("raid") || tNameLower0.Contains("follow");
        string platform = isTwitch ? "twitch" : "youtube";

        // Fix old YouTube terminology
        if (triggerName.IndexOf("New Sponsor", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            triggerName = "New Member";
        }

        string tNameLower = triggerName.ToLower();

        // 2. Map Trigger Names to Overlay Alert Types
        if (isTwitch)
        {
            if (tNameLower.Contains("cheer")) alertType = "cheer";
            else if (tNameLower.Contains("raid")) alertType = "raid";
            else if (tNameLower.Contains("follow")) alertType = "follow";
            else if (tNameLower.Contains("resub")) alertType = "milestone";
            else if (tNameLower.Contains("gift bomb") || tNameLower.Contains("community")) alertType = "gift";
            else if (tNameLower.Contains("gift")) alertType = "giftRec";
            else if (tNameLower.Contains("sub")) alertType = "sub";
        }
        else if (tNameLower.Contains("super chat")) alertType = "superchat";
        else if (tNameLower.Contains("super sticker")) alertType = "supersticker";
        else if (tNameLower.Contains("subscriber")) alertType = "sub";
        else if (tNameLower.Contains("milestone")) alertType = "milestone";
        else if (tNameLower.Contains("received")) alertType = "giftRec"; 
        else if (tNameLower.Contains("membership gift") || tNameLower.Contains("gift membership")) alertType = "gift";
        else if (tNameLower.Contains("member") || tNameLower.Contains("sponsor")) alertType = "member";
        else if (tNameLower.Contains("jewel")) alertType = "jewels";
```

Delete the original duplicated `// Fix old YouTube terminology` block and `string tNameLower = ...` line that preceded it so they are not declared twice.

- [ ] **Step 2: Add platform to payload and Twitch avatar**

Replace `            { "alertType", alertType },` with:

```csharp
            { "platform", platform },
            { "alertType", alertType },
```

Replace `(args.ContainsKey("userName") ? args["userName"].ToString() : "YouTube Member");` with `(args.ContainsKey("userName") ? args["userName"].ToString() : (isTwitch ? "Twitch Viewer" : "YouTube Member"));`.

Replace the `if (isTest) { userProfileUrl = "https://yt3...."; }` block with:

```csharp
        if (isTest)
        {
            userProfileUrl = isTwitch
                ? "https://i.pravatar.cc/300?img=47"
                : "https://yt3.ggpht.com/XhCX8VaSQAaxcZhSahLCjdB19626R1VNRKKTbCd_PSlepkyqEJYGbybrAbiLHtSDwBN9KN72vYE=s600-c-k-c0x00ffffff-no-rj";
        }
        else if (isTwitch && string.IsNullOrEmpty(userProfileUrl))
        {
            try
            {
                string login = payload["userName"].ToString();
                var info = CPH.TwitchGetExtendedUserInfoByLogin(login);
                if (info != null) userProfileUrl = info.ProfileImageUrl ?? "";
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-Alert] Twitch avatar lookup failed: {ex.Message}"); }
        }
        else if (!isTwitch && !string.IsNullOrEmpty(userProfileUrl))
```

(The trailing `else if` replaces the old `else if (!string.IsNullOrEmpty(userProfileUrl))`; its body, the `=s600-` regex, is unchanged.)

- [ ] **Step 3: Twitch field cases**

Add a helper before `Execute`:

```csharp
    private object ArgAny(params string[] keys)
    {
        foreach (string k in keys)
            if (args.ContainsKey(k) && args[k] != null && args[k].ToString() != "") return args[k];
        return null;
    }
```

Then change the `member`/`milestone`/`gift` cases and add new ones. Add before `case "member":`:

```csharp
            case "sub":
                payload["levelName"] = ArgAny("tier", "subTier", "tierName") ?? "";
                break;

            case "follow":
                break;

            case "cheer":
                payload["amount"] = ArgAny("bits", "amount") ?? 0;
                payload["message"] = ArgAny("message", "rawInput") ?? "";
                break;

            case "raid":
                payload["count"] = ArgAny("viewers", "viewerCount") ?? 0;
                break;
```

Change the `milestone` case's months line to `payload["months"] = ArgAny("cumulative", "months", "monthStreak") ?? 1;` and its tier line to `payload["levelName"] = ArgAny("tier", "tierName") ?? "";`. In the `gift` case, extend count: `string countStr = ArgAny("gifts", "totalGifts", "count", "amount")?.ToString() ?? "";` (replace the first `countStr` assignment and drop the following `if (string.IsNullOrEmpty(countStr) && args.ContainsKey("amount")) {...}` block). In `giftRec`, before the existing `if (args.ContainsKey("gifterUser")...` chain add:

```csharp
                if (isTwitch)
                {
                    object recipient = ArgAny("recipientUser", "recipient", "targetUser");
                    if (recipient != null) payload["userName"] = recipient.ToString();
                    payload["gifterUser"] = args.ContainsKey("user") ? args["user"].ToString() : "Anonymous";
                    break;
                }
```

- [ ] **Step 4: Compile and commit**

In Streamer.bot: paste the script into an Execute C# Code sub-action → Compile; expect no errors. Test with an action whose trigger is Twitch → Cheer and `isTest=true`; the overlay should show a Cheer alert.

```bash
git add streamerbot/GTA_Master_Alerts.cs
git commit -m "feat: add Twitch trigger mapping to master alerts script

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr"
```

---

### Task 4: Shoutout, Walk-On and Lookup scripts

**Files:**
- Modify: `streamerbot/GTA_Shoutout.cs:15`, `:41-46`, `:64-68`, `:104-114`
- Create: `streamerbot/GTA_WalkOnTwitch.cs`, `streamerbot/GTA_LookupTwitchUser.cs`

**Interfaces:**
- Shoutout broadcast unchanged (`event: "GTA_Shoutout"`, `user`, `avatar`, `channelLink`, `timestamp`).
- WalkOn broadcast: `{ event: <eventName arg, default "MemberWalkOn">, userName, avatarUrl, levelName, months? }`.
- Lookup broadcast: `GTA_UserLookupResult` (same contract `setup.html` already consumes).

- [ ] **Step 1: Shoutout — default platform and Twitch lookup**

Replace `        userType = userType?.ToLower() ?? "youtube";` with:

```csharp
        if (string.IsNullOrEmpty(userType) && CPH.TryGetArg<string>("eventSource", out string src) && !string.IsNullOrEmpty(src))
            userType = src;
        userType = userType?.ToLower() ?? "youtube";
```

Replace the whole `else if (userType == "twitch") { ... }` block with:

```csharp
        else if (userType == "twitch")
        {
            storedUserId = CPH.GetTwitchUserVar<string>(targetName, "userId", true);
            storedAvatarUrl = CPH.GetTwitchUserVar<string>(targetName, "avatarUrl", true);
            storedChannelLink = CPH.GetTwitchUserVar<string>(targetName, "channelLink", true);

            // Fall back to the Twitch API when the user variables were never populated
            if (string.IsNullOrEmpty(storedAvatarUrl))
            {
                try
                {
                    var info = CPH.TwitchGetExtendedUserInfoByLogin(targetName);
                    if (info != null)
                    {
                        storedUserId = info.UserId;
                        storedAvatarUrl = info.ProfileImageUrl;
                        storedChannelLink = $"https://twitch.tv/{targetName.ToLower()}";
                    }
                }
                catch (Exception ex) { CPH.LogWarn($"[GTA-Shoutout] Twitch lookup failed: {ex.Message}"); }
            }
        }
```

Change the avatar-upscale guard's condition to `if (userType == "youtube" && !string.IsNullOrEmpty(storedAvatarUrl))`.

In `SendPlatformMessage`, make Twitch explicit: replace the `else { CPH.SendMessage(msg); }` with `else if (platform == "twitch") { CPH.SendMessage(msg, true); } else { CPH.SendMessage(msg); }`.

- [ ] **Step 2: Create `GTA_WalkOnTwitch.cs`**

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class CPHInline
{
    public bool Execute()
    {
        string user = args.ContainsKey("userName") ? args["userName"].ToString()
                    : (args.ContainsKey("user") ? args["user"].ToString() : "");
        if (string.IsNullOrWhiteSpace(user)) return false;

        string eventName = args.ContainsKey("eventName") ? args["eventName"].ToString() : "MemberWalkOn";
        bool isTest = args.ContainsKey("isTest") && args["isTest"].ToString().ToLower() == "true";

        string avatarUrl = isTest ? "https://i.pravatar.cc/300?img=47" : "";
        if (!isTest)
        {
            try
            {
                var info = CPH.TwitchGetExtendedUserInfoByLogin(user);
                if (info != null) avatarUrl = info.ProfileImageUrl ?? "";
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-WalkOnTwitch] Avatar lookup failed: {ex.Message}"); }
        }

        string levelName = "Viewer";
        if (Flag("isSubscribed")) levelName = "Subscriber";
        if (Flag("isVip")) levelName = "VIP";
        if (Flag("isModerator")) levelName = "Moderator";
        if (Flag("isBroadcaster")) levelName = "Broadcaster";

        var payload = new Dictionary<string, object>
        {
            { "event", eventName },
            { "platform", "twitch" },
            { "userName", user },
            { "avatarUrl", avatarUrl },
            { "levelName", levelName }
        };

        int? months = CPH.GetTwitchUserVar<int?>(user, "months", true);
        if (months.HasValue) payload["months"] = months.Value;

        string json = JsonConvert.SerializeObject(payload);
        CPH.LogInfo($"[GTA-WalkOnTwitch] Broadcasting: {json}");
        CPH.WebsocketBroadcastJson(json);
        return true;
    }

    private bool Flag(string key)
    {
        return args.ContainsKey(key) && args[key] != null && args[key].ToString().ToLower() == "true";
    }
}
```

- [ ] **Step 3: Create `GTA_LookupTwitchUser.cs`**

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class CPHInline
{
    private object SafeGetTwitchVar(string user, string varName)
    {
        try { var s = CPH.GetTwitchUserVar<string>(user, varName, true); if (s != null) return s; } catch { }
        try { var i = CPH.GetTwitchUserVar<int?>(user, varName, true); if (i.HasValue) return i.Value; } catch { }
        try { var l = CPH.GetTwitchUserVar<long?>(user, varName, true); if (l.HasValue) return l.Value; } catch { }
        try { var d = CPH.GetTwitchUserVar<double?>(user, varName, true); if (d.HasValue) return d.Value; } catch { }
        return null;
    }

    private object FindUserVarWithAliases(string user, string primaryKey, string[] aliases)
    {
        var keys = new List<string> { primaryKey };
        if (aliases != null) foreach (string a in aliases) if (!keys.Contains(a)) keys.Add(a);
        foreach (string k in keys)
        {
            object v = SafeGetTwitchVar(user, k);
            if (v != null) return v;
        }
        return null;
    }

    public bool Execute()
    {
        string targetUser = args.ContainsKey("targetUser") ? args["targetUser"].ToString().Trim() : "";
        string requestedVarsCsv = args.ContainsKey("requestedVars")
            ? args["requestedVars"].ToString()
            : "avatarUrl,levelName,months,memberSince,loyaltyLevel";
        string eventName = args.ContainsKey("eventName") ? args["eventName"].ToString() : "MemberWalkOn";

        if (string.IsNullOrEmpty(targetUser))
        {
            CPH.LogWarn("[GTA-LookupTwitch] targetUser was empty.");
            return false;
        }

        var result = new Dictionary<string, object>
        {
            { "event", "GTA_UserLookupResult" },
            { "type", eventName },
            { "platform", "twitch" },
            { "targetUser", targetUser },
            { "userName", targetUser }
        };

        var aliasMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "avatarUrl",    new[] { "userProfileUrl", "avatar", "profileImageUrl", "profilePic" } },
            { "levelName",    new[] { "tier", "tierName", "subTier", "role" } },
            { "months",       new[] { "tenure", "subMonths", "cumulative", "totalMonths" } },
            { "memberSince",  new[] { "joined", "joinDate", "since", "followDate" } },
            { "loyaltyLevel", new[] { "loyalty", "rank", "userLevel", "level" } }
        };

        foreach (string rawVar in requestedVarsCsv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string varName = rawVar.Trim();
            object val = FindUserVarWithAliases(targetUser, varName, aliasMap.ContainsKey(varName) ? aliasMap[varName] : null);
            if (val != null) result[varName] = val;
            else CPH.LogWarn($"[GTA-LookupTwitch] '{varName}' returned null for {targetUser}.");
        }

        // Fall back to the Twitch API for the profile image
        if (!result.ContainsKey("avatarUrl"))
        {
            try
            {
                var info = CPH.TwitchGetExtendedUserInfoByLogin(targetUser);
                if (info != null && !string.IsNullOrEmpty(info.ProfileImageUrl)) result["avatarUrl"] = info.ProfileImageUrl;
            }
            catch (Exception ex) { CPH.LogWarn($"[GTA-LookupTwitch] API lookup failed: {ex.Message}"); }
        }

        object storedName = FindUserVarWithAliases(targetUser, "userName", new[] { "user", "displayName", "name" });
        if (storedName != null && !string.IsNullOrEmpty(storedName.ToString())) result["userName"] = storedName.ToString();

        string json = JsonConvert.SerializeObject(result);
        CPH.LogInfo($"[GTA-LookupTwitch] Broadcasting JSON: {json}");
        CPH.WebsocketBroadcastJson(json);
        return true;
    }
}
```

- [ ] **Step 4: Compile and commit**

Compile each of the three scripts in Streamer.bot; expect no errors. `TwitchGetExtendedUserInfoByLogin` and its `UserId`/`ProfileImageUrl` members are the one API surface not verifiable offline; if the compiler rejects a member name, check the Streamer.bot C# docs and adjust all three files.

```bash
git add streamerbot/
git commit -m "feat: add Twitch shoutout lookup, walk-on and user lookup scripts

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr"
```

---

### Task 5: Setup hub and README

**Files:**
- Modify: `setup.html:197-204` (alerts tab), `:313-316` (sim types), `:322` (lookup), `:567` (toggle ids), `:731-737` (sim payloads); `README.md`

- [ ] **Step 1: Twitch toggles panel**

In `setup.html`, after the closing `</div>` of the YouTube Events panel (the one containing `chk-al_jwl`, line ~205), insert:

```html
      <div class="panel">
        <div class="panel-title"><i class="fa-brands fa-twitch"></i> Twitch Events</div>
        <p style="font-size:0.75rem; color:#9aa4b2;">Twitch Sub, Resub, Gift Subs and Gift Received share the Subscriber, Milestone, Gift and Gift Received toggles above.</p>
        <label class="inline-toggle-row"><span>New Follower</span><input type="checkbox" class="switch" id="chk-al_fol" checked></label>
        <label class="inline-toggle-row"><span>Bits Cheered</span><input type="checkbox" class="switch" id="chk-al_cht" checked></label>
        <label class="inline-toggle-row"><span>Incoming Raid</span><input type="checkbox" class="switch" id="chk-al_rd" checked></label>
      </div>
```

In `checkboxToggles`, replace `'al_sub','al_mem','al_ms','al_gft','al_grc','al_sc','al_stk','al_jwl',` with `'al_sub','al_mem','al_ms','al_gft','al_grc','al_sc','al_stk','al_jwl','al_fol','al_cht','al_rd',`.

- [ ] **Step 2: Simulator types and platform**

After `<option value="jewels">Jewels Gifted</option>` add:

```html
            <option value="follow">Twitch: New Follower</option>
            <option value="cheer">Twitch: Bits Cheered</option>
            <option value="raid">Twitch: Incoming Raid</option>
```

After the `</label>` that closes the `sim-alert-type` select add:

```html
        <label>Simulate As Platform
          <select id="sim-platform">
            <option value="youtube">YouTube</option>
            <option value="twitch">Twitch</option>
          </select>
        </label>
```

In the `btn-sim-alert` handler, replace the `const p = { alertType: type, userName: ...` line with:

```js
        const platformEl = document.getElementById('sim-platform');
        const p = { platform: (['follow','cheer','raid'].includes(type) ? 'twitch' : (platformEl ? platformEl.value : 'youtube')), alertType: type, userName: 'Lucia_Caminos', userProfileUrl: 'https://i.pravatar.cc/300?img=47' };
```

and after the `jewels` line add:

```js
        if (type === 'follow') p.triggerName = "New Follower";
        if (type === 'cheer') { p.triggerName = "Bits Cheered"; p.amount = 500; p.message = "Take my bits!"; }
        if (type === 'raid') { p.triggerName = "Incoming Raid"; p.userName = "BigStreamer"; p.count = 42; }
```

- [ ] **Step 3: Lookup platform selector**

Replace the line `<label>SB Lookup Action <input type="text" id="cfg-lookupAction" value="GTA_LookupYouTubeUser"></label>` with:

```html
        <label>Lookup Platform
          <select id="cfg-lookupPlatform">
            <option value="youtube">YouTube</option>
            <option value="twitch">Twitch</option>
          </select>
        </label>
        <label>SB Lookup Action <input type="text" id="cfg-lookupAction" value="GTA_LookupYouTubeUser"></label>
```

Add inside the script, next to the other `getElementById` wiring (after `btnConnectSb` setup):

```js
    const lookupPlatformEl = document.getElementById('cfg-lookupPlatform');
    if (lookupPlatformEl) {
      lookupPlatformEl.addEventListener('change', () => {
        const actionEl = document.getElementById('cfg-lookupAction');
        if (actionEl) actionEl.value = lookupPlatformEl.value === 'twitch' ? 'GTA_LookupTwitchUser' : 'GTA_LookupYouTubeUser';
      });
    }
```

- [ ] **Step 4: README**

In `README.md` add a `## Twitch Support (v0.0.5)` section listing: Twitch events and the alert each maps to (Follow, Sub, ReSub→milestone, GiftBomb→gift, GiftSub→gift received, Cheer, Raid); the new URL toggles `al_fol`, `al_cht`, `al_rd`; the three new scripts (`GTA_WalkOnTwitch.cs`, `GTA_LookupTwitchUser.cs`, plus the updated `GTA_Master_Alerts.cs` / `GTA_Shoutout.cs` with `userType=twitch`); and that Streamer.bot must have Twitch connected. Add `js/twitch-events.js` and the three scripts to the folder tree.

- [ ] **Step 5: Verify and commit**

Run: `cd /home/user/walkon-card && node --test tests/ && node tests/overlay-twitch.e2e.js`
Expected: all pass. Then serve `setup.html` (`python3 -m http.server 8099`), open the Alerts and Test tabs in the Playwright browser, pick "Twitch: Incoming Raid", press Alert, and confirm the preview shows a raid card with no console errors.

```bash
git add setup.html README.md
git commit -m "feat: add Twitch options to setup hub and docs

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01KdGkBKHb3YYSZpptrhdEHr"
```

---

### Task 6: Live payload verification and push

**Files:** none (verification), possibly `js/twitch-events.js`.

- [ ] **Step 1: Verify against real Streamer.bot payloads**

Connect `setup.html` to Streamer.bot with Twitch connected. For each of Follow, Sub, ReSub, GiftBomb, GiftSub, Cheer, Raid, fire a test event from Streamer.bot (Triggers → right-click → Test Trigger) and compare the alert shown against the expected user/amount. Any field that renders as the fallback ("Someone", `0`) means the real field name differs: add the real path to the matching `first(d, [...])` list in `js/twitch-events.js` and a test case in `tests/twitch-events.test.js` using the real payload.

- [ ] **Step 2: Push**

```bash
cd /home/user/walkon-card && git push -u origin feature/twitch-support
git ls-remote --heads origin feature/twitch-support
```

Expected: the remote SHA equals `git rev-parse HEAD`. Do not open a pull request; merge into the fork's own `main` only when the user asks.
