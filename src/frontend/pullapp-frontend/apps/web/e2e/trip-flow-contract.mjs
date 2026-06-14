// Integration: pełny core trip-flow przeciw realnemu gatewayowi (:8080).
// Kodyfikuje kontrakty (payloady + kształty eventów SSE) używane przez front:
// publish → activate → search → request → ride_requested(SSE) → accept → ride_accepted(SSE).
// Wymaga: backend up + gateway na :8080. Czysty fetch, bez przeglądarki.
const GW = process.env.GW || 'http://127.0.0.1:8080';
let failed = false;
const ok = (c, m) => { console.log(`${c ? 'PASS' : 'FAIL'}: ${m}`); if (!c) failed = true; };

async function newUser() {
	const email = `flow-${Date.now()}-${Math.random().toString(36).slice(2, 7)}@example.com`;
	await fetch(`${GW}/api/auth/register`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ name: 'F', surname: 'L', email, password: 'Passw0rd!', birthDate: '1990-01-01' }) });
	const r = await fetch(`${GW}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ email, password: 'Passw0rd!' }) });
	return (await r.json()).accessToken;
}

// czeka na pierwszy event SSE danego typu (lub null po timeout)
async function waitEvent(token, type, ms = 9000) {
	const ctrl = new AbortController();
	const timer = setTimeout(() => ctrl.abort(), ms);
	try {
		const res = await fetch(`${GW}/sse/notifications`, { headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' }, signal: ctrl.signal });
		const reader = res.body.getReader(); const dec = new TextDecoder();
		let buf = '', et = '';
		while (true) {
			const { done, value } = await reader.read(); if (done) break;
			buf += dec.decode(value, { stream: true });
			const lines = buf.split('\n'); buf = lines.pop() ?? '';
			for (const line of lines) {
				if (line.startsWith('event:')) et = line.slice(6).trim();
				else if (line.startsWith('data:')) {
					let d; try { d = JSON.parse(line.slice(5).trim()); } catch { d = null; }
					if (et === type) { clearTimeout(timer); ctrl.abort(); return d; }
					et = '';
				}
			}
		}
	} catch { /* abort */ } finally { clearTimeout(timer); }
	return null;
}
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const post = (path, token, body) => fetch(`${GW}${path}`, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });

const A = { Latitude: 52.23, Longitude: 21.01 }, B = { Latitude: 52.40, Longitude: 21.05 };
const driver = await newUser();
const passenger = await newUser();

// publish
const pubRes = await post('/api/route/driver/routes', driver, { Start: A, End: B, Capacity: 3 });
const pub = await pubRes.json();
ok(pubRes.status === 202 && typeof pub.routeId === 'string', `publish → 202 {routeId} (${pubRes.status})`);
const routeId = pub.routeId;

// activate — geometria liczy się async po publish; 409 dopóki status != Created,
// więc czekamy (front NIE ma dziś sygnału gotowości — patrz DECISIONS it.6)
let actRes;
for (let i = 0; i < 12; i++) {
	actRes = await post(`/api/route/driver/routes/${routeId}/activate`, driver, { CurrentLocation: A });
	if (actRes.status !== 409) break;
	await sleep(700);
}
ok(actRes.status === 204, `activate → 204 po dojściu geometrii (${actRes.status})`);

// search
const searchRes = await post('/api/route/passenger/routes/search', passenger, { Start: A, End: B, DepartureDate: Date.now(), SeatsNeeded: 1, MaxDetourKm: 5, TimeWindowMinutes: 30 });
const search = await searchRes.json();
ok(searchRes.status === 202 && typeof search.jobId === 'string', `search → 202 {jobId} (${searchRes.status})`);

// request + ride_requested(SSE→driver)
const reqEventP = waitEvent(driver, 'ride_requested');
await sleep(1200);
const reqRes = await post(`/api/route/passenger/routes/${routeId}/requests`, passenger, { Start: { Latitude: 52.26, Longitude: 21.02 }, End: { Latitude: 52.37, Longitude: 21.04 } });
const req = await reqRes.json();
ok(reqRes.status === 201 && typeof req.requestId === 'string', `request → 201 {requestId} (${reqRes.status})`);
const reqEvt = await reqEventP;
ok(reqEvt && reqEvt.RequestId === req.requestId, 'SSE ride_requested → poprawny RequestId (do kierowcy)');
ok(reqEvt && reqEvt.StartPoint && typeof reqEvt.StartPoint.Latitude === 'number', 'SSE ride_requested → StartPoint.Latitude (kształt jak parser panelu)');

// accept + ride_accepted(SSE→passenger)
const accEventP = waitEvent(passenger, 'ride_accepted');
await sleep(1200);
const accRes = await post(`/api/route/driver/requests/${req.requestId}/accept`, driver);
const acc = await accRes.json();
ok(accRes.status === 200 && typeof acc.rideId === 'string', `accept → 200 {rideId,chatRoomId} (${accRes.status})`);
const accEvt = await accEventP;
ok(accEvt && typeof accEvt.RideId === 'string', 'SSE ride_accepted → dociera do pasażera (nazwa eventu zgodna z toastem)');

// reject path → ride_rejected(SSE→passenger)
const passenger2 = await newUser();
const req2Res = await post(`/api/route/passenger/routes/${routeId}/requests`, passenger2, { Start: { Latitude: 52.27, Longitude: 21.02 }, End: { Latitude: 52.36, Longitude: 21.04 } });
const req2 = await req2Res.json();
ok(req2Res.status === 201 && typeof req2.requestId === 'string', `request#2 → 201 {requestId} (${req2Res.status})`);
const rejEventP = waitEvent(passenger2, 'ride_rejected');
await sleep(1200);
const rejRes = await post(`/api/route/driver/requests/${req2.requestId}/reject`, driver);
ok(rejRes.status === 204, `reject → 204 (${rejRes.status})`);
const rejEvt = await rejEventP;
ok(rejEvt && rejEvt.RequestId === req2.requestId, 'SSE ride_rejected → dociera do pasażera (nazwa eventu zgodna z toastem)');

await new Promise(r => setTimeout(r, 200));
process.exit(failed ? 1 : 0);
