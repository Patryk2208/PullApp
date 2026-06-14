// e2e: 401 z API → auto-logout + redirect na /login (handler w ApiInitializer).
// Token w storage, /api/users/me mockowane na 401, wejście na /profile.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

// token przez evaluate (NIE addInitScript — to re-seedowałoby przy redirect na /login)
await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await page.evaluate(() => localStorage.setItem('pullapp-auth-storage',
	JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));

// authenticatedApiClient (UserRepository.me) → 401
await page.route('**/api/users/me', (r) => r.fulfill({
	status: 401, headers: { 'content-type': 'application/json' },
	body: JSON.stringify({ detail: 'Unauthorized' }),
}));

await page.goto(`${BASE}/profile`, { waitUntil: 'domcontentloaded' });

await page.waitForURL(`${BASE}/login`, { timeout: 10000 }).catch(() => {});
ok(page.url() === `${BASE}/login`, `401 → redirect na /login (URL ${page.url()})`);

const token = await page.evaluate(() => {
	const raw = localStorage.getItem('pullapp-auth-storage');
	try { return JSON.parse(raw)?.state?.token; } catch { return raw; }
});
ok(!token, `token wyczyszczony po 401 (jest: ${JSON.stringify(token)})`);

await browser.close();
process.exit(failed ? 1 : 0);
