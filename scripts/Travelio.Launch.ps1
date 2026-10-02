[CmdletBinding()]
param(
    [ValidateSet('Web','Api','Desktop','Android','iOS','MacCatalyst')]
    [string]$Profile,
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $root 'src/Travelio.Api/Travelio.Api.csproj'
$mauiProject = Join-Path $root 'src/Travelio.Maui/Travelio.Maui.csproj'
if ([string]::IsNullOrWhiteSpace($Profile)) {
    Write-Host 'Travelio: 1 Web, 2 API, 3 Windows, 4 Android, 5 iOS, 6 Mac Catalyst' -ForegroundColor Cyan
    $selection = Read-Host 'Wybór [1-6]'
    if ($selection -notmatch '^[1-6]$') { throw 'Wybierz liczbę od 1 do 6.' }
    $Profile = @('Web','Api','Desktop','Android','iOS','MacCatalyst')[[int]$selection - 1]
}
if ($Profile -in @('iOS','MacCatalyst') -and $env:OS -eq 'Windows_NT') {
    throw 'iOS i Mac Catalyst wymagają komputera Mac z Xcode. Otwórz Travelio.Native.sln na Macu lub sparuj Visual Studio z Makiem.'
}
$url = 'http://localhost:5180'
$api = $null
try {
    $running = $false
    try {
        $health = Invoke-RestMethod "$url/api/status" -TimeoutSec 2
        $running = $health.application -eq 'Travelio'
    } catch { }
    if (-not $running) {
        & dotnet build $apiProject --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Kompilacja serwera nie powiodła się. Szczegóły są powyżej.' }
        $logDirectory = Join-Path $root 'artifacts/launch'
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $hostName = if ($env:OS -eq 'Windows_NT') { 'Travelio.Api.exe' } else { 'Travelio.Api' }
        $hostPath = Join-Path $root "src/Travelio.Api/bin/Debug/net8.0/$hostName"
        $arguments = @{
            FilePath = $hostPath
            WorkingDirectory = (Split-Path -Parent $apiProject)
            ArgumentList = @('--urls', $url, '--environment', 'Development')
            PassThru = $true
            RedirectStandardOutput = (Join-Path $logDirectory 'api.log')
            RedirectStandardError = (Join-Path $logDirectory 'api-error.log')
        }
        if ($env:OS -eq 'Windows_NT') { $arguments.WindowStyle = 'Hidden' }
        $api = Start-Process @arguments
        $deadline = (Get-Date).AddSeconds(60)
        do {
            if ($api.HasExited) { throw "Serwer zakończył pracę. Sprawdź $logDirectory/api-error.log" }
            try { $health = Invoke-RestMethod "$url/api/status" -TimeoutSec 2; $running = $health.application -eq 'Travelio' } catch { }
            if (-not $running) { Start-Sleep -Milliseconds 500 }
        } until ($running -or (Get-Date) -ge $deadline)
        if (-not $running) { throw "Serwer nie odpowiedział. Szczegóły: $logDirectory" }
    }
    Write-Host "Travelio działa: $url. Zatrzymanie: Ctrl+C." -ForegroundColor Green
    switch ($Profile) {
        'Web' {
            if (-not $NoBrowser) { Start-Process $url }
            if ($api) { while (-not $api.HasExited) { Start-Sleep -Seconds 1 } }
        }
        'Api' { if ($api) { while (-not $api.HasExited) { Start-Sleep -Seconds 1 } } }
        default {
            $framework = switch ($Profile) {
                'Desktop' { 'net8.0-windows10.0.19041.0' }
                'Android' { 'net8.0-android' }
                'iOS' { 'net8.0-ios' }
                'MacCatalyst' { 'net8.0-maccatalyst' }
            }
            & dotnet build $mauiProject -t:Run "-p:TravelioTargetFramework=$framework" "-f" $framework
            if ($LASTEXITCODE -ne 0) { throw 'Uruchomienie aplikacji natywnej nie powiodło się. Sprawdź SDK oraz emulator/urządzenie.' }
            if ($api) { Read-Host 'Enter zatrzyma serwer API po zakończeniu pracy z aplikacją' | Out-Null }
        }
    }
}
finally {
    # Stop only the exact host started here. An existing server belongs to its original launcher.
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -ErrorAction SilentlyContinue }
}
