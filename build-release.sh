#!/usr/bin/env bash
#
# build-release.sh — baut die Release-Fassung dieses Projekts.
#
# Gegenstueck: build-release.cmd (gleiche Optionen, gleiches Ergebnis).
# Aenderungen hier bitte dort nachziehen.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Ohne --rid werden beide Zielsysteme gebaut: das Werkzeug laeuft auf Linux
# und auf Windows, und beide Fassungen sollen aus einem Aufruf entstehen.
DEFAULT_RIDS=("win-x64" "linux-x64")
RIDS=()
SELF_CONTAINED="false"
SINGLE_FILE="true"
OUTPUT_ROOT=""
BUILD_CLI="true"
BUILD_GUI="true"

CLI_PROJECT="src/Obfuskation.Cli/Obfuskation.Cli.csproj"
GUI_PROJECT="src/Obfuskation.Gui/Obfuskation.Gui.csproj"

usage() {
  cat <<'EOF'
Verwendung: ./build-release.sh [Optionen]

Baut die Release-Fassung und legt sie unter publish/<RID>/ ab.
Ohne --rid werden win-x64 und linux-x64 gebaut, je mit Kommandozeilen-
programm und Oberflaeche.

Optionen:
  --rid <kennung>      Nur diese Ziellaufzeit bauen; mehrfach angebbar
                       z. B. win-x64, win-arm64, linux-x64, linux-arm64
  --self-contained     Runtime mitliefern; laeuft ohne installiertes .NET,
                       das Ergebnis wird dadurch deutlich groesser
  --no-single-file     Nicht zu einer einzelnen Programmdatei zusammenfassen
  --cli-only           Nur das Kommandozeilenprogramm
  --gui-only           Nur die Oberflaeche
  --output <pfad>      Abweichendes Wurzelverzeichnis; darunter entsteht <RID>/
  -h, --help           Diese Hilfe

Beispiele:
  ./build-release.sh
  ./build-release.sh --rid linux-x64
  ./build-release.sh --rid win-x64 --self-contained
  ./build-release.sh --cli-only --rid linux-x64
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)            RIDS+=("${2:?--rid braucht einen Wert}"); shift 2 ;;
    --self-contained) SELF_CONTAINED="true"; shift ;;
    --no-single-file) SINGLE_FILE="false"; shift ;;
    --cli-only)       BUILD_GUI="false"; shift ;;
    --gui-only)       BUILD_CLI="false"; shift ;;
    --output)         OUTPUT_ROOT="${2:?--output braucht einen Wert}"; shift 2 ;;
    -h|--help)        usage; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; echo "Hilfe: $0 --help" >&2; exit 1 ;;
  esac
done

if [[ ${#RIDS[@]} -eq 0 ]]; then
  RIDS=("${DEFAULT_RIDS[@]}")
fi

if [[ "${BUILD_CLI}" == "false" && "${BUILD_GUI}" == "false" ]]; then
  echo "Fehler: --cli-only und --gui-only schliessen einander aus." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Fehler: dotnet ist nicht installiert." >&2
  exit 1
fi

cd "${SCRIPT_DIR}"
[[ -z "${OUTPUT_ROOT}" ]] && OUTPUT_ROOT="publish"

# Die Projekte werden einzeln veroeffentlicht. Die Solution zu nehmen wuerde
# auch die Bibliothek und das Testprojekt mitschleifen.
PROJECTS=()
[[ "${BUILD_CLI}" == "true" ]] && PROJECTS+=("${CLI_PROJECT}")
[[ "${BUILD_GUI}" == "true" && -f "${GUI_PROJECT}" ]] && PROJECTS+=("${GUI_PROJECT}")

if [[ "${BUILD_GUI}" == "true" && ! -f "${GUI_PROJECT}" ]]; then
  echo "Hinweis: ${GUI_PROJECT} gibt es nicht, die Oberflaeche wird uebersprungen." >&2
fi

for projekt in "${PROJECTS[@]}"; do
  if [[ ! -f "${projekt}" ]]; then
    echo "Fehler: ${projekt} nicht gefunden." >&2
    exit 1
  fi
done

veroeffentlichen() {
  local projekt="$1" rid="$2" ziel="$3"

  # Die Wertform "--self-contained false" schaltet unter SDK 10 nicht ab: das
  # Ergebnis enthaelt dann trotzdem die vollstaendige Runtime (67 MB statt
  # 0,8 MB). Nur die Schalterform wirkt zuverlaessig.
  local sc_schalter="--no-self-contained"
  [[ "${SELF_CONTAINED}" == "true" ]] && sc_schalter="--self-contained"

  # Ohne IncludeNativeLibrariesForSelfExtract blieben die nativen
  # Bibliotheken der Oberflaeche (Skia, HarfBuzz) als eigene Dateien liegen —
  # dann waere "eine Programmdatei" nur die halbe Wahrheit.
  echo "  ${projekt}"
  dotnet publish "${projekt}" \
    --configuration Release \
    --runtime "${rid}" \
    "${sc_schalter}" \
    -p:PublishSingleFile="${SINGLE_FILE}" \
    -p:IncludeNativeLibrariesForSelfExtract="${SINGLE_FILE}" \
    -p:DebugType=none \
    --output "${ziel}" \
    --nologo \
    --verbosity quiet
}

for rid in "${RIDS[@]}"; do
  ziel="${OUTPUT_ROOT}/${rid}"

  echo "=== ${rid} ==============================================="
  echo "Self-contained: ${SELF_CONTAINED}   Einzeldatei: ${SINGLE_FILE}"
  echo "Ausgabe       : ${ziel}"

  for projekt in "${PROJECTS[@]}"; do
    veroeffentlichen "${projekt}" "${rid}" "${ziel}"
  done

  echo
done

echo "=== Ergebnis ============================================="
for rid in "${RIDS[@]}"; do
  ziel="${OUTPUT_ROOT}/${rid}"
  [[ -d "${ziel}" ]] || continue

  if [[ "${ziel}" = /* ]]; then echo "${ziel}"; else echo "${SCRIPT_DIR}/${ziel}"; fi
  # Nur die startbaren Dateien auffuehren; die Begleitdateien interessieren
  # beim Ausliefern nicht.
  find "${ziel}" -maxdepth 1 -type f \( -name 'obfuskation' -o -name 'obfuskation-gui' \
       -o -name 'obfuskation.exe' -o -name 'obfuskation-gui.exe' \) -printf '  %-24f %10s Byte\n' \
       2>/dev/null | sort
done
