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

## Iteracja #5 — hardening: walidacja formularzy + favicon

**Decyzja.**
- `isValidEmail` w domenie (reużywalne). Login: blokada pustego submitu + format email (brak zbędnego POST). Register: required wszystkie pola + email + hasło min 6 znaków (przed checkiem 18+).
- `app/icon.svg` (Next App Router) → emituje `<link rel="icon">`, zabija 404 na `/favicon.ico`.

**Weryfikacja.** Playwright: login pusty/zły-email → błąd + 0 POST; register krótkie hasło → błąd + 0 POST; favicon link obecny.

---

## Podsumowanie sesji (5 iteracji wg planu)
#1 ✅ weryfikacja rdzenia trip-flow (+ fix race activate, kontrakt accept/reject) — it.6/7/8
#2 ✅ widok „Moje przejazdy" pasażera (store z SSE, persist)
#3 ✅ cykl życia Ride pasażera (pickup/end/cancel)
#4 ✅ strona kierowcy: lifecycle (driver pickup/end) + delete trasy
#5 ✅ hardening: walidacja formularzy + favicon

**Flagi backendu (do naprawy po stronie serwisów):** brak GET read-modelu rides/routes; brak eventów `driver_pickup_declared`/`ride_started`; `GET /api/users/me` 404 (profil). `next dev` nie hydratuje w headless (e2e na buildzie).

## Post-sesja — drobne fixy

**A. Linki login↔register.** Brakowało nawigacji do `/register` (trzeba było wpisywać URL). Dodane krzyżowe linki: login → „Zarejestruj się", register → „Zaloguj się".

