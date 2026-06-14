# DECISIONS — frontend loop session (feature/frontend/loop-session)

Baseline: `a85358a` (od `feature/frontend/base`). Sesja samodzielna, ~60 min, mix per iteracja.
Backend traktowany jako nietykalny (fix+flag jeśli zepsuty). Weryfikacja: Playwright (playwright-core + system Chrome).

## Znane flagi / ograniczenia środowiska
- `GET /api/users/me` → 404 z gatewaya (zamiast 401). Routing backendu — NIE ruszam. Może blokować flow profilu; w e2e obchodzę mockiem.
- `pnpm dev` przez turbo odpala też native/expo; używam `--filter @pullapp/web` (tylko web na :3000).
- Pełne e2e (2 role, JWT, async matching) jest ciężkie do napędzenia skryptem → flow UI weryfikuję **mockując `/api/sse` i `/api/*`** w Playwright (route.fulfill), render/logikę client-side testuję wprost.

---

## Iteracja 1 — pętla powiadomień pasażera (flow 4/5 domknięcie)

**Problem.** `useSearchTrips` otwiera SSE, czyta jeden `route_search_completed` i **abortuje** połączenie. Po wysłaniu RideRequest pasażer nie ma żadnego nasłuchu na `ride_accepted` / `ride_rejected` — kierowca akceptuje/odrzuca, a pasażer nigdy się nie dowiaduje w aplikacji. Panel kierowcy ma własny trwały SSE tylko na `ride_requested`. Brak wspólnej, trwałej warstwy powiadomień.

**Decyzja.**
- Dodaję współdzielony, trwały strumień SSE jako hook w `@pullapp/features`: `useNotificationStream` — łączy się gdy jest token, parsuje eventy SSE, woła `onEvent`.
- Globalny komponent `NotificationListener` (web), montowany w `layout.tsx`, subskrybuje strumień i pokazuje toasty dla `ride_accepted` / `ride_rejected` / `ride_ended` (pasażer) — domyka pętlę zwrotną.
- Lekki, bezzależnościowy toast UI (bez nowych paczek).

**Czemu tak.** Najwyższa dźwignia — bez tego cały flow request→accept jest „ślepy" po stronie pasażera. Warstwa trwała jest reużywalna pod kolejne eventy (flow 7/8) i pozwoli później skonsolidować SSE panelu kierowcy.

**Świadomy dług.** Panel kierowcy na razie zostaje z własnym SSE (dwa połączenia). Konsolidacja na jeden strumień = osobna iteracja.

**Weryfikacja.** Playwright: mock `/api/sse` zwraca event-stream z `ride_accepted`; asercja że toast się pokazuje. + render bez błędów.

### 🔴 Znaleziska podczas iteracji 1 (ważne)

1. **BUG (naprawiony, frontend): logowanie nigdy nie działało.** Backend `POST /api/auth/login` zwraca `{"accessToken": "..."}`, a frontend (`LoginUserResponse.token`, `useLogin`) czytał `result.value.token` → `undefined`, więc `setToken(undefined)` — sesja nigdy się nie ustawiała. Potwierdzone curlem na realnym backendzie. Fix: `LoginUserResponse.accessToken` + `useLogin` używa `accessToken` (+ usunięte debug logi). Backend NIE ruszany.

2. **Dev server (`next dev`, :3000) nie hydratuje klienta w headless Chrome.** 0 logów klienta, brak rehydracji zustand, HMR WebSocket pada `ERR_INVALID_HTTP_RESPONSE`. Build produkcyjny (:5000) hydratuje poprawnie (zweryfikowane: Navbar pokazuje zalogowany stan, format tokena OK). **Decyzja: e2e wymagające hydracji testuję na buildzie, nie na dev.** Dev zostaje do szybkich sprawdzeń renderu/SSR. (Do zdiagnozowania osobno — prawdopodobnie konfiguracja HMR/origin Next 16.)

3. Format zustand persist potwierdzony: klucz `pullapp-auth-storage`, wartość `{"state":{"token":"..."},"version":0}`.

## Iteracja 2 — diagnoza flagi `/api/users/me`

Z ważnym tokenem `GET /api/users/me` (i warianty ścieżki) → **404**. Gateway MA route
`accounts-users-route: /api/users/{**catch-all}` → forward do accounts as-is. Czyli
**accounts nie serwuje `GET /api/users/me`** — endpoint brakuje/ma inną ścieżkę.
→ **Gap BACKENDU** (psuje stronę profilu: `UserRepository.me` → `useProfile`). NIE ruszam (backend).
Frontend zachowuje się poprawnie (pokazuje błąd), więc nic do naprawy po stronie frontu bez endpointu.

## Iteracja 3 — hardening auth (401 auto-logout + czystość)

**Problem.** `ApiInitializer` rejestrował no-op handler 401 (auto-logout zakomentowany) — wygasły/niepoprawny token zostawiał usera w martwym stanie. Dodatkowo ~12 debug `console.log` (tokeny w konsoli!) w Navbar, apiClient, ApiInitializer, UserRepository, useProfile, driver.

