# Profile uruchomieniowe Travelio

Najprostszy sposób to dwukrotnie kliknąć `Start-Travelio.cmd` albo uruchomić w katalogu głównym:

```powershell
.scriptsTravelio.Launch.ps1
```

Skrypt pokaże menu. Uruchamia API na `http://localhost:5180`, czeka na `/health`, a następnie startuje wybraną aplikację. API jest zamykane razem z aplikacją. Można też wybrać profil bez menu:

```powershell
.scriptsTravelio.Launch.ps1 -Profile Web
.scriptsTravelio.Launch.ps1 -Profile Desktop
.scriptsTravelio.Launch.ps1 -Profile Android
.scriptsTravelio.Launch.ps1 -Profile iOS
.scriptsTravelio.Launch.ps1 -Profile MacCatalyst
.scriptsTravelio.Launch.ps1 -Profile Api
```

W Visual Studio profile są dostępne na liście uruchamiania w odpowiednim projekcie:

- `Travelio.Web`: `Travelio Web (HTTP)` lub `Travelio Web (HTTPS)`;
- `Travelio.Api`: `Travelio API (HTTP)` lub `Travelio API (HTTPS)`;
- `Travelio.Maui`: `Travelio Desktop (Windows)`, `Travelio Android (emulator)`, `Travelio iOS (simulator)` albo `Travelio Mac Catalyst`.

Dla Web i MAUI aplikacja potrzebuje API. Menu uruchamia je automatycznie. Profil Web łączy się z `http://localhost:5180/` zapisanym w `src/Travelio.Web/wwwroot/appsettings.json`; w Visual Studio uruchom najpierw profil `Travelio API (HTTP)`, a potem wybrany profil klienta. Android Emulator korzysta z `10.0.2.2:5180`; Windows, iOS Simulator i Mac Catalyst korzystają z `localhost:5180`.

Android, iOS i Mac Catalyst wymagają właściwego emulatora/symulatora oraz SDK platformy. Profil nie instaluje emulatora i nie zmienia konfiguracji podpisywania aplikacji.
