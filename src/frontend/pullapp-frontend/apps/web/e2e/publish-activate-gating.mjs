// e2e: przycisk „Aktywuj" odblokowuje się dopiero po evencie SSE route_ready.
// Mock publish API + mock SSE (z/bez route_ready). Mapa: 2 kliknięcia = start+meta.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });

async function run({ withReady }) {
	const page = await browser.newPage();
	await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage',
		JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
	await page.route('**/api/route/driver/routes', (r) =>
		r.fulfill({ status: 202, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ routeId: 'r1' }) }));
	await page.route('**/api/sse', (r) => r.fulfill({
		status: 200, headers: { 'content-type': 'text/event-stream' },
		body: withReady
			? 'event: route_ready\ndata: {"RouteId":"r1","EventType":"route_ready"}\n\n'
			: ': keepalive\n\n',
	}));

	await page.goto(`${BASE}/trips/publish`, { waitUntil: 'domcontentloaded' });
	await page.waitForSelector('.leaflet-container', { timeout: 10000 });
	await page.waitForTimeout(500);
	// 2 kliknięcia w mapę: start + meta
	await page.locator('.leaflet-container').click({ position: { x: 120, y: 110 } });
	await page.locator('.leaflet-container').click({ position: { x: 240, y: 170 } });
	await page.getByText('Opublikuj trasę').click();

	await page.waitForSelector('[data-testid="activate-button"]', { timeout: 8000 });
	await page.waitForTimeout(1000); // daj SSE dojść
	const btn = page.locator('[data-testid="activate-button"]');
	const disabled = await btn.isDisabled();
	const label = (await btn.textContent())?.trim() || '';

	if (withReady) {
		ok(!disabled, `route_ready → przycisk ODBLOKOWANY (disabled=${disabled})`);
		ok(/Aktywuj/.test(label), `route_ready → label „Aktywuj" (got: "${label}")`);
	} else {
		ok(disabled, `brak route_ready → przycisk ZABLOKOWANY (disabled=${disabled})`);
		ok(/Czekam/.test(label), `brak route_ready → label „Czekam…" (got: "${label}")`);
	}
	await page.close();
}

await run({ withReady: true });
await run({ withReady: false });
await browser.close();
process.exit(failed ? 1 : 0);
