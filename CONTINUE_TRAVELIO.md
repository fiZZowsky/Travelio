# Travelio — punkt wznowienia pracy

Ten plik jest instrukcją dla kolejnej sesji. Po resecie limitu użytkownik może napisać: **„Kontynuuj pracę nad Travelio od ostatniego momentu”**. Najpierw przeczytaj ten plik, potem sprawdź aktualny stan repozytorium i wykonuj zadania w podanej kolejności.

## Stan wykonany

- Utworzono rozwiązania `Travelio.sln` oraz `Travelio.Native.sln`.
- Ustawiono .NET 8 SDK w `global.json` (`8.0.307`), nullable reference types, implicit usings i reguły analizy.
- Utworzono warstwy:
  - `Travelio.Domain` — encje, value records, walidacja domenowa, budżet i plan podróży.
  - `Travelio.Application` — interfejsy serwisów, rekomendacje, generator planu, przygotowania, alerty i przypomnienia.
  - `Travelio.Infrastructure` — EF Core SQLite, Identity, migracja `InitialCreate`, adapter GDACS.
  - `Travelio.Api` — konta, logowanie, CSRF, podróże, współdzielenie, paszport, alerty, health check, rate limiting.
  - `Travelio.UI` — wspólne komponenty Blazor, offline-first workspace, PWA UI, globus, budżet, plan dnia i przygotowania.
  - `Travelio.Web` — Blazor WebAssembly/PWA host.
  - `Travelio.Maui` — host Blazor Hybrid dla Android, iOS, macOS Catalyst i Windows.
- Zaimplementowano pulpit, odkrywanie kierunków, ankietę rekomendacji, tworzenie podróży, automatyczny plan zwiedzania, schemat trasy, budżet i wydatki, listę pakowania, ostrzeżenia, współdzielenie, notatki, eksport JSON/CSV, globus odwiedzonych krajów oraz tryb offline.
- Dodano przykładowe kierunki: Lizbona, Rzym, Bali, Kioto, Barcelona i Reykjavík.
- Dodano zasoby zdjęć, granice Natural Earth, D3, font Manrope, manifest PWA i service worker produkcyjny.
- Dodano lokalne powiadomienia przeglądarkowe, desktopowe, Androidowe i Apple.
- Dodano testy domenowe, planera, budżetu, stref czasowych, alertów, API, uprawnień, konfliktów synchronizacji i offline storage.
- Dodano rzeczywiste źródła danych: Open-Meteo/GeoNames (miasta), Photon/OpenStreetMap i Overpass (miejsca oraz noclegi), Valhalla (trasy piesze), NBP (kursy walut) oraz linki do Booking, Map Google i stron linii lotniczych.
- Dodano profile uruchomieniowe `Travelio Web`, `Travelio Api`, `Travelio Desktop`, `Travelio Android`, `Travelio iOS` i `Travelio Mac Catalyst`, menu `scripts/Travelio.Launch.ps1` oraz instrukcję `LAUNCH_PROFILES.md`.

## Ostatnia weryfikacja

- `dotnet test tests/Travelio.Tests --no-restore --filter "FullyQualifiedName!~ApiTests"` — **21 testów przeszło**.
- `dotnet build Travelio.sln --no-restore` — **przeszło bez błędów i ostrzeżeń**.
- `dotnet build src/Travelio.Maui/Travelio.Maui.csproj -p:TravelioTargetFramework=net8.0-android --no-restore` — ostatnio zakończone powodzeniem; wygenerowano APK w `src/Travelio.Maui/bin/Debug/net8.0-android/`.
- Pełny zestaw API wymaga Windows DPAPI; w tej sesji automatyczny reviewer nie dopuścił ponownego uruchomienia z eskalacją po wyczerpaniu limitu. Testy domenowe, live-data, UI storage i planner przeszły.
- Wygenerowano migrację EF Core `src/Travelio.Infrastructure/Migrations/20260930132051_InitialCreate.cs`.
- Lokalny API był uruchamiany pod `http://localhost:5180` i pulpit był sprawdzany w przeglądarce.

