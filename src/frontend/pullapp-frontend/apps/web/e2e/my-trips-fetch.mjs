// e2e: „Moje przejazdy" pobiera prośby i przejazdy z GET (read-model) na wejściu.
// Kluczowe: request widać bez SSE (ze źródła prawdy) → przeżywa refresh.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:4000';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage',
	JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));

// SSE puste — wszystko musi przyjść z GET
await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: ': empty\n\n' }));
await page.route('**/api/route/passenger/requests', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'application/json' },
	body: JSON.stringify([{ requestId: 'req-1', routeId: 'route-12345', status: 'Pending', start: { lat: 52.2, lng: 21.0 }, end: { lat: 52.3, lng: 21.1 }, createdAt: new Date().toISOString() }]),
}));
await page.route('**/api/route/passenger/rides', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'application/json' },
	body: JSON.stringify([{ rideId: 'ride-1', routeId: 'route-9', driverId: 'drv-abcd1234', passengerId: 'p1', status: 'WaitingForDriver', chatRoomId: 'c1', endedAt: null }]),
}));

await page.goto(`${BASE}/trips/my-rides`, { waitUntil: 'domcontentloaded' });

let reqShown = true;
try { await page.waitForSelector('[data-testid="request-card"]', { timeout: 10000 }); } catch { reqShown = false; }
ok(reqShown, 'prośba (RideRequest) widoczna z GET — bez SSE');
ok((await page.locator('[data-testid="request-status-Pending"]').count()) > 0, 'status prośby „Oczekuje"');

let rideShown = true;
try { await page.waitForSelector('[data-testid="ride-card"]', { timeout: 5000 }); } catch { rideShown = false; }
ok(rideShown, 'przejazd zhydrowany z GET /passenger/rides (bez SSE)');

await browser.close();
process.exit(failed ? 1 : 0);
