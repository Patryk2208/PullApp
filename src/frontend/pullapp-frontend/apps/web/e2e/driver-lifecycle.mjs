// e2e: panel kierowcy — accept → driver pickup → driver end (flow 7/8 strona kierowcy).
import { chromium } from 'playwright-core';
const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();
await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage', JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' },
	body: 'event: ride_requested\ndata: {"RequestId":"req-1","RouteId":"route-1","PassengerId":"pass-1","StartPoint":{"Latitude":52.1,"Longitude":21.0},"EndPoint":{"Latitude":52.2,"Longitude":21.1}}\n\n' }));
await page.route('**/api/route/driver/requests/*/accept', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ rideId: 'ride-1', chatRoomId: 'c1' }) }));
await page.route('**/api/route/driver/rides/*/pickup', (r) => r.fulfill({ status: 204 }));
await page.route('**/api/route/driver/rides/*/end', (r) => r.fulfill({ status: 204 }));

await page.goto(`${BASE}/trips/driver`, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('text=Prośba o dołączenie', { timeout: 10000 });

await page.getByText('Akceptuj').click();
let p1 = true; try { await page.waitForSelector('[data-testid="driver-pickup"]', { timeout: 5000 }); } catch { p1 = false; }
ok(p1, 'po accept → przycisk „Potwierdź odbiór pasażera"');

await page.locator('[data-testid="driver-pickup"]').click();
let p2 = true; try { await page.waitForSelector('[data-testid="driver-end"]', { timeout: 5000 }); } catch { p2 = false; }
ok(p2, 'po driver pickup → przycisk „Zakończ przejazd" (Odbiór potwierdzony)');

await page.locator('[data-testid="driver-end"]').click();
let p3 = true; try { await page.waitForSelector('text=Zakończony', { timeout: 5000 }); } catch { p3 = false; }
ok(p3, 'po driver end → status „Zakończony"');

await browser.close();
process.exit(failed ? 1 : 0);
