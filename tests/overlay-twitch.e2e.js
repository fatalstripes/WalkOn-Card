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
