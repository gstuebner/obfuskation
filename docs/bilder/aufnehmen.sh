#!/bin/bash
# Nimmt die Bilder der Dokumentation auf.
#
# Die Aufnahme laeuft auf einem virtuellen Bildschirm (Xvfb), nicht auf dem
# echten. Das hat drei Gruende: die Fenstergroesse ist damit fest und die Bilder
# bleiben zwischen zwei Laeufen vergleichbar; die Skalierung ist 1:1, sodass
# keine hochaufloesenden Bilder nachtraeglich verkleinert werden muessen; und
# ein gesperrter oder abgeschalteter Bildschirm haelt den Lauf nicht auf —
# GNOME verweigert dann jede Aufnahme mit "Screenshot is not allowed".
#
# Voraussetzungen: xorg-server-xvfb, xdotool, imagemagick, alacritty.
#
#   ./docs/bilder/aufnehmen.sh
#
# Erstellt von Gregor Stuebner und Claude (Anthropic).

set -u

REPO=$(cd "$(dirname "$0")/../.." && pwd)
GUI=$REPO/publish/linux-x64/obfuskation-gui
CLI=$REPO/publish/linux-x64/obfuskation
ZIEL=$REPO/docs/bilder
BEISPIEL=$REPO/docs/beispiel

ARBEIT=$(mktemp -d /tmp/obfuskation-bilder.XXXXXX)
trap 'aufraeumen' EXIT

DISPLAY_NR=:77
export DISPLAY=$DISPLAY_NR
unset WAYLAND_DISPLAY          # sonst zieht Alacritty die Wayland-Sitzung vor
export XDG_SESSION_TYPE=x11
export XDG_CONFIG_HOME=$ARBEIT/cfg
export DBUS_SESSION_BUS_ADDRESS=/dev/null   # erzwingt Avalonias eigene Dateidialoge
export PATH="$ARBEIT/bin:$PATH"

aufraeumen() {
  pkill -x obfuskation-gui 2>/dev/null
  pkill -x alacritty 2>/dev/null
  [ -n "${XVFB_PID:-}" ] && kill "$XVFB_PID" 2>/dev/null
  rm -rf "$ARBEIT"
}

fehler() { echo "Fehler: $*" >&2; exit 1; }

# ---------------------------------------------------------------- Vorbereiten

[ -x "$GUI" ] || fehler "$GUI fehlt. Erst ./build-release.sh ausfuehren."
for werkzeug in Xvfb xdotool import magick alacritty; do
  command -v "$werkzeug" >/dev/null || fehler "$werkzeug ist nicht installiert."
done