**Decyzja.**
- 401 z `authenticatedApiClient` → `useAuthStore.logout()` + redirect na `/login` (jeśli nie jesteśmy już tam).
- Wycięte wszystkie debug `console.log` (zostaje tylko `console.warn` realnych błędów strumienia SSE).

**Weryfikacja.** Playwright: token w storage, `/api/users/me` mock 401, wejście na `/profile` → redirect `/login` + token wyczyszczony.

## Iteracja 4 — reguła wieku 18+ przy rejestracji

**Problem.** Domena ma `isUserOldEnough` z `// TODO USE THIS`, ale formularz rejestracji nie waliduje wieku (ani nie był eksportowany z `@pullapp/domain`). Scaffold = spec → implementuję.

**Decyzja.**
- Eksport `rules` z `packages/domain/src/index.ts`.
- Walidacja client-side w `register/page.tsx`: brak daty → błąd; `!isUserOldEnough(new Date(birthDate))` → błąd „18 lat", BEZ wywołania `register` (nie bijemy w backend dla nieletnich).
- Komunikat w `data-testid="register-error"`.

**Weryfikacja.** Playwright: underage (8 lat) → błąd + 0 POST `/api/auth/register` + zostaje na /register; dorosły (31 lat) → POST wywołany + redirect /login (register mockowany).

## Iteracja 5 — jeden współdzielony transport SSE (spłata długu z it.1)

**Problem.** Po it.1 były DWA połączenia SSE: globalny `NotificationListener` i panel kierowcy (własny `useEffect` z fetch+parsowaniem, ~65 linii duplikatu).

**Decyzja.** `useNotificationStream` przepisany na singleton: moduł trzyma jedno połączenie + zbiór subskrybentów. Połączenie wstaje przy 1. subskrybencie, znika przy ostatnim, reconnect przy zmianie tokena. Panel kierowcy używa teraz `useNotificationStream(handleEvent)` (filtr `ride_requested` → karty); wskaźnik połączenia uproszczony do `!!token`. Usunięte ~65 linii zduplikowanego SSE.

**Weryfikacja.** Playwright: mock `ride_requested` na `/trips/driver` → karta prośby + akcje + id pasażera. Regresja: toasty pasażera nadal działają (ten sam strumień).

## Iteracja 6 — weryfikacja rdzenia trip-flow (publish→search→request→accept)

**Wynik: rdzeń NIE ma mismatchy kontraktu.** Przeszedłem cały flow przeciw realnemu backendowi i wszystkie payloady/odpowiedzi/eventy zgadzają się z frontem:
- `POST /driver/routes` → 202 `{routeId}` ✅ (front czyta `data.routeId`)
- `POST /driver/routes/{id}/activate` → 204 ✅
- `POST /passenger/routes/search` → 202 `{jobId}` ✅ (front i tak czeka na SSE)
- `POST /passenger/routes/{id}/requests` → 201 `{requestId}` ✅
- SSE `ride_requested` → kształt `{RequestId,RouteId,PassengerId,StartPoint:{Latitude,Longitude},…}` **dokładnie** jak parser panelu kierowcy ✅
- `POST /driver/requests/{id}/accept` → 200 `{rideId,chatRoomId}` ✅
- SSE `ride_accepted` → dociera do pasażera, nazwa eventu zgodna z toastem z it.1 ✅

Czyli jedynym realnym bugiem kontraktu był `accessToken` (login) — już naprawiony. Po jego naprawie rdzeń działa.

**🟡 Znalezisko (race przy aktywacji).** Geometria trasy liczy się **async** po publish (202). `activate` zwraca **409** dopóki status != `Created`. Front NIE ma dziś sygnału gotowości — user może kliknąć „Aktywuj" za wcześnie i dostać błąd. **Rekomendacja (przyszła iteracja):** polling statusu trasy lub event `route_created`, i gejtowanie przycisku activate do czasu gotowości.

**Artefakt:** `apps/web/e2e/trip-flow-contract.mjs` — integracyjny regression guard całego flow (czysty fetch, bez przeglądarki, przeciw :8080).

## Iteracja 7 — fix race aktywacji (gating na evencie route_ready)

**Problem (znaleziony w it.6).** Geometria liczy się async po publish; `activate` zwraca 409 dopóki trasa nie jest `Created`. Front zawsze pokazywał aktywny przycisk „Aktywuj" → user mógł kliknąć za wcześnie i dostać błąd.

**Odkrycie.** Backend emituje do kierowcy SSE `route_ready` gdy geometria gotowa:
`{RouteId, DriverId, RoutePoints, DistanceMeters, DurationSeconds, EventType:"route_ready"}`.

