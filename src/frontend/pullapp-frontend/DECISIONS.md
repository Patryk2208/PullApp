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