mkdir -p "$ARBEIT/cfg/obfuskation" "$ARBEIT/bin" "$ARBEIT/beispiel" "$ZIEL"
ln -sf "$CLI" "$ARBEIT/bin/obfuskation"
cp "$BEISPIEL"/*.csv "$BEISPIEL"/antwort-der-ki.txt "$BEISPIEL"/*.json "$ARBEIT/beispiel/"

Xvfb $DISPLAY_NR -screen 0 1600x1000x24 -dpi 96 -nolisten tcp > "$ARBEIT/xvfb.log" 2>&1 &
XVFB_PID=$!
sleep 3
xdotool getdisplaygeometry >/dev/null || fehler "Der virtuelle Bildschirm startet nicht."

# Die Ersetzungstabelle des Demo-Profils frisch anlegen, damit die Zaehler in
# den Bildern zu denen der Testdokumentation passen.
rm -rf ~/.local/share/obfuskation/demo
(cd "$ARBEIT/beispiel" \
  && obfuskation obfuscate stammdaten.csv -o stammdaten.pseudo.csv --strict --config profil-demo.json \
  && obfuskation obfuscate konten.csv     -o konten.pseudo.csv     --strict --config profil-demo.json \
  && obfuskation obfuscate buchungen.csv  -o buchungen.pseudo.csv  --strict --config profil-demo.json) \
  > "$ARBEIT/vorlauf.log" 2>&1 || fehler "Der Vorlauf ist gescheitert, siehe $ARBEIT/vorlauf.log"

# Die Beispielantwort der KI traegt Pseudonyme, und die gehoeren zu genau
# dieser Ersetzungstabelle. Das Salt entsteht mit jeder neuen Tabelle neu —
# eine mitgelieferte, feste Datei liesse sich nach einem Neuaufbau nicht mehr
# zurueckholen. Deshalb wird sie hier aus dem eben erzeugten Bestand
# geschrieben.
python3 - "$ARBEIT/beispiel" "$BEISPIEL/antwort-der-ki.txt" <<'PY'
import csv, os, sys

quelle, ziel = sys.argv[1], sys.argv[2]

def lies(name):
    with open(os.path.join(quelle, name), encoding="utf-8") as datei:
        return list(csv.DictReader(datei, delimiter=";"))

stamm, konten = lies("stammdaten.pseudo.csv"), lies("konten.pseudo.csv")
a, b, ka, kb = stamm[0], stamm[1], konten[0], konten[1]

open(ziel, "w", encoding="utf-8").write(f"""Auswertung der übergebenen Beispieldaten
=======================================

Die Datei enthält 120 Datensätze. Auffällig sind zwei Konten:

- {a['Vorname']} {a['Nachname']} (Personennummer {a['Personennummer']}),
  erreichbar unter {a['EMail']} und {a['Telefon']},
  Konto {ka['IBAN']} bei {ka['BIC']}, Stand {ka['Kontostand']}.
- {b['Vorname']} {b['Nachname']} (Personennummer {b['Personennummer']}),
  Konto {kb['IBAN']}, Stand {kb['Kontostand']}.

Vorschlag für die Auswertung:

```python
import pandas as pd

df = pd.read_csv("stammdaten.csv", sep=";", encoding="utf-8")
auffaellig = df[df["Personennummer"].isin([{a['Personennummer']}, {b['Personennummer']}])]
print(auffaellig[["Personennummer", "Nachname", "EMail"]])
```

Die Anschrift „{a['Straße']}, {a['PLZ']} {a['Ort']}" kommt in beiden
Beständen vor und sollte vereinheitlicht werden.
""")
PY
cp "$BEISPIEL/antwort-der-ki.txt" "$ARBEIT/beispiel/antwort-der-ki.txt"

# ------------------------------------------------------------ Oberflaeche

# LETZTES_VERZEICHNIS (optional gesetzt) landet als lastDataDirectory in
# gui.json, damit der Dateiauswahldialog von "Neu aus Datei..." gleich im
# Beispielordner steht, statt in einem beliebigen Vorgabeverzeichnis.
thema() {
  if [ -n "${LETZTES_VERZEICHNIS:-}" ]; then
    python3 - "$XDG_CONFIG_HOME/obfuskation/gui.json" "$1" "$LETZTES_VERZEICHNIS" <<'PY'
import json, sys
ziel, thema, ordner = sys.argv[1], sys.argv[2], sys.argv[3]
json.dump({"theme": thema, "windowWidth": 1040, "windowHeight": 720,
           "lastDataDirectory": ordner}, open(ziel, "w", encoding="utf-8"))
PY
  else
    printf '{\n  "theme": "%s",\n  "windowWidth": 1040,\n  "windowHeight": 720\n}\n' \
      "$1" > "$XDG_CONFIG_HOME/obfuskation/gui.json"
  fi
}

start() {                       # start [argumente...]
  thema "${THEMA:-light}"
  setsid "$GUI" "$@" > "$ARBEIT/gui.log" 2>&1 &
  for _ in $(seq 1 40); do
    WID=$(xdotool search --onlyvisible --name '^Obfuskation' 2>/dev/null | tail -1)
    [ -n "$WID" ] && break
    sleep 0.5
  done
  [ -n "$WID" ] || fehler "Die Oberflaeche zeigt kein Fenster. $(tail -3 "$ARBEIT/gui.log")"
  sleep 2
}

halt() { pkill -x obfuskation-gui; sleep 1; }

bild() { sleep 1; import -window "${2:-$WID}" "$ZIEL/$1.png"; echo "  $1.png"; }

# Aufnahme einschliesslich Popups: Auswahllisten und Menues liegen unter X11 in
# eigenen Fenstern und stehen deshalb nicht im Bild des Hauptfensters.
bildp() {
  sleep 1
  import -window root "$ARBEIT/roh.png"
  magick "$ARBEIT/roh.png" -crop 1040x720+10+10 +repage "$ZIEL/$1.png"
  echo "  $1.png"
}

klick()  { xdotool mousemove --window "$WID" "$1" "$2" click 1; sleep 1.2; }
klickw() { xdotool mousemove --window "$1" "$2" "$3" click 1; sleep 1.2; }

# Bestaetigt den Speichern-Dialog. Ohne DBus zeigt Avalonia seinen eigenen;
# dessen Groesse haengt vom Bildschirm ab, feste Koordinaten treffen daneben.
# Deshalb wird die Schaltflaeche aus der Fenstergroesse berechnet: sie sitzt
# 147 Bildpunkte vom rechten und 23 vom unteren Rand.
speichern_dialog() {
  local dialog breite hoehe
  for _ in $(seq 1 20); do
    dialog=$(xdotool search --onlyvisible --name 'Speichern unter' | tail -1)
    [ -n "$dialog" ] && break
    sleep 0.5
  done
  [ -n "$dialog" ] || fehler "Der Speichern-Dialog erscheint nicht."

  eval "$(xdotool getwindowgeometry --shell "$dialog")"
  breite=$WIDTH; hoehe=$HEIGHT
  xdotool mousemove --window "$dialog" $((breite - 147)) $((hoehe - 23)) click 1
  sleep 3

  # Nachsehen, ob der Dialog wirklich weg ist; sonst stimmt das naechste Bild nicht.
  xdotool search --onlyvisible --name 'Speichern unter' >/dev/null 2>&1 \
    && fehler "Der Speichern-Dialog liess sich nicht bestaetigen."
  return 0
}

# Wartet, bis ein Fenster mit passendem Titel erscheint, und gibt seine
# xdotool-Fenster-ID aus. Wie die Wartschleife in speichern_dialog, nur
# fuer einen beliebigen Titel statt fest "Speichern unter".
warte_fenster() {                # warte_fenster <name-regex>
  local fenster
  for _ in $(seq 1 20); do
    fenster=$(xdotool search --onlyvisible --name "$1" | tail -1)
    [ -n "$fenster" ] && { echo "$fenster"; return 0; }
    sleep 0.5
  done
  # Kein fehler() hier: die Funktion laeuft in $(...), ein exit traefe nur die
  # Subshell. Der Aufrufer prueft den Rueckgabewert und bricht selbst ab --
  # sonst wird mit leerer Fenster-ID ein falsches Bild aufgenommen.
  echo "Das Fenster '$1' erscheint nicht." >&2
  return 1
}

# Klickt in die rechte untere Ecke eines Fensters mit SizeToContent-Hoehe
# (Neues Profil, Profil umbenennen, Rueckfrage): dort sitzt immer die
# betonte, rechte Schaltflaeche (Anlegen/Umbenennen/Speichern). Dieselbe
# Idee wie bei speichern_dialog, aber allgemein ueber die tatsaechliche
# Fenstergroesse statt fester Zahlen fuer ein bestimmtes Fenster.
klick_ecke() {                   # klick_ecke <fenster> <dx-von-rechts> <dy-von-unten>
  local fenster=$1 dx=$2 dy=$3
  eval "$(xdotool getwindowgeometry --shell "$fenster")"
  xdotool mousemove --window "$fenster" $((WIDTH - dx)) $((HEIGHT - dy)) click 1
  sleep 1.5
}

echo "Oberflaeche:"
B=$ARBEIT/beispiel

start;                                                        bild gui-leer; halt

start --config "$B/profil-geruest.json" "$B/stammdaten.csv"
bild gui-alle-offen
klick 103 668;                                                 bild gui-abbruch-offene-felder; halt

start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 85 192;                                                 bild gui-feld-regel
klick 730 194;                                                bildp gui-aktion-auswahl
xdotool key Escape; sleep 1
klick 730 256;                                                bildp gui-generator-auswahl
xdotool key Escape; sleep 1
klick 926 28;                                                 bildp gui-mehr-menue
klick 900 90; sleep 2
M=$(xdotool search --onlyvisible --name 'Ersetzungstabelle' | tail -1)
[ -n "$M" ] && bild gui-ersetzungstabelle "$M"
halt

# Kurzhilfe und Ueber-Fenster liegen im Menue "Mehr" untereinander. Achtung
# bei Aenderungen am Menue: ein zusaetzlicher Eintrag verschiebt alles darunter.
# Stand jetzt: Textregeln 62, Ersetzungstabelle 90, Trenner, Kurzhilfe 127,
# Ueber 155.
start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 926 28; klick 900 127; sleep 2
K=$(warte_fenster '^Kurzhilfe$') || fehler "Aufnahme abgebrochen."
# Groesser aufziehen als die Vorgabe: sonst zeigt das Bild nur die oberen
# beiden Karten, und gerade der Abschnitt ueber die Profile faellt weg.
xdotool windowsize "$K" 620 900; sleep 1.5
bild gui-kurzhilfe "$K"
halt

start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 926 28; klick 900 155; sleep 2
U=$(xdotool search --onlyvisible --name 'Obfuskation' | grep -v "^$WID$" | tail -1)
[ -n "$U" ] && bild gui-ueber "$U"
halt

start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 926 28; klick 900 62; sleep 2
T=$(xdotool search --onlyvisible --name 'Textregeln' | tail -1)
bild gui-textregeln "$T"
klickw "$T" 625 147; xdotool key ctrl+a; sleep 0.4
xdotool type --delay 25 '\b[A-Z]{2}\d{2}('; sleep 1.5
bild gui-textregel-fehlerhaft "$T"
klickw "$T" 625 147; xdotool key ctrl+a; sleep 0.4
xdotool type --delay 25 '\d{4}'; sleep 1.5
bild gui-textregel-zu-weit "$T"
halt

# Ersetzen laeuft ueber den Speichern-Dialog; ohne DBus zeigt Avalonia den
# eigenen, dessen "Save" unten rechts liegt.
#
# Die Zieldatei muss vorher weg: der Vorlauf oben hat sie bereits angelegt, und
# der Dialog laeuft mit ShowOverwritePrompt. Die Rueckfrage "existiert bereits,
# ersetzen?" erscheint als Einblendung *im* Dialogfenster, ist also von aussen
# nicht als eigenes Fenster zu fassen -- speichern_dialog liefe ins Leere. Die
# Aufnahme darauf einzustellen waere Koordinatenraten; die Datei zu loeschen ist
# eindeutig. Sie entsteht in diesem Schritt ohnehin neu und wird erst danach
# gebraucht.
rm -f "$B/stammdaten.pseudo.csv"
start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 103 668; sleep 2
speichern_dialog;                                             bild gui-ergebnis-ersetzen; halt

start --config "$B/profil-demo.json" "$B/stammdaten.pseudo.csv"
klick 426 668; sleep 3;                                       bild gui-pruefen-sauber
klick 292 668; sleep 2
speichern_dialog;                                             bild gui-zurueckholen; halt

start --config "$B/profil-demo.json" "$B/buchungen.pseudo.csv"
klick 426 668; sleep 4;                                       bild gui-pruefen-befund; halt

start --config "$B/profil-demo.json" "$B/konten.csv"
klick 85 259;                                                 bild gui-fehler-generator-leer; halt

# Fuer die Vorschau ohne Tabelle braucht es ein Profil ohne eigene Ablage.
python3 - "$B" <<'PY'
import json, sys, collections, os
b = sys.argv[1]
d = json.load(open(os.path.join(b, "profil-demo.json"), encoding="utf-8"),
              object_pairs_hook=collections.OrderedDict)
d["profileName"] = "frisch"
d["mappingStore"] = os.path.join(b, "frisch", "mapping.json")
json.dump(d, open(os.path.join(b, "profil-frisch.json"), "w", encoding="utf-8"),
          ensure_ascii=False, indent=2)
PY
rm -rf "$B/frisch"
start --config "$B/profil-frisch.json" "$B/stammdaten.csv"
klick 85 203;                                                 bild gui-vorschau-beispielhaft; halt

start --config "$B/kaputt-profil.json" "$B/stammdaten.csv"
klick 103 668; sleep 2;                                        bild gui-kaputte-konfiguration; halt

# Fortschritt und Abbruch zeigen sich erst bei einer grossen Datei.
head -1 "$B/buchungen.csv" > "$B/buchungen-gross.csv"
for _ in $(seq 1 50); do tail -n +2 "$B/buchungen.csv" >> "$B/buchungen-gross.csv"; done

start --config "$B/profil-streng.json" "$B/buchungen-gross.csv"
xdotool mousemove --window "$WID" 256 668 click 1; sleep 3;   bild gui-fortschritt; halt

start --config "$B/profil-streng.json" "$B/buchungen-gross.csv"
xdotool mousemove --window "$WID" 256 668 click 1; sleep 3
xdotool mousemove --window "$WID" 689 668 click 1; sleep 2;   bild gui-abbruch; halt

THEMA=dark start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 85 203;                                                 bild gui-dunkel; halt

# -------------------------------------------- Profilverwaltung (Fassung 1.0.3)
#
# Die Koordinaten der Kopfzeilen-Schaltflaechen "Neu aus Datei..." und
# "Profile..." sowie die Positionen innerhalb der Profiluebersicht sind
# Schaetzwerte -- anders als der Rest dieses Skripts liessen sie sich nicht an
# einem echten Bildschirm nachmessen. Beim ersten Lauf gegen die tatsaechlich
# entstandenen Bilder pruefen und bei Bedarf anpassen, genau wie die uebrigen
# Koordinaten hier einmal eingemessen wurden.

# Neues Profil: "Neu aus Datei..." aus dem Leerzustand, Dateiauswahl (Titel
# "Datei oeffnen"), dann der Anlegen-Dialog.
#
# Der Oeffnen-Dialog hat, anders als der Speichern-Dialog, kein Namensfeld:
# getippter Text laeuft dort ins Leere und Return bestaetigt nichts. Die Zeile
# muss angeklickt werden. Damit ihre Position feststeht, zeigt der Dialog auf
# ein eigenes Verzeichnis mit genau einer Datei -- im Beispielordner haenge die
# Zeilennummer sonst davon ab, wie viele Dateien der Vorlauf erzeugt hat.
mkdir -p "$ARBEIT/nur-stammdaten"
cp "$B/stammdaten.csv" "$ARBEIT/nur-stammdaten/"
LETZTES_VERZEICHNIS=$ARBEIT/nur-stammdaten start
klick 650 28; sleep 2
O=$(warte_fenster 'Datei oeffnen') || fehler "Aufnahme abgebrochen."
klickw "$O" 287 85                      # die einzige Zeile der Liste
eval "$(xdotool getwindowgeometry --shell "$O")"
klickw "$O" $((WIDTH - 147)) $((HEIGHT - 23)); sleep 2
N=$(warte_fenster 'Neues Profil') || fehler "Aufnahme abgebrochen."
bild gui-profil-neu "$N"
# Kein Schliessklick: halt beendet die Oberflaeche ohnehin, und ein Fehlklick
# auf "Anlegen" wuerde ungewollt ein Profil schreiben.
halt
unset LETZTES_VERZEICHNIS

# Profiluebersicht und Umbenennen. Das offene Profil muss dabei gespeichert
# sein: die Uebersicht verweigert das Umbenennen des gerade geladenen Profils,
# solange dort ungesicherte Regeln liegen, weil das anschliessende Nachladen
# sie verwerfen wuerde. Deshalb hier keine Regelaenderung vorweg.
# Die Zeile des offenen Profils ist beim Oeffnen bereits ausgewaehlt.
start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 731 28; sleep 2
P=$(warte_fenster '^Profile$') || fehler "Aufnahme abgebrochen."
bild gui-profiluebersicht "$P"
klickw "$P" 155 572; sleep 2
R=$(warte_fenster '^Profil umbenennen$') || fehler "Aufnahme abgebrochen."
bild gui-profil-umbenennen "$R"
halt

# Die Rueckfrage bei ungespeicherten Aenderungen. Ausgeloest ueber "Neu aus
# Datei...": der Vorgang prueft als Erstes, ob noch etwas ungesichert ist, und
# fragt nach, bevor er ueberhaupt den Dateidialog oeffnet. Das kommt ohne ein
# zweites Profil in der Liste aus und ist damit unabhaengig davon, was der
# Nutzungs-Index bis hierher gesammelt hat.
# Die Aenderung per Maus, nicht per Tastatur: ohne Fenstermanager bekommt das
# aufgeklappte Auswahlfeld keinen Tastaturfokus, Down/Return liefen ins Leere
# und das Profil bliebe unveraendert -- die Rueckfrage kaeme dann nie.
start --config "$B/profil-demo.json" "$B/stammdaten.csv"
klick 85 192; klick 730 194; sleep 1
klick 496 256; sleep 1                  # "durchlassen" in der offenen Liste
klick 650 28; sleep 2
# Bewusst ohne den Umlaut im Muster: xdotool findet das Fenster mit
# '^Ungespeicherte Änderungen$' nicht, obwohl es offen ist -- der Vergleich
# stolpert ueber das mehrbyteige "Ä". Der ASCII-Anfang genuegt zur Unterscheidung.
C=$(warte_fenster '^Ungespeicherte') || fehler "Aufnahme abgebrochen."
bild gui-ungespeichert "$C"
halt

# ------------------------------------------------------------- Kommandozeile

cat > "$ARBEIT/term.sh" <<'EOS'
#!/bin/bash
# Fuehrt eine Befehlszeile aus und laesst sie stehen, damit sie aufgenommen
# werden kann. Erster Parameter ist das Arbeitsverzeichnis.
cd "$1" || exit 1; shift
printf "\033[1;34m~/$(basename "$PWD")\033[0m \033[1;32m$\033[0m %s\n" "$*"
eval "$@"
printf '\033[2m[Rueckgabewert %s]\033[0m\n' "$?"
sleep 900
EOS
chmod +x "$ARBEIT/term.sh"

# Helles Farbschema, damit die Bilder zu denen der Oberflaeche passen.
term() {                        # term <name> <spalten> <zeilen> <befehl...>
  local name=$1 spalten=$2 zeilen=$3 wartezeit=${TERM_WARTE:-5}; shift 3
  pkill -x alacritty; sleep 0.5
  setsid alacritty \
    -o "window.dimensions.columns=$spalten" -o "window.dimensions.lines=$zeilen" \
    -o 'window.padding.x=10' -o 'window.padding.y=8' -o 'font.size=10' \
    -o 'colors.primary.background="#ffffff"' -o 'colors.primary.foreground="#1b2733"' \
    -o 'colors.normal.black="#1b2733"'  -o 'colors.normal.red="#b3261e"' \
    -o 'colors.normal.green="#146b3a"'  -o 'colors.normal.yellow="#8a6100"' \
    -o 'colors.normal.blue="#1a4f8a"'   -o 'colors.normal.magenta="#7b3fa0"' \
    -o 'colors.normal.cyan="#0f6f7a"'   -o 'colors.normal.white="#5b6b7b"' \
    -o 'colors.bright.black="#8b98a5"'  -o 'colors.bright.red="#b3261e"' \
    -o 'colors.bright.green="#146b3a"'  -o 'colors.bright.yellow="#8a6100"' \
    -o 'colors.bright.blue="#1a4f8a"'   -o 'colors.bright.magenta="#7b3fa0"' \
    -o 'colors.bright.cyan="#0f6f7a"'   -o 'colors.bright.white="#1b2733"' \
    -e "$ARBEIT/term.sh" "${TERM_VERZEICHNIS:-$ARBEIT/beispiel}" "$@" \
    > "$ARBEIT/term.log" 2>&1 &
  sleep "$wartezeit"
  local fenster
  fenster=$(xdotool search --onlyvisible --name 'Alacritty' | tail -1)
  [ -n "$fenster" ] || fehler "Kein Terminalfenster."
  import -window "$fenster" "$ZIEL/$name.png"
  # Auf den Inhalt beschneiden, damit unten kein leerer Block stehen bleibt.
  magick "$ZIEL/$name.png" -bordercolor white -border 1 -fuzz 1% -trim +repage \
         -bordercolor white -border 14 "$ZIEL/$name.png"
  echo "  $name.png"
}

echo "Kommandozeile:"
rm -f "$ARBEIT/beispiel/regelgeruest.json"
term cli-hilfe               100 30 'obfuskation --help'
term cli-init                100 16 'obfuskation init --profile demo --from stammdaten.csv --config regelgeruest.json'
term cli-n13-init-vorhanden  100 10 'obfuskation init --profile demo --from stammdaten.csv --config regelgeruest.json'
term cli-obfuscate           100 14 'obfuskation obfuscate konten.csv -o konten.pseudo.csv --strict --config profil-demo.json'
term cli-mapping-list        100 18 'obfuskation mapping list --config profil-demo.json'
term cli-antwort-der-ki       96 28 'cat antwort-der-ki.txt'
term cli-deobfuscate          96 40 'cat antwort-der-ki.txt | obfuskation deobfuscate --config profil-demo.json'
term cli-n01-offene-felder   104 10 'obfuskation obfuscate stammdaten.csv -o x.csv --config regelgeruest.json'
term cli-n03-kaputtes-profil 118 20 'obfuskation obfuscate stammdaten.csv -o x.csv --config kaputt-profil.json'
term cli-n11-scan-befund     104 34 'obfuskation scan buchungen.pseudo.csv --config profil-demo.json'

# Die Tabelle darf nicht im Arbeitsbaum liegen — dafuer ein eigenes Profil.
python3 - "$ARBEIT/beispiel" "$REPO" <<'PY'
import json, sys, collections, os
b, repo = sys.argv[1], sys.argv[2]
d = json.load(open(os.path.join(b, "profil-demo.json"), encoding="utf-8"),
              object_pairs_hook=collections.OrderedDict)
d["profileName"] = "imrepo"
d["mappingStore"] = os.path.join(repo, "mapping.json")
json.dump(d, open(os.path.join(b, "profil-imrepo.json"), "w", encoding="utf-8"),
          ensure_ascii=False, indent=2)
PY
term cli-n05-git 104 12 'obfuskation obfuscate stammdaten.csv -o x.csv --config profil-imrepo.json'

TERM_VERZEICHNIS=$REPO TERM_WARTE=50 term cli-tests 112 22 'dotnet test 2>&1 | tail -14'

echo
echo "Fertig. Bilder liegen in $ZIEL"
