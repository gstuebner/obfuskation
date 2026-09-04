@echo off
rem
rem build-release.cmd - baut die Release-Fassung dieses Projekts.
rem
rem Gegenstueck: build-release.sh (gleiche Optionen, gleiches Ergebnis).
rem Aenderungen hier bitte dort nachziehen.

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"

rem Ohne --rid werden beide Zielsysteme gebaut: das Werkzeug laeuft auf Linux
rem und auf Windows, und beide Fassungen sollen aus einem Aufruf entstehen.
set "RIDS="
set "SELF_CONTAINED=false"
set "SINGLE_FILE=true"
set "OUTPUT_ROOT="
set "BUILD_CLI=true"
set "BUILD_GUI=true"

set "CLI_PROJECT=src\Obfuskation.Cli\Obfuskation.Cli.csproj"
set "GUI_PROJECT=src\Obfuskation.Gui\Obfuskation.Gui.csproj"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--rid" (
    if "%~2"=="" (echo Fehler: --rid braucht einen Wert.& exit /b 1)
    set "RIDS=!RIDS! %~2" & shift & shift & goto parse
)
if /i "%~1"=="--self-contained" (set "SELF_CONTAINED=true" & shift & goto parse)
if /i "%~1"=="--no-single-file" (set "SINGLE_FILE=false" & shift & goto parse)
if /i "%~1"=="--cli-only" (set "BUILD_GUI=false" & shift & goto parse)
if /i "%~1"=="--gui-only" (set "BUILD_CLI=false" & shift & goto parse)
if /i "%~1"=="--output" (
    if "%~2"=="" (echo Fehler: --output braucht einen Wert.& exit /b 1)
    set "OUTPUT_ROOT=%~2" & shift & shift & goto parse
)
if /i "%~1"=="-h"     goto usage
if /i "%~1"=="--help" goto usage
if /i "%~1"=="/?"     goto usage
echo Unbekannte Option: %~1
echo Hilfe: %~nx0 --help
exit /b 1

:usage
echo Verwendung: %~nx0 [Optionen]
echo.
echo Baut die Release-Fassung und legt sie unter publish\^<RID^>\ ab.
echo Ohne --rid werden win-x64 und linux-x64 gebaut, je mit Kommandozeilen-
echo programm und Oberflaeche.
echo.
echo Optionen:
echo   --rid ^<kennung^>      Nur diese Ziellaufzeit bauen; mehrfach angebbar
echo                        z. B. win-x64, win-arm64, linux-x64, linux-arm64
echo   --self-contained     Runtime mitliefern; laeuft ohne installiertes .NET,
echo                        das Ergebnis wird dadurch deutlich groesser
echo   --no-single-file     Nicht zu einer einzelnen Programmdatei zusammenfassen
echo   --cli-only           Nur das Kommandozeilenprogramm
echo   --gui-only           Nur die Oberflaeche
echo   --output ^<pfad^>      Abweichendes Wurzelverzeichnis; darunter entsteht ^<RID^>\
echo   -h, --help           Diese Hilfe
echo.
echo Beispiele:
echo   %~nx0
echo   %~nx0 --rid win-x64
echo   %~nx0 --rid win-x64 --self-contained
echo   %~nx0 --cli-only --rid win-x64
exit /b 0

:parsed
if not defined RIDS set "RIDS=win-x64 linux-x64"

if "%BUILD_CLI%"=="false" if "%BUILD_GUI%"=="false" (
    echo Fehler: --cli-only und --gui-only schliessen einander aus.
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo Fehler: dotnet ist nicht installiert oder nicht im PATH.
    exit /b 1
)

cd /d "%SCRIPT_DIR%"
if not defined OUTPUT_ROOT set "OUTPUT_ROOT=publish"

rem Die Wertform "--self-contained false" schaltet unter SDK 10 nicht ab: das
rem Ergebnis enthaelt dann trotzdem die vollstaendige Runtime (67 MB statt
rem 0,8 MB). Nur die Schalterform wirkt zuverlaessig.
set "SC_SCHALTER=--no-self-contained"
if "%SELF_CONTAINED%"=="true" set "SC_SCHALTER=--self-contained"

rem Die Projekte werden einzeln veroeffentlicht. Die Solution zu nehmen wuerde
rem auch die Bibliothek und das Testprojekt mitschleifen.
set "PROJECTS="
if "%BUILD_CLI%"=="true" (
    if not exist "%SCRIPT_DIR%%CLI_PROJECT%" (
        echo Fehler: %CLI_PROJECT% nicht gefunden.
        exit /b 1
    )
    set "PROJECTS=!PROJECTS! %CLI_PROJECT%"
)
if "%BUILD_GUI%"=="true" (
    if exist "%SCRIPT_DIR%%GUI_PROJECT%" (
        set "PROJECTS=!PROJECTS! %GUI_PROJECT%"
    ) else (
        echo Hinweis: %GUI_PROJECT% gibt es nicht, die Oberflaeche wird uebersprungen.
    )
)

for %%R in (%RIDS%) do (
    set "ZIEL=%OUTPUT_ROOT%\%%R"
    echo === %%R ===============================================
    echo Self-contained: %SELF_CONTAINED%   Einzeldatei: %SINGLE_FILE%
    echo Ausgabe       : !ZIEL!

    rem Ohne IncludeNativeLibrariesForSelfExtract blieben die nativen
    rem Bibliotheken der Oberflaeche (Skia, HarfBuzz) als eigene Dateien
    rem liegen - dann waere "eine Programmdatei" nur die halbe Wahrheit.
    for %%P in (!PROJECTS!) do (
        echo   %%P
        dotnet publish "%%P" ^
            --configuration Release ^
            --runtime %%R ^
            %SC_SCHALTER% ^
            -p:PublishSingleFile=%SINGLE_FILE% ^
            -p:IncludeNativeLibrariesForSelfExtract=%SINGLE_FILE% ^
            -p:DebugType=none ^
            --output "!ZIEL!" ^
            --nologo ^
            --verbosity quiet
        if errorlevel 1 (
            echo.
            echo Build fehlgeschlagen: %%P fuer %%R
            exit /b 1
        )
    )
    echo.
)

echo === Ergebnis =============================================
for %%R in (%RIDS%) do (
    set "ZIEL=%OUTPUT_ROOT%\%%R"
    if exist "!ZIEL!" (
        echo !ZIEL!
        rem Nur die startbaren Dateien auffuehren; die Begleitdateien
        rem interessieren beim Ausliefern nicht.
        for %%F in ("!ZIEL!\obfuskation.exe" "!ZIEL!\obfuskation-gui.exe" ^
                    "!ZIEL!\obfuskation" "!ZIEL!\obfuskation-gui") do (
            if exist "%%~F" echo   %%~nxF %%~zF Byte
        )
    )
)
exit /b 0
