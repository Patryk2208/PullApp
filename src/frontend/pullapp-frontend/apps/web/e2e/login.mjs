// e2e: realne logowanie przez UI (weryfikuje fix accessToken).
// Rejestruje usera przez API, loguje się formularzem, sprawdza zalogowany stan.
// Wymaga: prod web na :3001 (proxy /api → gateway :8080) + backend up.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
const GW = process.env.GW || 'http://127.0.0.1:8080';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const email = `loop-e2e-${Date.now()}@example.com`;
const password = 'Passw0rd!';

// 1. rejestracja przez API
const reg = await fetch(`${GW}/api/auth/register`, {
	method: 'POST', headers: { 'Content-Type': 'application/json' },
	body: JSON.stringify({ name: 'Loop', surname: 'E2E', email, password, birthDate: '2000-01-01' }),
});
ok(reg.status === 201, `rejestracja API (201) — got ${reg.status}`);

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

// 2. login przez UI
await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await page.fill('input[type=email]', email);
await page.fill('input[type=password]', password);
await page.click('button[type=submit]');

// 3. sukces = redirect na / + Navbar pokazuje zalogowany stan
await page.waitForURL(`${BASE}/`, { timeout: 10000 }).catch(() => {});
ok(page.url() === `${BASE}/`, `redirect na / po logowaniu — URL ${page.url()}`);

await page.waitForSelector('text=Wyloguj', { timeout: 6000 }).catch(() => {});
const loggedIn = await page.getByText('Wyloguj').count();
ok(loggedIn > 0, 'Navbar pokazuje "Wyloguj" (sesja ustawiona — fix accessToken)');

const stored = await page.evaluate(() => localStorage.getItem('pullapp-auth-storage'));
ok(!!stored && /"token":"ey/.test(stored), 'token JWT zapisany w storage');

await page.screenshot({ path: '/tmp/pw-smoke/login-e2e.png', fullPage: true });
console.log('screenshot: /tmp/pw-smoke/login-e2e.png');
await browser.close();
process.exit(failed ? 1 : 0);
