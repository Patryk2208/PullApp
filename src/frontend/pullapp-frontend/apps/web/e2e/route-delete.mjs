// e2e: usunięcie trasy na publish page (flow 1.5). publish → „Usuń trasę" → potwierdzenie.
import { chromium } from 'playwright-core';
const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();
await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage', JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
await page.route('**/api/route/driver/routes', (r) => r.fulfill({ status: 202, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ routeId: 'r1' }) }));
await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: ': empty\n\n' }));
let deleteCalled = false;
await page.route('**/api/route/driver/routes/*', (r) => {
	if (r.request().method() === 'DELETE') { deleteCalled = true; return r.fulfill({ status: 204 }); }
	return r.continue();
});

await page.goto(`${BASE}/trips/publish`, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('.leaflet-container', { timeout: 10000 });
await page.waitForTimeout(400);
await page.locator('.leaflet-container').click({ position: { x: 120, y: 110 } });
await page.locator('.leaflet-container').click({ position: { x: 240, y: 170 } });
await page.getByText('Opublikuj trasę').click();
await page.waitForSelector('[data-testid="delete-route-button"]', { timeout: 8000 });

await page.locator('[data-testid="delete-route-button"]').click();
let deleted = true; try { await page.waitForSelector('[data-testid="route-deleted"]', { timeout: 5000 }); } catch { deleted = false; }
ok(deleted, 'po „Usuń trasę" → potwierdzenie „Trasa została usunięta"');
ok(deleteCalled, 'wywołano DELETE /api/route/driver/routes/{id}');

await browser.close();
process.exit(failed ? 1 : 0);
