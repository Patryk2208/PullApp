// e2e: pętla powiadomień pasażera — mock SSE wysyła `ride_accepted`,
// oczekujemy toasta success. Uruchom: node apps/web/e2e/notification-toast.mjs
import { chromium } from 'playwright-core';

const BASE = process.env.BASE || 'http://127.0.0.1:3000';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage();

// 1. wstrzyknij token (zustand persist) — bez niego listener nie połączy SSE
await page.addInitScript(() => {
	localStorage.setItem('pullapp-auth-storage',
		JSON.stringify({ state: { token: 'fake-jwt-token' }, version: 0 }));
});

// 2. zamockuj strumień SSE: jedno zdarzenie ride_accepted
await page.route('**/api/sse', async (route) => {
	await route.fulfill({
		status: 200,
		headers: { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' },
		body: 'event: ride_accepted\ndata: {"RideId":"abc","RouteId":"r1"}\n\n',
	});
});

await page.goto(BASE + '/', { waitUntil: 'domcontentloaded', timeout: 20000 });

// 3. czekamy na toast success
let appeared = true;
try {
	await page.waitForSelector('[data-testid="toast-success"]', { timeout: 10000 });
} catch { appeared = false; }
ok(appeared, 'toast success pojawia się po evencie ride_accepted');

if (appeared) {
	const txt = (await page.locator('[data-testid="toast-success"]').first().textContent())?.trim();
	console.log('   treść toasta:', txt);
	ok(!!txt && txt.includes('zaakceptował'), 'toast ma poprawną treść akceptacji');
}

await page.screenshot({ path: '/tmp/pw-smoke/notif-toast.png', fullPage: true });
console.log('screenshot: /tmp/pw-smoke/notif-toast.png');

await browser.close();
process.exit(failed ? 1 : 0);
