// e2e: reguła 18+ przy rejestracji (isUserOldEnough).
// underage → błąd + brak POST /api/auth/register; dorosły → przechodzi (redirect /login).
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });

async function fillForm(page, birthDate) {
	await page.goto(`${BASE}/register`, { waitUntil: 'domcontentloaded' });
	await page.locator('input[type=text]').first().fill('Jan');
	await page.locator('input[type=text]').nth(1).fill('Kowalski');
	await page.fill('input[type=email]', `age-${Date.now()}@example.com`);
	await page.fill('input[type=password]', 'Passw0rd!');
	await page.fill('input[type=date]', birthDate);
}

// --- case 1: underage (8 lat) → blokada ---
{
	const page = await browser.newPage();
	let registerCalls = 0;
	await page.route('**/api/auth/register', (r) => { registerCalls++; return r.fulfill({ status: 201, body: '"x"' }); });
	await fillForm(page, '2018-01-01');
	await page.click('button[type=submit]');
	await page.waitForSelector('[data-testid="register-error"]', { timeout: 5000 }).catch(() => {});
	const err = (await page.locator('[data-testid="register-error"]').textContent().catch(() => '')) || '';
	ok(/18 lat/.test(err), `underage → błąd 18+ (got: "${err.trim()}")`);
	ok(registerCalls === 0, `underage → brak POST /api/auth/register (calls: ${registerCalls})`);
	ok(page.url().endsWith('/register'), 'underage → zostaje na /register');
	await page.close();
}

// --- case 2: dorosły (31 lat) → przechodzi ---
{
	const page = await browser.newPage();
	let registerCalls = 0;
	await page.route('**/api/auth/register', (r) => { registerCalls++; return r.fulfill({ status: 201, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ userId: 123 }) }); });
	await fillForm(page, '1995-01-01');
	await page.click('button[type=submit]');
	await page.waitForURL(`${BASE}/login`, { timeout: 6000 }).catch(() => {});
	ok(registerCalls === 1, `dorosły → POST /api/auth/register wywołany (calls: ${registerCalls})`);
	ok(page.url().endsWith('/login'), `dorosły → redirect na /login (URL ${page.url()})`);
	await page.close();
}

await browser.close();
process.exit(failed ? 1 : 0);
