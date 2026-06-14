// e2e: nawigacja login ↔ register (linki krzyżowe).
import { chromium } from 'playwright-core';
const BASE = process.env.BASE || 'http://127.0.0.1:4000';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await page.locator('[data-testid="to-register"]').click();
await page.waitForURL(`${BASE}/register`, { timeout: 6000 }).catch(() => {});
ok(page.url().endsWith('/register'), `login → „Zarejestruj się" → /register (${page.url()})`);

await page.locator('[data-testid="to-login"]').click();
await page.waitForURL(`${BASE}/login`, { timeout: 6000 }).catch(() => {});
ok(page.url().endsWith('/login'), `register → „Zaloguj się" → /login (${page.url()})`);

await browser.close();
process.exit(failed ? 1 : 0);
