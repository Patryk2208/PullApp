// e2e: panel kierowcy na współdzielonym strumieniu SSE.
// mock ride_requested → pojawia się karta prośby z akcjami.
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3001';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

await page.addInitScript(() => localStorage.setItem('pullapp-auth-storage',
	JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 })));

const body =
	'event: ride_requested\n' +
	'data: {"RequestId":"req1","RouteId":"r1","PassengerId":"pass1234abcd","StartPoint":{"Latitude":52.1,"Longitude":21.0},"EndPoint":{"Latitude":52.2,"Longitude":21.1}}\n\n';
await page.route('**/api/sse', (r) => r.fulfill({
	status: 200, headers: { 'content-type': 'text/event-stream' }, body,
}));

await page.goto(`${BASE}/trips/driver`, { waitUntil: 'domcontentloaded' });

let cardAppeared = true;
try {
	await page.waitForSelector('text=Prośba o dołączenie', { timeout: 10000 });
} catch { cardAppeared = false; }
ok(cardAppeared, 'karta ride_requested pojawia się (współdzielony SSE)');

if (cardAppeared) {
	ok((await page.getByText('Akceptuj').count()) > 0, 'karta ma akcję Akceptuj');
	ok((await page.getByText('pass1234').count()) > 0, 'karta pokazuje id pasażera');
}

await page.screenshot({ path: '/tmp/pw-smoke/driver-request.png', fullPage: true });
await browser.close();
process.exit(failed ? 1 : 0);
