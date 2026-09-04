#!/usr/bin/env bash
#
# icon-erzeugen.sh — erzeugt das Anwendungssymbol aus den SVG-Vorlagen.
#
# Motiv: ein Blatt mit ersetzten Zeilen — die Kernfunktion der Anwendung.
# Farben aus der dunklen Farbwelt, damit Symbol und Oberflaeche
# zusammengehoeren.
#
# Das Ergebnis liegt im Repository; dieses Skript ist nur noetig, wenn das
# Motiv geaendert werden soll. Es laeuft von Hand, nicht im Build — deshalb
# gibt es auch kein .cmd-Gegenstueck.
#
# Die kleinen Groessen kommen aus einer eigenen, vereinfachten Vorlage statt
# aus einer heruntergerechneten grossen: ein skalierter Haarstrich wird bei
# 16 px zu grauem Brei.
#
# Voraussetzungen: rsvg-convert (librsvg) und ImageMagick 7.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WURZEL="$(dirname "${SCRIPT_DIR}")"

QUELLE_GROSS="${WURZEL}/assets/obfuskation.svg"
QUELLE_KLEIN="${WURZEL}/assets/obfuskation-klein.svg"
ZIEL_DIR="${WURZEL}/src/Obfuskation.Gui/Assets"

# Bis einschliesslich dieser Kantenlaenge wird die vereinfachte Vorlage genommen.
GRENZE_KLEIN=24

GROESSEN=(16 24 32 48 64 128 256)

usage() {
  cat <<'EOF'
Verwendung: ./build/icon-erzeugen.sh [Optionen]

Erzeugt aus assets/obfuskation.svg und assets/obfuskation-klein.svg:
  src/Obfuskation.Gui/Assets/obfuskation.ico   (Windows, alle Groessen)
  src/Obfuskation.Gui/Assets/obfuskation.png   (Linux, 256 px)

Optionen:
  --ziel <verzeichnis>   Abweichendes Ausgabeverzeichnis
  --behalten             Die einzelnen PNG-Groessen nicht loeschen
                         (zum Nachsehen, ob 16 px noch lesbar ist)
  -h, --help             Diese Hilfe
EOF
}

BEHALTEN="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --ziel)      ZIEL_DIR="${2:?--ziel braucht einen Wert}"; shift 2 ;;
    --behalten)  BEHALTEN="true"; shift ;;
    -h|--help)   usage; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; echo "Hilfe: $0 --help" >&2; exit 1 ;;
  esac
done

for werkzeug in rsvg-convert magick; do
  if ! command -v "${werkzeug}" >/dev/null 2>&1; then
    echo "Fehler: ${werkzeug} ist nicht installiert." >&2
    echo "  paru -S librsvg imagemagick" >&2
    exit 1
  fi
done

for datei in "${QUELLE_GROSS}" "${QUELLE_KLEIN}"; do
  [[ -f "${datei}" ]] || { echo "Fehler: ${datei} nicht gefunden." >&2; exit 1; }
done

mkdir -p "${ZIEL_DIR}"
ARBEIT="$(mktemp -d)"
trap 'rm -rf "${ARBEIT}"' EXIT

echo "Vorlagen : $(basename "${QUELLE_GROSS}"), $(basename "${QUELLE_KLEIN}")"
echo "Ziel     : ${ZIEL_DIR}"
echo

PNGS=()
for groesse in "${GROESSEN[@]}"; do
  if [[ ${groesse} -le ${GRENZE_KLEIN} ]]; then
    quelle="${QUELLE_KLEIN}"
    vermerk="vereinfacht"
  else
    quelle="${QUELLE_GROSS}"
    vermerk="voll"
  fi

  ziel="${ARBEIT}/${groesse}.png"
  rsvg-convert -w "${groesse}" -h "${groesse}" "${quelle}" -o "${ziel}"
  PNGS+=("${ziel}")

  printf '  %3d px  %s\n' "${groesse}" "${vermerk}"
done

echo
magick "${PNGS[@]}" "${ZIEL_DIR}/obfuskation.ico"
echo "ICO      : ${ZIEL_DIR}/obfuskation.ico"

# Das PNG dient als Fenstersymbol unter Linux und in der .desktop-Datei.
cp "${ARBEIT}/256.png" "${ZIEL_DIR}/obfuskation.png"
echo "PNG      : ${ZIEL_DIR}/obfuskation.png"

if [[ "${BEHALTEN}" == "true" ]]; then
  cp "${PNGS[@]}" "${ZIEL_DIR}/"
  echo "Einzelne Groessen liegen ebenfalls in ${ZIEL_DIR}."
fi

echo
echo "Enthaltene Groessen:"
magick identify "${ZIEL_DIR}/obfuskation.ico" | sed 's/^/  /'
