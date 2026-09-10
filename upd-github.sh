#!/usr/bin/env bash
#
# upd-github.sh — Baut, paketiert und veroeffentlicht ein neues Release auf GitHub.
#
# Ablauf:
#   1. Version aus Directory.Build.props ermitteln
#   2. Pruefen, ob der Git-Arbeitsbereich sauber ist und gh bereitsteht
#   3. Tests ausfuehren (ueberspringbar mit --skip-tests)
#   4. ./build-release.sh aufrufen (linux-x64 und win-x64)
#   5. Auslieferungsordner zusammenstellen (Programme, Doku, Desktop-Integration)
#   6. Release-Archive schnueren (.tar.gz fuer Linux, .zip fuer Windows)
#   7. SHA256SUMS.txt generieren und die Auslieferungsordner wieder entfernen,
#      deren Inhalt jetzt in den Archiven steckt (behaltbar mit --behalten)
#   8. Git-Branch und Versionstag pushen
#   9. GitHub-Release via 'gh release create' samt Assets hochladen

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"

SKIP_TESTS="false"
DRAFT="false"
NO_PUSH="false"
NOTES_FILE=""
CUSTOM_TITLE=""
BEHALTEN="false"

usage() {
  cat <<'EOF'
Verwendung: ./upd-github.sh [Optionen]

Baut das Release fuer Linux und Windows, schnuert die Pakete, setzt das
Git-Tag und veroeffentlicht das Release auf GitHub.

Optionen:
  --skip-tests       'dotnet test' vorher ueberspringen
  --draft            Release auf GitHub als Entwurf anlegen (noch nicht oeffentlich)
  --no-push          Nur lokal bauen und paketieren, kein git push / gh release
  --notes-file <f>   Markdown-Datei mit Release-Hinweisen
  --title <text>     Abweichender Release-Titel (Vorgabe: "<Version> — Release")
  --behalten         Die Auslieferungsverzeichnisse nach dem Packen stehen lassen
  -h, --help         Diese Hilfe
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-tests) SKIP_TESTS="true"; shift ;;
    --draft)      DRAFT="true"; shift ;;
    --no-push)    NO_PUSH="true"; shift ;;
    --notes-file) NOTES_FILE="${2:?--notes-file braucht einen Dateipfad}"; shift 2 ;;
    --title)      CUSTOM_TITLE="${2:?--title braucht einen Text}"; shift 2 ;;
    --behalten)   BEHALTEN="true"; shift ;;
    -h|--help)    usage; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; echo "Hilfe: $0 --help" >&2; exit 1 ;;
  esac
done

# --- 1. Version ermitteln ----------------------------------------------------
if [[ ! -f "Directory.Build.props" ]]; then
  echo "Fehler: Directory.Build.props nicht gefunden." >&2
  exit 1
fi

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props | tr -d '[:space:]')
if [[ -z "${VERSION}" ]]; then
  echo "Fehler: Konnte keine <Version> in Directory.Build.props finden." >&2
  exit 1
fi

TAG="v${VERSION}"
echo "=== Release-Vorbereitung fuer Version ${VERSION} (${TAG}) ==="

# --- 2. Vorpruefungen ---------------------------------------------------------
if [[ "${NO_PUSH}" == "false" ]]; then
  if ! command -v gh >/dev/null 2>&1; then
    echo "Fehler: 'gh' (GitHub CLI) ist nicht installiert." >&2
    exit 1
  fi

  if ! gh auth status >/dev/null 2>&1; then
    echo "Fehler: 'gh' ist nicht bei GitHub angemeldet. Bitte 'gh auth login' ausfuehren." >&2
    exit 1
  fi

  # Pruefen, ob wir ueberhaupt auf main stehen: weiter unten wird fest
  # "main" gepusht, das Tag aber auf HEAD gesetzt. Auf einem anderen
  # Branch zeigte das Tag damit auf einen Stand, der gar nicht
  # veroeffentlicht wird.
  CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
  if [[ "${CURRENT_BRANCH}" != "main" ]]; then
    echo "Fehler: Release wird von main erstellt, aktuell ist '${CURRENT_BRANCH}' ausgecheckt." >&2
    exit 1
  fi

  # Pruefen, ob uncommittete Aenderungen vorliegen
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "Fehler: Das Git-Arbeitsverzeichnis enthaelt uncommittete Aenderungen." >&2
    echo "Bitte erst committen (oder verwerfen), bevor ein Release freigegeben wird:" >&2
    git status -s >&2
    exit 1
  fi

  # Pruefen, ob origin vorausgelaufen ist. Ohne das scheitert erst der
  # Push in Schritt 8 -- nach Tests und vollstaendigem Build fuer zwei
  # Plattformen.
  git fetch --quiet origin main
  if [[ -n "$(git rev-list HEAD..origin/main)" ]]; then
    echo "Fehler: origin/main enthaelt Commits, die lokal fehlen. Erst 'git pull --rebase'." >&2
    exit 1
  fi

  # Pruefen, ob Tag lokal bereits existiert
  if git rev-parse "${TAG}" >/dev/null 2>&1; then
    echo "Fehler: Lokaler Git-Tag ${TAG} existiert bereits." >&2
    exit 1
  fi

  # Pruefen, ob Tag auf dem Remote existiert
  if git ls-remote --tags origin "${TAG}" | grep -q "${TAG}"; then
    echo "Fehler: Remote-Git-Tag ${TAG} existiert bereits auf origin." >&2
    exit 1
  fi
fi

# Pack-Werkzeug fuer ZIP pruefen
ZIP_CMD=""
if command -v 7z >/dev/null 2>&1; then
  ZIP_CMD="7z"
elif command -v zip >/dev/null 2>&1; then
  ZIP_CMD="zip"
