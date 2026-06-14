// e2e: walidacja client-side login/register (required, email, hasło) + favicon link.
import { chromium } from 'playwright-core';
const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });

// --- login: pusty submit → błąd, brak wywołania API ---
{
	const page = await browser.newPage();
	let loginCalls = 0;
	await page.route('**/api/auth/login', (r) => { loginCalls++; return r.fulfill({ status: 200, body: '{}' }); });
	await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
	await page.click('button[type=submit]');
	await page.waitForSelector('[data-testid="login-error"]', { timeout: 4000 }).catch(() => {});
	const err = (await page.locator('[data-testid="login-error"]').textContent().catch(() => '')) || '';
	ok(/Podaj e-mail i hasło/.test(err), `login pusty → błąd (got: "${err.trim()}")`);
	ok(loginCalls === 0, `login pusty → brak POST /api/auth/login (${loginCalls})`);
	// zły email — przeglądarka (type=email) blokuje submit; gwarancja: brak wywołania API
	await page.fill('input[type=email]', 'niepoprawny');
	await page.fill('input[type=password]', 'x');
	await page.click('button[type=submit]');
	await page.waitForTimeout(300);
	ok(loginCalls === 0, 'login zły email → brak POST (blokada formularza)');
	// favicon link obecny (zabity 404 /favicon.ico)
	const iconHref = await page.locator('link[rel="icon"]').first().getAttribute('href').catch(() => null);
	ok(!!iconHref, `favicon link obecny (href: ${iconHref})`);
	await page.close();
}

// --- register: krótkie hasło → błąd, brak wywołania API ---
{
	const page = await browser.newPage();
	let regCalls = 0;
	await page.route('**/api/auth/register', (r) => { regCalls++; return r.fulfill({ status: 201, body: '"x"' }); });
	await page.goto(`${BASE}/register`, { waitUntil: 'domcontentloaded' });
	await page.locator('input[type=text]').first().fill('Jan');
	await page.locator('input[type=text]').nth(1).fill('Kowalski');
	await page.fill('input[type=email]', 'jan@example.com');
	await page.fill('input[type=password]', '123');
	await page.fill('input[type=date]', '1990-01-01');
	await page.click('button[type=submit]');
	await page.waitForSelector('[data-testid="register-error"]', { timeout: 4000 }).catch(() => {});
	const err = (await page.locator('[data-testid="register-error"]').textContent().catch(() => '')) || '';
	ok(/co najmniej 6 znaków/.test(err), `register krótkie hasło → błąd (got: "${err.trim()}")`);
	ok(regCalls === 0, `register krótkie hasło → brak POST (${regCalls})`);
	await page.close();
}

await browser.close();
process.exit(failed ? 1 : 0);