**Decyzja.** `publish/page.tsx` subskrybuje współdzielony strumień (`useNotificationStream`), zapisuje `readyRouteId` z `route_ready` (obsługa obu kolejności event↔result), i `routeReady = !!result && readyRouteId === result.routeId`. Przycisk activate jest **disabled** + „⏳ Czekam na gotowość trasy…" dopóki nie przyjdzie event; potem odblokowany + „🟢 Aktywuj…". Eliminuje race 409.

**Weryfikacja.** Playwright (kliknięcia w Leaflet + mock publish/SSE): z `route_ready` → przycisk enabled + „Aktywuj"; bez → disabled + „Czekam…". Pełna suite bez regresji.

## Iteracja 8 — domknięcie kontraktu powiadomień (reject)

Zweryfikowane: `reject` → 204, SSE `ride_rejected` do pasażera (`{RequestId,RouteId,DriverId,PassengerId,EventType:"ride_rejected"}`) — nazwa zgodna z toastem z it.1. Rozszerzony `trip-flow-contract.mjs` o ścieżkę reject (11 asercji). Cały kontrakt accept/reject/ride_requested/ride_accepted/ride_rejected potwierdzony przeciw realnemu backendowi.

## Iteracja #2 (plan) — widok „Moje przejazdy" pasażera

**Odkrycie API.** Trip-planner ma komendy ale **ZERO endpointów GET** (brak `GET /passenger/rides`, `GET /driver/routes`). Stan rides istnieje wyłącznie w eventach SSE. Eventy lifecycle potwierdzone: `ride_accepted/ride_rejected/ride_ended/route_deleted/route_ready/ride_requested`. **Brak `ride_started`** — gdy oba pickupy → Started, żaden event nie leci (UI nie dowie się reaktywnie o starcie).

**Decyzja.** `useRidesStore` (zustand+persist `pullapp-rides-storage`) zasilany z globalnego `NotificationListener` (jedyne źródło prawdy). Strona `/trips/my-rides` listuje przejazdy ze statusem, aktualizacja na żywo, link w Navbarze. Persist → przeżywa reload (mitygacja braku GET).

**🟡 Flaga backendu:** brak read-modelu rides/routes (GET) — widoki budowane z eventów, świeża sesja na innym urządzeniu nie zobaczy historii. Wymaga endpointów GET po stronie trip-planner.

**Weryfikacja.** Playwright: mock `ride_accepted` → karta ze statusem + przeżywa reload.

## Iteracja #3 — cykl życia Ride (pickup/end/cancel, flow 7/8)

**Kontrakty zweryfikowane przeciw backendowi:**
- `POST /passenger/rides/{id}/pickup` → 204 (tylko PO deklaracji kierowcy; inaczej 403 `declaration_order`). Sukces ⇒ Started.
- `POST /passenger/rides/{id}/end` → 204 (tylko w Started; inaczej 409 `invalid_status`).
- `DELETE /passenger/rides/{id}` → 204 (cancel).

**Decyzja.** Hook `useRideActions` (pickup/end/cancel + optymistyczna zmiana statusu w store). Na „Moich przejazdach" przyciski wg statusu: accepted → [Potwierdź odbiór][Anuluj], started → [Zakończ przejazd]. 403 `declaration_order` mapowane na przyjazny komunikat „Kierowca jeszcze nie potwierdził odbioru…".

**🟡 Flaga backendu (poważna dla UX flow 7).** Brak eventu gdy kierowca zadeklaruje odbiór ani gdy ride → Started. Pasażer nie wie, kiedy może deklarować swój pickup — może tylko próbować i dostawać 403. Również brak `ride_started` → druga strona nie widzi reaktywnie startu. Wymaga eventów `driver_pickup_declared` / `ride_started` po stronie backendu.

**Weryfikacja.** Playwright (mock endpointów): pickup→Started+przycisk Zakończ; 403→przyjazny komunikat+status bez zmian; cancel→Anulowany.

## Iteracja #4 — strona kierowcy: lifecycle + delete trasy

**Kontrakty zweryfikowane:** driver pickup/end → 204; `DELETE /driver/routes/{id}` → 204 (świeża), 404 `route_not_found`, 403 `route_not_deletable` (aktywna z rides).

**Decyzja.**
- Panel kierowcy: po accept zapisujemy `rideId` z odpowiedzi; karta dostaje przyciski lifecycle — accepted → [Potwierdź odbiór pasażera] (driver pickup) → pickedup → [Zakończ przejazd] (driver end) → ended. Błąd 409 (end przed Started) → przyjazny komunikat „Pasażer jeszcze nie potwierdził odbioru".
- Publish page: [🗑️ Usuń trasę] (DELETE), po sukcesie „Trasa została usunięta".

**🟡 Flaga (jak w #3):** driver pickup → 204 ale ride zostaje WaitingForDriver do czasu pickup pasażera; brak eventu → kierowca nie wie reaktywnie o przejściu w Started.

**Weryfikacja.** Playwright: driver accept→pickup→end (status „Zakończony"); publish→Usuń trasę→potwierdzenie + wywołane DELETE.