elif command -v python3 >/dev/null 2>&1; then
  ZIP_CMD="python3"
else
  echo "Fehler: Weder 7z noch zip noch python3 zum Erstellen von ZIP-Archiven gefunden." >&2
  exit 1
fi

# --- 3. Tests ausfuehren ------------------------------------------------------
if [[ "${SKIP_TESTS}" == "false" ]]; then
  echo "--> Führe Tests aus..."
  dotnet test --verbosity normal
fi

# --- 4. Release kompilieren ---------------------------------------------------
echo "--> Baue Release-Binaerdateien ueber ./build-release.sh..."
./build-release.sh

# --- 5. Auslieferungsverzeichnisse zusammenstellen ----------------------------
echo "--> Stelle Pakete zusammen..."
LINUX_DIR="publish/obfuskation-${VERSION}-linux-x64"
WIN_DIR="publish/obfuskation-${VERSION}-win-x64"
PAKETE_DIR="publish/pakete"

rm -rf "${LINUX_DIR}" "${WIN_DIR}"
mkdir -p "${LINUX_DIR}/packaging" "${WIN_DIR}" "${PAKETE_DIR}"

# Linux-Paket
cp "publish/linux-x64/obfuskation" "${LINUX_DIR}/"
cp "publish/linux-x64/obfuskation-gui" "${LINUX_DIR}/"
cp LICENSE README.md README.de.md "${LINUX_DIR}/"
cp packaging/obfuskation.desktop "${LINUX_DIR}/packaging/"
cp packaging/README.md "${LINUX_DIR}/packaging/"
cp src/Obfuskation.Gui/Assets/obfuskation.png "${LINUX_DIR}/packaging/"

# Windows-Paket
cp "publish/win-x64/obfuskation.exe" "${WIN_DIR}/"
cp "publish/win-x64/obfuskation-gui.exe" "${WIN_DIR}/"
cp LICENSE README.md README.de.md "${WIN_DIR}/"

# --- 6. Archive schnueren -----------------------------------------------------
echo "--> Packe Archive..."
LINUX_TAR="obfuskation-${VERSION}-linux-x64.tar.gz"
WIN_ZIP="obfuskation-${VERSION}-win-x64.zip"

tar -czf "${PAKETE_DIR}/${LINUX_TAR}" -C publish "obfuskation-${VERSION}-linux-x64"

case "${ZIP_CMD}" in
  7z)
    (cd publish && 7z a -tzip -bso0 -bsp0 "pakete/${WIN_ZIP}" "obfuskation-${VERSION}-win-x64")
    ;;
  zip)
    (cd publish && zip -qr "pakete/${WIN_ZIP}" "obfuskation-${VERSION}-win-x64")
    ;;
  python3)
    (cd publish && python3 -m zipfile -c "pakete/${WIN_ZIP}" "obfuskation-${VERSION}-win-x64")
    ;;
esac

# --- 7. Pruefsummen berechnen -------------------------------------------------
echo "--> Berechne SHA256-Pruefsummen..."
(cd "${PAKETE_DIR}" && sha256sum "${LINUX_TAR}" "${WIN_ZIP}" > SHA256SUMS.txt)
cat "${PAKETE_DIR}/SHA256SUMS.txt"

# Die beiden Verzeichnisse sind eine Zwischenstufe: ihr Inhalt steckt jetzt in
# den Archiven. Blieben sie liegen, sammelte sich unter publish/ mit jedem
# Release ein weiteres Paar von rund 60 MB an, das niemand je wieder anfasst.
# Mit --behalten bleiben sie stehen, um ein Paket vor dem Hochladen von Hand
# durchzusehen.
if [[ "${BEHALTEN}" == "true" ]]; then
  echo "--> Auslieferungsverzeichnisse bleiben stehen (--behalten aktiv):"
  echo "    ${LINUX_DIR}"
  echo "    ${WIN_DIR}"
else
  rm -rf "${LINUX_DIR}" "${WIN_DIR}"
fi

if [[ "${NO_PUSH}" == "true" ]]; then
  echo "=== Erfolgreich lokal gebaut und gepackt (--no-push aktiv) ==="
  exit 0
fi

# --- 8. Git Tag und Push ------------------------------------------------------
echo "--> Pushe Branch main und erstelle Tag ${TAG}..."
git push origin main
git tag -a "${TAG}" -m "Release ${VERSION}"
git push origin "${TAG}"

# --- 9. GitHub Release erstellen ----------------------------------------------
echo "--> Erstelle Release auf GitHub..."

RELEASE_TITLE="${CUSTOM_TITLE:-${VERSION} — Release}"
GH_ARGS=(
  release create "${TAG}"
  "${PAKETE_DIR}/${LINUX_TAR}"
  "${PAKETE_DIR}/${WIN_ZIP}"
  "${PAKETE_DIR}/SHA256SUMS.txt"
  --title "${RELEASE_TITLE}"
)

if [[ "${DRAFT}" == "true" ]]; then
  GH_ARGS+=(--draft)
fi

if [[ -n "${NOTES_FILE}" && -f "${NOTES_FILE}" ]]; then
  GH_ARGS+=(--notes-file "${NOTES_FILE}")
else
  # Vorgabe-Notizen generieren
  TEMP_NOTES=$(mktemp)
  trap 'rm -f "${TEMP_NOTES}"' EXIT
  cat <<EOF > "${TEMP_NOTES}"
## Release ${VERSION}

### Prüfsummen

\`\`\`
$(cat "${PAKETE_DIR}/SHA256SUMS.txt")
\`\`\`
EOF
  GH_ARGS+=(--notes-file "${TEMP_NOTES}")
fi

gh "${GH_ARGS[@]}"

echo "=== Release ${TAG} erfolgreich auf GitHub veröffentlicht! ==="