## Znane problemy do naprawy jako pierwsze

1. W przeglądarce wystąpił błąd JS interop przy odczycie nieistniejącej wartości `travelio.reminders.enabled`: `null` nie może zostać zdeserializowane do `bool`. Naprawić przez odczyt `bool?` w `BrowserLocalStore`/`ReminderCoordinator` albo zwracanie `false` z JS dla brakującego klucza. Po poprawce wyczyścić IndexedDB lub obsłużyć stare dane migracyjnie i ponownie sprawdzić konsolę.
2. Dokończyć ręczny test UI ankiety: przejść wszystkie 3 kroki, uzyskać wyniki, utworzyć podróż, odświeżyć stronę, wejść w szczegóły, dodać wydatek i sprawdzić zapis.
3. Uruchomić ponownie testy po poprawce przypomnień i sprawdzić, czy `blazor-error-ui` nie pojawia się w przeglądarce.
4. Sprawdzić kompilację MAUI dla iOS, Mac Catalyst i Windows na maszynach z odpowiednimi workloadami/SDK. Windows wymaga lokalnego Windows SDK; Android został zbudowany.

## Braki funkcjonalne przed wersją produkcyjną

### Integracje zewnętrzne

- Zastąpić `DemoTravelOfferProvider` prawdziwymi dostawcami lotów i noclegów, z kluczami API w secret managerze, timeoutami, retry, circuit breakerem, cache i limitami dostawców.
- Zastąpić przykładowe opinie, ceny i godziny otwarcia aktualnymi źródłami oraz pokazać datę aktualizacji, walutę, podatki, bagaż, zasady anulowania i link afiliacyjny.
- Dodać geokodowanie, prawdziwe mapy i routing pieszy/transportowy; obecna trasa jest schematem na podstawie odległości w linii prostej.
- Dodać źródła lotów, noclegów, atrakcji, pogody, walut i świąt lokalnych.
- Dokończyć produkcyjny adapter alertów: GDACS działa jako odczyt RSS, ale trzeba dodać monitoring awarii, cache serwerowy, MSZ oraz mapowanie granic/regionów.

### Konta i backend

- Zdefiniować produkcyjną bazę (PostgreSQL/Azure SQL), backupy, retencję, migracje uruchamiane w pipeline i monitoring.
- Dodać potwierdzenie e-mail, reset hasła, 2FA/passkeys, limity prób, blokady i panel zarządzania kontem.
- Skonfigurować sekrety, HTTPS, CORS dla właściwych domen, cookies `Secure`, HSTS, CSP nonce/hash, politykę prywatności i zgodę na cookies.
- Dodać audyt zmian, wersjonowanie encji i pełną strategię konfliktów dla podróży, paszportu i uczestników.
- Dodać paginację, wyszukiwanie serwerowe i limity zapytań dla dużych kont.
- Dodać testy migracji, testy bezpieczeństwa, testy rate limitingu i testy wielokrotnego uruchomienia API.

### Offline i synchronizacja

- Dodać wersję schematu lokalnego, migracje IndexedDB/pliku MAUI i bezpieczny import poprzednich danych.
- Dodać kolejkę operacji zamiast zapisywania całych snapshotów, idempotency keys i synchronizację przy powrocie aplikacji z tła.
- Pokazać użytkownikowi stan każdej operacji, czas ostatniej synchronizacji i możliwość ponowienia.
- Zweryfikować limit pamięci IndexedDB, zachowanie w trybie prywatnym i odzyskiwanie po przerwanym zapisie.
- Dodać szyfrowanie lokalnych danych w MAUI oraz rozważyć ochronę wrażliwych danych webowych.

### Mobilne powiadomienia i system