**B. Fix OTel logs (regresja z obs-hardeningu).** Serwisy (route-calc/.NET) mostkują logi przez OTLP → collector :4317 → Loki. W obs-hardeningu wyciąłem pipeline `logs` z collectora (`logs: null`) → collector odpowiadał `UNIMPLEMENTED`, route-calc spamował błędami przy szukaniu tras. To było over-correction (spam „negative structured metadata" pochodził od przestarzałego eksportera `loki`, nie od pipeline'u). Fix: przywrócony pipeline `logs` z eksporterem `otlphttp/loki` → natywny endpoint OTLP Loki (`/otlp/v1/logs`), poprawnie obsługuje structured metadata, z korelacją trace↔log. Zweryfikowane: 0 błędów UNIMPLEMENTED po fixie. (Trade-off: app-logi lecą i przez OTLP, i przez promtail — drobna duplikacja; do ewentualnego zawężenia promtaila później.)

## Iteracja — read-model GET (Ride/RideRequest) + wpięcie frontu

**Problem.** RideRequest znikał po refreshu — brak GET, stan tylko w SSE.

**Backend (trip-planner).** Dodane 4 endpointy GET (200, projekcja na DTO):
`/passenger/requests`, `/passenger/rides`, `/driver/requests` (JOIN routes po driver_id), `/driver/rides`.
+ metody query w `IRide(Request)Repository` i implementacje Postgres. Testy integracyjne (Testcontainers) — 26/26 PASS. Zweryfikowane przez gateway end-to-end (wszystkie 4 → 200).

**Frontend.** `useMyTrips`: na wejściu fetch `/passenger/rides` + `/passenger/requests`, hydruje `ridesStore` (status backendu → store), zwraca prośby; odświeża na SSE accept/reject/end. Strona `/trips/my-rides`: sekcja „Moje prośby" (Pending/Accepted/Rejected) + przejazdy ze źródła prawdy → **widać po refreshu**.

**🟡 Flagi infra (znalezione przy deployu):**
- `make ci-trip-planner` zepsute — generyczny `build-%` daje kontekst `src/services/<svc>`, a Dockerfile trip-plannera wymaga `src/` (cross-service `schemas/`). Build pada.
- `minikube image load` nie nadpisuje istniejącego `:latest` (+ `imagePullPolicy: Never`) → pody trzymają stary obraz. Workaround: `docker save | (eval $(minikube docker-env); docker load)` + `rollout restart`.

## Grafana — Faza 1 (dashboardy na realnych metrykach)

**Reality-check Prometheusa:** metryki biznesowe ISTNIEJĄ (push przez OTel→remote-write), ale pod innymi nazwami niż docs/06-observability (np. `ride_active_rides` nie `ride_active`, `matching_result_results_total` nie `matching_result_total`, `ride_driver_decline_declines_total`). Faza 3 (instrumentacja) okazała się prawie zbędna.

**Zrobione:**
- **Ride Funnel** (nowy, 3. dashboard) — wygenerowany skryptem, 10 paneli na realnych metrykach (matching, `ride_transitions_total{from_state,to_state}`, `ride_active_rides`, route_calc, decline/cancel) + logi Loki. Wszystkie 12 queries zwalidowane w Prometheusie (`status=success`). Załadowany przez sidecar.
- **Request Flow** — fix: `gateway_auth_failures_total` (nie istnieje) → `accounts_login_failed_attempts_total`.

**Zostaje:** Faza 2 — exportery redis/postgres/rabbitmq (DB/cache/queue panele w System Health). Infra: DB/cache/queue to ExternalName→`host.minikube.internal` (compose), więc exportery w k8s celują w host + ServiceMonitor (label `release: kube-prometheus-stack`).

## Grafana — naprawa istniejących dashboardów (tylko kod + logi, bez DB)

Decyzja użytkownika: olać exportery DB/cache/queue — tylko metryki z kodu (OTel) + logi.
- **System Health**: usunięty row „Databases" (redis/postgres — brak exporterów), „ComputeQueue Depth" (rabbitmq) → `notification_kafka_lag_messages` (Kafka lag, z kodu), dodany row „Aplikacja (kod)": wyjątki .NET (`aspnetcore_diagnostics_exceptions_total`), powiadomienia (`notifications_sent_total`), logowania (`accounts_login_{success,failed}_attempts_total`).
- **Request Flow**: auth panel `gateway_auth_failures_total` → `accounts_login_failed_attempts_total`.
- **Ride Funnel**: nowy (Faza 1).
Wszystkie queries zwalidowane w Prometheusie (`status=success`); zero zależności od exporterów infra. 3/3 dashboardy wczytane przez sidecar.

## accounts /me + security hardening (kaskada 4 latentnych bugów)

Cel: naprawić `/api/users/me` (404) + hardening accounts. Issue 1 (JWT zamiast X-User-Id w trip-plannerze) i issue 2 (role Driver/Passenger) — **olane** wg decyzji (trip-planner tylko przez gateway; role nie pasują do domeny dual-role).

Diagnoza wykazała 4 ukryte bugi (latentne — serwisy nie były restartowane po zmianach):
1. **accounts CrashLoop** — `db.Database.Migrate()` pada `42P07: relation "Users" already exists` (schemat jest, historia migracji pusta). Fix: `catch PostgresException(42P07)` → log + kontynuuj (restart-safe).
2. **brak `UseAuthentication()`/`UseAuthorization()`** w pipeline accounts — usługi JWT zarejestrowane (`DependencyInjection`), middleware niewpięty → `/me` z `RequireAuthorization` nie działał. Fix: 2 linie w `Program.cs`.
3. **gateway: przestarzały obraz** — `accounts-users-route` (`/api/users/{**catch-all}`) jest w `appsettings.json`, ale zapieczony obraz go nie miał → `/api/users/*` → 404. Fix: rebuild+redeploy gatewaya (bez zmiany source).
4. **accounts: walidacja JWT bez `KeyId`** — token ma `kid=pullapp-key` (JwtProvider), klucz walidacji nie miał KeyId → resolver po kid zawodził → 401. Fix: `securityKey.KeyId="pullapp-key"` + `MapInboundClaims=false` (mirror gatewaya).
5. **pole login `token` vs `accessToken`** — worktree accounts zwracał `Token`, front oczekuje `accessToken`. Fix: `LoginUserResponse.AccessToken`.

Wynik (zweryfikowane przez gateway): login → `{accessToken}`, `GET /api/users/me` z tokenem → 200 (pełny profil), bez tokena → 401. accounts 0 restartów. Zmiana kodu **tylko w accounts**; gateway = redeploy stale image.

## docs/ — przebudowa pod C4 + aktualizacja

Cel: cały `docs/` ładnie pod model C4 i AKTUALNY (odzwierciedla stan zbudowany w tej sesji). Plan zaakceptowany.

Nowa struktura (poziomy C4 zamiast poprzednich numerowanych folderów):
- `01-context/system-context.md` — L1: aktorzy (Passenger/Driver, dual-role), systemy zewnętrzne (OSM/OSRM, FCM, payment gw).
- `02-containers/` — `containers.md` (przepisany: +frontend, +gateway=YARP, protokół HTTP/REST a nie gRPC, statusy ✅/🟡/⬜), `diagram.md` (mermaid odświeżony), `deployment.md` (NOWY: minikube + compose ExternalName→host.minikube.internal, imagePullPolicy Never, KEDA, workaround `docker save|docker load`, trip-planner build context).
- `03-components/` — NOWE: `frontend.md`, `gateway.md`, `accounts.md`, `payments.md` (stub); `trip-planner.md` zaktualizowany (gRPC→REST, tabela endpointów + 4 read-model GET-y); przeniesione `route-calc/notifications/driver-tracker/chat/tile-server`.
- `04-flows/` — `ride-lifecycle.md` (konsolidacja flows 0–8 z trip-planner-redo + statusy implementacji/fakes/deferred), `auth-and-profile.md`, `notifications-sse.md`.
- `05-observability/` — konsolidacja 4→3: `observability.md` (+nota o logs pipeline otlphttp/loki), `metrics.md` (przepisany na REALNE nazwy z kodu + transformacja OTel→Prometheus, zweryfikowane), `dashboards.md` (3 wdrożone dashboardy).
- `reference/` — `trip-planner-spec.md`, `gitflow.md`, `sprint-4.md`.

Usunięte (stale/redundant): `01-containers/`, `02-bounded-contexts/` (puste .gitkeep stuby + redundant), `03-flows/` (kryptyczne 02-1..4), `04-components/` (stuby), `06-observability/` (metrics/monitoring/grafana-dashboards = stare design/plan docs), `08-trip-planner-done-right/` (wchłonięte do ride-lifecycle).

Realne nazwy metryk potwierdzone z kodu + deployed dashboardów: gateway MA custom metryki (`services/gateway/GatewayMetrics.cs`, nie w `/src`), unit-suffix w Prometheusie (`ride_active`→`ride_active_rides`, `ride_cancelled_total`→`ride_cancelled_rides_total`, `matching_result_total`→`matching_result_results_total`, `accounts.login.*`→`accounts_login_*_attempts_total`).

## Makefile — naprawa 2 infra-bugów + make frontend + przegląd

Dwa „infra bugi" z wcześniejszych iteracji to były błędy w samym Makefile:
1. **Kontekst budowania trip-plannera** — `build`/`ci` budowały każdy serwis z `src/services/<svc>`, ale Dockerfile trip-plannera COPY-uje ścieżki cross-service (`services/trip-planner/...` + `schemas/...`), więc wymaga kontekstu `src/`. Generyczny target padał. Fix: `svc_ctx = $(if $(filter trip-planner,...),src,src/services/$(1))`, użyte w `build`, `build-%`, `_images`, `ci-%`.
2. **Nienadpisywany obraz `:latest`** — `minikube image load` nie podmienia istniejącego taga `:latest`, a przy `imagePullPolicy: Never` pody trzymały stary obraz (stąd całosesyjny workaround `docker save|docker load`). Fix: budujemy wprost do dockera minikube (`eval $(minikube docker-env)` → `docker build`), bez `image load`. Nowy target `_images`; `ci`/`ci-%` z niego korzystają; martwy `_load-%` usunięty.

**make run** — przyczyna długiego czekania na rollout: kolejność była `... cd ci`, czyli `cd` (apply + czekanie rollout 120s/serwis) szło PRZED zbudowaniem obrazów (`ci`). Na świeżym klastrze pody = ErrImageNeverPull, a `cd` przepalał ~12 min czekania zanim cokolwiek się zbudowało. Fix: `run: _cluster-ensure obs-install keda-install infra _images _tag-images cd` — build najpierw, deploy raz. Dodany brakujący `keda-install` (route-calc ScaledObject). Nie usuwam `run` — był tylko źle ułożony.

**make frontend** (NOWE) — `frontend` (compose up --build → :5000, wymaga `make pf-gateway`), `frontend-dev` (pnpm dev), `frontend-down`, `frontend-logs`. Help zaktualizowany. Wszystko zweryfikowane `make -n`.
