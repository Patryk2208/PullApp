// e2e: akcje cyklu życia przejazdu na „Moich przejazdach" (flow 7/8).
// pickup happy → Started; pickup gdy kierowca nie potwierdził → graceful 403; cancel → Anulowany.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const ACCEPTED = 'event: ride_accepted\ndata: {"RideId":"ride-1","RouteId":"route-1","DriverId":"drv-1","EventType":"ride_accepted"}\n\n';

async function setup(routes) {
	const ctx = await browser.newContext();
	const page = await ctx.newPage();
	await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage', JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
	await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: ACCEPTED }));
	for (const [glob, handler] of routes) await page.route(glob, handler);
	await page.goto(`${BASE}/trips/my-rides`, { waitUntil: 'domcontentloaded' });
	await page.waitForSelector('[data-testid="ride-card"]', { timeout: 10000 });
	return { ctx, page };
}

// 1. pickup happy → Started
{
	const { ctx, page } = await setup([['**/api/route/passenger/rides/*/pickup', (r) => r.fulfill({ status: 204 })]]);
	await page.locator('[data-testid="ride-pickup"]').click();
	let started = true;
	try { await page.waitForSelector('[data-testid="ride-status-started"]', { timeout: 5000 }); } catch { started = false; }
	ok(started, 'pickup OK → status „W trakcie" (Started)');
	ok((await page.locator('[data-testid="ride-end"]').count()) > 0, 'pojawia się przycisk „Zakończ przejazd"');
	await ctx.close();
}

// 2. pickup gdy kierowca nie potwierdził → 403 declaration_order → graceful komunikat
{
	const { ctx, page } = await setup([['**/api/route/passenger/rides/*/pickup', (r) => r.fulfill({ status: 403, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ Code: 'declaration_order', Message: 'Driver has not declared pickup yet.' }) })]]);
	await page.locator('[data-testid="ride-pickup"]').click();
	await page.waitForSelector('[data-testid="ride-action-error"]', { timeout: 5000 }).catch(() => {});
	const err = (await page.locator('[data-testid="ride-action-error"]').textContent().catch(() => '')) || '';
	ok(/Kierowca jeszcze nie potwierdził/.test(err), `403 → przyjazny komunikat (got: "${err.trim().slice(0,40)}…")`);
	ok((await page.locator('[data-testid="ride-status-accepted"]').count()) > 0, 'status zostaje „Zaakceptowany"');
	await ctx.close();
}

// 3. cancel → Anulowany
{
	const { ctx, page } = await setup([['**/api/route/passenger/rides/*', (r) => r.request().method() === 'DELETE' ? r.fulfill({ status: 204 }) : r.continue()]]);
	await page.locator('[data-testid="ride-cancel"]').click();
	let cancelled = true;
	try { await page.waitForSelector('[data-testid="ride-status-cancelled"]', { timeout: 5000 }); } catch { cancelled = false; }
	ok(cancelled, 'cancel OK → status „Anulowany"');
	await ctx.close();
}

await browser.close();
process.exit(failed ? 1 : 0);
