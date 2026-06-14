// e2e: passenger search (flow 2) end-to-end through the real stack.
// Verifies: map point selection enables the button, the search POST is accepted
// (points land inside the service area), the SSE round-trip completes, and NO CORS
// error shows up in the browser. Run: node apps/web/e2e/search-flow.mjs
// Requires: prod web on :4000 (proxy /api+/sse → gateway :8080) + backend up.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:4000';
const GW   = process.env.GW   || 'http://127.0.0.1:8080';
const EMAIL = process.env.EMAIL || 'loadtest@pullapp.dev';
const PASSWORD = process.env.PASSWORD || 'loadtest123';

let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

// token (register-if-needed, then login)
await fetch(`${GW}/api/auth/register`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ Name: 'Load', Surname: 'Tester', Email: EMAIL, Password: PASSWORD, BirthDate: '1995-01-01' }),
}).catch(() => {});
const token = (await (await fetch(`${GW}/api/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ Email: EMAIL, Password: PASSWORD }),
})).json()).accessToken;
ok(!!token, 'got JWT from gateway');

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();
await page.addInitScript((t) => {
  localStorage.setItem('pullapp-auth-storage', JSON.stringify({ state: { token: t }, version: 0 }));
}, token);

// CORS watchdog: any cross-origin block on the SSE or API surfaces here.
let corsSeen = false;
const corsRe = /cors|access-control|cross-origin/i;
page.on('console', m => { if (corsRe.test(m.text())) corsSeen = true; });
page.on('requestfailed', r => { if (corsRe.test(r.failure()?.errorText || '')) corsSeen = true; });

await page.goto(`${BASE}/trips/search`, { waitUntil: 'domcontentloaded', timeout: 30000 });
await page.waitForTimeout(2000); // shared notification SSE connects on mount

// select start + end on the map (near centre → inside the Warsaw service area)
await page.waitForSelector('.leaflet-container', { timeout: 15000 });
const box = await (await page.$('.leaflet-container')).boundingBox();
await page.mouse.click(box.x + box.width * 0.49, box.y + box.height * 0.49);
await page.waitForTimeout(400);
await page.mouse.click(box.x + box.width * 0.51, box.y + box.height * 0.51);
await page.waitForTimeout(400);
ok((await page.getByText('Start wybrany').count()) > 0, 'map clicks set start + end');

const btn = page.locator('button[type="submit"]');
ok(!(await btn.isDisabled()), 'search button enabled once both points are set');

await page.fill('input[name="departureDate"]', '2026-07-01T10:00');
await btn.click({ timeout: 5000 });

// SSE round-trip must complete: either matches render or the "nobody driving"
// completion message. An "API rejected" / "cannot connect" message means it broke.
let outcome = 'timeout';
await Promise.race([
  page.getByText(/Znalezione przejazdy/).waitFor({ timeout: 60000 }).then(() => outcome = 'matches'),
  page.getByText(/nikt aktualnie nie jedzie|Nie znaleziono/i).waitFor({ timeout: 60000 }).then(() => outcome = 'empty'),
  page.getByText(/API odrzuci|nie można połączy/i).waitFor({ timeout: 60000 }).then(() => outcome = 'error'),
]).catch(() => {});

ok(outcome === 'matches' || outcome === 'empty', `search SSE round-trip completed (outcome=${outcome})`);
ok(!corsSeen, 'no CORS error in the browser');

await page.screenshot({ path: '/tmp/pw-smoke/search-flow.png', fullPage: true });
await browser.close();
process.exit(failed ? 1 : 0);
