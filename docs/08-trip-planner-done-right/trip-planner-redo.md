# System Carpooling — szczegóły techniczne

surowe notatki z excalidraw, bez obrabiania

---

## Modele / Agregaty

### Route

- **Driver** — kto prowadzi
- **status** — stan trasy
- **start** — punkt startowy
- **end** — punkt końcowy
- **current_location** — aktualna lokalizacja kierowcy
- **capacity** — ile miejsc
- **List\<Ride\>** — lista aktywnych przejazdów na tej trasie

### Ride

- **Driver**
- **Passenger**
- **start_point** — skąd pasażer wsiada
- **end_point** — gdzie pasażer wysiada
- **price** — cena przejazdu
- **cancellation_price** — kara za anulowanie
- **timeout** — deadline na spotkanie
- **status** — stan przejazdu

### RideRequest

- **Route** — do której trasy jest prośba
- **start_point**
- **end_point**

---

## Eventy (zidentyfikowane w excalidraw)

- RideRequested

_(lista niekompletna w excalidraw, pozostałe wynikają z flowów poniżej)_

---

## Flows

### flow 0 — kierowca tworzy Route (sync)

1. kierowca tworzy trasę synchronicznie
2. obliczana jest trasa, `status = Created`
3. zwracamy trasę

---

### flow 1 — kierowca aktywuje Route

1. walidacja: `current_location == start`
2. zmiana `status = Active`

---

### flow 1.5 — kierowca usuwa Route

trzy przypadki:

**a) `status == Active` i istnieją Ride**
- nie można usunąć, twarde zablokowanie

**b) `status == Created` i istnieją Ride**
- Route jest usuwany
- system generuje `RouteDeleted`
- idzie powiadomienie do pasażera (do tych z Ride)

**c) nie ma żadnych Ride**
- można usunąć po prostu, bez ceremonii

---

### flow 2 — pasażer szuka tras

1. pasażer wpisuje `start`, `end`
2. system zwraca listę top-N tras, które spełniają:
   - `status != full`
   - posortowane zgodnie z **metryką zgodności z trasą** — bierze pod uwagę `start_point`, `end_point` pasażera oraz `current_location` kierowcy
   - pasażer dostaje trasy najlepiej dopasowane do jego potrzeb
   - szczegół implementacyjny — metryka do zdefiniowania przez dev

---

### flow 3 — pasażer wybiera trasę (tworzy RideRequest)

1. sprawdzenie czy trasa nie jest pełna — jeśli tak, odrzuć
2. system wysyła prośbę do Drivera o dodanie pasażera do trasy
3. system generuje event `RideRequested` → do drivera wysyłane jest powiadomienie (**SSE, Push**)
4. na koncie pasażera następuje **zamrożenie obliczonego `price`**

---

### flow 4 — kierowca odrzuca pasażera

1. **odmrożenie środków** pasażera
2. system generuje event `RideRejected` → do pasażera idzie powiadomienie (**SSE, Push**)

---

### flow 5 — kierowca akceptuje

wszystko w **transakcji atomowej:**

1. tworzony jest `Ride` z `status`:
   - `WaitingForStart` — jeśli trasa jest już aktywna
   - `WaitingForActivation` — w przeciwnym razie
2. jeżeli po stworzeniu brakuje miejsc → trasa przechodzi w stan `full` (nie wyświetla się nowym pasażerom)
3. tworzony jest **chat room** (wywołanie do serwisu chat) — pasażer i kierowca dostają dostęp do wspólnego kanału komunikacji

po transakcji:

4. jeżeli transakcja się nie powiedzie → automatycznie odrzuć pasażera (jak flow 4)
5. jeżeli `status = full` → odrzuć **wszystkich** pozostałych oczekujących pasażerów z tej trasy
6. system generuje event `RideAccepted` → do pasażera idzie powiadomienie (**SSE, Push**)

---

### flow 6 (opcjonalne) — pasażer odrzucony, stan RideRequest

- **nie usuwamy RideRequest** od razu
- RideRequest ma **TTL 24h**
- po upłynięciu TTL lub po zakończeniu Ride pasażer może dostać powiadomienie że miejsce się zwolniło (patrz flow 8)

---

### flow 6.5 — upłynął timeout Ride

dwa podprzypadki:

**a) kierowca nie dotarł** (`lokalizacja kierowcy != lokalizacja meeta`)
- **nie jest pobierana opłata**
- Ride jest usuwany