- Zweryfikować Android 13+ permission, kanały, reboot receiver, strefy czasowe, Doze i anulowanie alarmów.
- Dodać `UNUserNotificationCenter` testy na prawdziwym urządzeniu i obsłużyć zmianę strefy czasowej.
- Dodać deep link z powiadomienia do konkretnego dnia/punktu podróży.
- Dodać natywne ikony i finalne assety App Store/Google Play; uzupełnić PrivacyInfo i deklaracje używanych danych.

### Jakość produktu

- Dodać pełną lokalizację zasobów (`pl`, `en`) zamiast tekstów wpisanych bezpośrednio w komponentach.
- Dodać WCAG: kontrast, focus trap testy, czytniki ekranu, klawiatura, reduced motion, komunikaty błędów i walidację formularzy.
- Dodać loading/error/empty state dla każdej integracji oraz retry bez utraty danych.
- Dodać testy responsywności na telefonie, tablecie, desktopie i orientacji poziomej.
- Zoptymalizować obrazy (WebP/AVIF, responsive sizes), lazy loading, cache headers i rozmiar WASM.
- Dodać telemetrykę wydajności bez wysyłania danych wrażliwych, logowanie strukturalne, tracing i alerty.
- Dodać onboarding, profil preferencji, edycję uczestników, zaproszenia linkiem/e-mailem i powiadomienia o zmianach współdzielonej podróży.
- Dodać prawdziwą analizę wydatków: trendy między podróżami, budżet dzienny, waluty, eksport zgodny z RODO i usuwanie danych.
- Dodać checklistę wizową i zdrowotną zależną od obywatelstwa, aktualnej trasy i daty; wszystkie porady powinny pokazywać źródło i datę.

## Dokumentacja i uruchomienie do dodania

- Utworzyć `README.md` z wymaganiami, uruchomieniem web/API, konfiguracją bazy, uruchomieniem Androida, iOS i Windows oraz publikacją PWA.
- Dodać `.env.example`/sekrety opisane bez wartości prywatnych.
- Dodać CI: restore, build, test, lint/analyzers, migracje, publish web i artefakty APK.
- Dodać Dockerfile/docker-compose dla API i bazy oraz instrukcję wdrożenia.
- Dodać wersjonowanie API i OpenAPI/Swagger dla środowiska developerskiego.
- Dodać seed danych demonstracyjnych jako jawny tryb demo, z możliwością całkowitego wyłączenia w produkcji.

## Zalecana kolejność następnej sesji

1. Naprawić `null` w ustawieniach przypomnień i przejść test UI w przeglądarce.
2. Uruchomić `dotnet test Travelio.sln` oraz build web/API/Android.
3. Dodać `README.md` i instrukcję konfiguracji.
4. Dokończyć obserwowalność, konfigurację produkcyjną i testy bezpieczeństwa.
5. Zaimplementować pierwszą prawdziwą integrację ofert oraz routing/mapy za interfejsami istniejącymi w `Travelio.Application`.
6. Zweryfikować MAUI na urządzeniu Android i simulatorze/urządzeniu Apple.
7. Przygotować pipeline CI/CD, staging, backupy, politykę prywatności i dopiero potem publikację.

## Ważne ograniczenia

- Projekt jest prototypem/MVP z katalogiem demonstracyjnym. Ceny, opinie, loty i noclegi nie są ofertą handlową.
- Alert „brak zdarzeń” nie oznacza bezpieczeństwa. Użytkownik musi sprawdzić źródła urzędowe.
- .NET 8 pozostaje wymaganym targetem użytkownika, ale przed publikacją trzeba zaplanować upgrade MAUI/.NET zgodnie z aktualną polityką wsparcia.
- Nie uruchamiać produkcji na SQLite z domyślnymi ustawieniami ani z przykładowym `ApiBaseUrl`.

## Komenda wznowienia

Po ponownym wejściu do projektu napisz: **„Kontynuuj pracę nad Travelio od ostatniego momentu. Przeczytaj `CONTINUE_TRAVELIO.md`, zacznij od sekcji ‘Znane problemy do naprawy jako pierwsze’, sprawdź aktualny stan plików i testów, a potem wykonuj kolejne punkty aż do zakończenia.”**
