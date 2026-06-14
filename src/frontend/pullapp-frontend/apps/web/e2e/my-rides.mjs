// e2e: widok „Moje przejazdy" zasilany z SSE (backend nie ma GET).
// mock ride_accepted → karta przejazdu ze statusem „Zaakceptowany".
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage',
	JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));
await page.route('**/api/sse', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'text/event-stream' },
	body: 'event: ride_accepted\ndata: {"RideId":"ride-1","RouteId":"route-1234","DriverId":"driver-abcd1234","ChatRoomId":"chat-1","EventType":"ride_accepted"}\n\n',
}));

await page.goto(`${BASE}/trips/my-rides`, { waitUntil: 'domcontentloaded' });

let appeared = true;
try { await page.waitForSelector('[data-testid="ride-card"]', { timeout: 10000 }); }
catch { appeared = false; }
ok(appeared, 'karta przejazdu pojawia się po ride_accepted (store z SSE)');

if (appeared) {
	ok((await page.locator('[data-testid="ride-status-accepted"]').count()) > 0, 'status „Zaakceptowany"');
	ok((await page.getByText('driver-a').count()) > 0, 'pokazany id kierowcy');
}

// trwałość: zablokuj SSE (puste) i reload — przejazd MUSI przyjść z persistu
await page.unroute('**/api/sse');
await page.route('**/api/sse', (r) => r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: ': empty\n\n' }));
await page.reload({ waitUntil: 'domcontentloaded' });
let persisted = true;
try { await page.waitForSelector('[data-testid="ride-card"]', { timeout: 8000 }); } catch { persisted = false; }
ok(persisted, 'przejazd przetrwał reload z pustego SSE (persist localStorage)');

await browser.close();
process.exit(failed ? 1 : 0);
