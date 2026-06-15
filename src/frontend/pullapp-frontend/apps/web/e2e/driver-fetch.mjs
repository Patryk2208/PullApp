// e2e: panel kierowcy pobiera pending prośby i aktywne rides z GET na wejściu.
// Kluczowe: przeżywa refresh (bez SSE) — pending z akcją accept, ride z akcją pickup.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:4000';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage',
	JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: ': empty\n\n' }));
await page.route('**/api/route/driver/requests', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'application/json' },
	body: JSON.stringify([{ requestId: 'req-1', routeId: 'route-1', passengerId: 'pass-aaaa1111', status: 'Pending', start: { lat: 52.1, lng: 21.0 }, end: { lat: 52.2, lng: 21.1 }, createdAt: new Date().toISOString() }]),
}));
await page.route('**/api/route/driver/rides', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'application/json' },
	body: JSON.stringify([{ rideId: 'ride-1', routeId: 'route-2', driverId: 'd1', passengerId: 'pass-bbbb2222', status: 'WaitingForDriver', driverDeclaredPickup: false, chatRoomId: 'c1', start: { lat: 52.3, lng: 21.0 }, end: { lat: 52.4, lng: 21.1 }, endedAt: null }]),
}));

await page.goto(`${BASE}/trips/driver`, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('text=Prośba o dołączenie', { timeout: 10000 }).catch(() => {});

ok((await page.getByText('Akceptuj').count()) >= 1, 'pending prośba z GET → akcja „Akceptuj"');
let pickup = true;
try { await page.waitForSelector('[data-testid="driver-pickup"]', { timeout: 5000 }); } catch { pickup = false; }
ok(pickup, 'aktywny ride z GET → akcja „Potwierdź odbiór" (lifecycle)');

await browser.close();
process.exit(failed ? 1 : 0);