**b) pasażer nie stawił się**
- pobierana jest `cancellation_price`
- Ride jest usuwany

---

### flow 7 — start Ride

1. kierowca deklaruje odebranie pasażera
2. pasażer deklaruje odebranie przez kierowcę
   - jeśli Ride start **nie został wcześniej zadeklarowany przez kierowcę** → nic się nie dzieje (deklaracja pasażera bez deklaracji kierowcy jest ignorowana)
3. dopiero po obu deklaracjach: `Ride.status = Started`, **olewamy timeout** (timeout przestaje obowiązywać)

---

### flow 8 — koniec / anulowanie przez pasażera Ride

trzy podprzypadki zależnie od stanu:

**a) `status = WaitingForActivation`**
- nie pobieramy opłaty
- Ride jest usuwany

**b) `status = WaitingForDriver`** _(w excalidraw: WaitingForStart)_
- pobieramy `cancellation_price`
- Ride jest usuwany

**c) `status = Started`**
1. pasażer deklaruje koniec przejazdu
2. kierowca deklaruje koniec przejazdu — jeśli pasażer jeszcze nie zadeklarował, to nic się nie dzieje (symetryczna logika jak przy starcie)

**wspólne dla wszystkich podprzypadków:**
1. Ride jest usuwany
2. idzie event `RideEnded` → **idzie powiadomienie do pasażerów z RideRequests odrzuconymi** na tę trasę (żeby wiedzieli że może znowu jest miejsce albo trasa się skończyła)

---

## Statusy

### Route.status

- `Created` — trasa stworzona, kierowca jeszcze nie ruszył
- `Active` — kierowca ruszył / jest w miejscu startowym
- `full` — brak miejsc, niewidoczna dla nowych pasażerów

### Ride.status

- `WaitingForActivation` — trasa jeszcze nie aktywna, pasażer zaakceptowany ale czeka aż kierowca ruszy
- `WaitingForStart` — trasa aktywna, czekamy na fizyczne spotkanie (tu działa timeout)
- `Started` — obaj potwierdzili spotkanie, przejazd w toku

### RideRequest.status

- aktywny / odrzucony
- TTL 24h po odrzuceniu (nie usuwamy od razu)

---

## Mechanizmy płatności / środków

- w momencie flow 3 (wybranie trasy) → **zamrożenie price** na koncie pasażera
- w flow 4 (odrzucenie) → **odmrożenie**
- w flow 5 (akceptacja) → środki nadal zamrożone
- w flow 6.5b (timeout wina pasażera) → pobieramy `cancellation_price`, reszta odmrożona (?)
- w flow 8b (anulowanie WaitingForStart) → pobieramy `cancellation_price`
- w flow 8a (anulowanie WaitingForActivation) → nic nie pobieramy, odmrażamy
- po zakończeniu normalnego przejazdu (flow 8c happy path) → pobieramy `price`

---

## Powiadomienia (transport)

- **SSE** (Server-Sent Events) — dla aktywnych sesji webowych
- **Push** — dla mobilnych / nieaktywnych sesji

zdarzenia które triggerują powiadomienia:
- `RideRequested` → do kierowcy
- `RideRejected` → do pasażera
- `RideAccepted` → do pasażera
- `RouteDeleted` → do pasażerów z Ride na tej trasie
- `RideEnded` → do pasażerów z odrzuconymi RideRequests

---

## Uwagi / rzeczy do doprecyzowania

- metryka zgodności trasy (flow 2) — obliczana przez route-calc service (istniejąca infrastruktura RabbitMQ)
- flow 6.5 (timeout Ride) — **pominięte w pierwszej implementacji**, zbyt skomplikowane
- flow 6 (TTL RideRequest 24h) — **pominięte**, oznaczone jako "opcjonalne"
- `WaitingForDriver` — ujednolicona nazwa stanu (zamiast `WaitingForStart`)
- flow 6.5b (timeout wina pasażera) — pobieramy tylko `cancellation_price`, reszta odmrożona
- powiadomienie po RideEnded do odrzuconych pasażerów — "trasa/przejazd się zakończył, może być miejsce"
- chat room tworzony synchronicznie przy flow 5 przez trip-planner → **chat service** (Go); chat service nie jest jeszcze zaimplementowany, trip-planner używa fake/stub
