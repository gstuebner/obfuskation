# Rohprotokoll der Testdurchführung

Diese Datei hält fest, was bei der Durchführung tatsächlich beobachtet wurde.
Sie ist die Quelle für `docs/testdokumentation.md`; dort steht nichts, was hier
nicht gemessen wurde.

**Prüfer:** Gregor Stübner (Durchführung durch Claude Opus 5 im Auftrag)
**Programme:** `publish/linux-x64/obfuskation` und `publish/linux-x64/obfuskation-gui`
(die veröffentlichten Binärdateien, nicht `dotnet run`)

**Zwei Durchführungen:**

| | Datum | Fassung | Ergebnis |
|---|---|---|---|
| 1 | 4. September 2026 | `1.0.0+cfec1d5` | Katalog vollständig abgearbeitet, neun Befunde (D-1 bis D-9) |
| 2 | 5. September 2026 | `1.0.1+cfec1d5` | Katalog nach Behebung von D-4 und D-8 erneut abgearbeitet |

**Alle Werte, Ausgaben und Bilder in diesem Protokoll stammen aus
Durchführung 2**, sofern nicht ausdrücklich anders vermerkt.

Zwischen beiden Durchführungen wurde die Ersetzungstabelle neu angelegt.
**Die Pseudonyme unterscheiden sich deshalb**, denn das Salt entsteht mit jeder
neuen Tabelle zufällig neu. Alle Zähler blieben gleich, mit einer Ausnahme: die
Zahl der Verdachtsfälle in `buchungen.pseudo.csv` ging von 1115 auf 1112
zurück, weil sich Pseudonyme und Klartexte anders überschneiden. Das ist selbst
ein Beleg dafür, dass die Prüfung an den Werten arbeitet und nicht an einer
festen Liste.

**Umgebung:**

| | |
|---|---|
| Betriebssystem | CachyOS (Arch-basiert), Kernel 7.2.0-1-cachyos |
| Sitzung | GNOME auf Wayland, Xwayland unter `DISPLAY=:0` |
| .NET | SDK 10.0, Laufzeiten `Microsoft.NETCore.App` 9.0.19 und 10.0.11 |
| Zielframework | `net8.0` mit `RollForward=Major` |
| Bildschirm | 5120×2880, Skalierung 2× (Fenster 1040×720 logisch = 2080×1440 Bildpunkte) |
| Arbeitsverzeichnis | Wegwerfverzeichnis außerhalb des Repositories |
| Ausgangszustand | `~/.local/share/obfuskation/demo/` vor Beginn gelöscht |

**Datenbestand:** `docs/beispiel/` — 120 Stammdatensätze, 120 Konten,
2000 Buchungen, alle Werte frei erfunden.

---

## 1. Automatisierte Tests

```
$ dotnet test
Bestanden!   : Fehler:     0, erfolgreich:   101, übersprungen:     0, gesamt:   101,
               Dauer: 130 ms - Obfuskation.Core.Tests.dll (net8.0)
Bestanden!   : Fehler:     0, erfolgreich:    24, übersprungen:     0, gesamt:    24,
               Dauer: 162 ms - Obfuskation.Gui.Tests.dll (net8.0)
```

**125 Tests, 0 Fehler.** Der Demo-Datenbestand verändert keinen Test.

In Durchführung 1 waren es 122. Die drei zusätzlichen Tests halten die Behebung
der Befunde D-4 und D-8 fest, siehe Abschnitt 6.

---

## 2. Positivfälle Kommandozeile

### P-01 — Regelgerüst aus einer Datei ableiten

```
$ obfuskation init --profile demo --from docs/beispiel/stammdaten.csv --config profil-demo.json
profil-demo.json angelegt (Profil 'demo').
Ersetzungstabelle: /home/gregor/.local/share/obfuskation/demo/mapping.json
10 Felder uebernommen — alle stehen auf action "error".
Jedes Feld durchgehen und bewusst entscheiden: pseudonymize, passthrough, redact oder drop.
EXIT=0
```

Alle 10 Spalten wurden übernommen, jede mit `"action": "error"` und einem
Vorschlag im Kommentar. Beobachtete Vorschläge:
Personennummer→`numericId`, Nachname→`lastName`, Vorname→`firstName`,
Straße→`street`, PLZ→`postalCode`, Ort→`city`, EMail→`email`,
Telefon→`phone`, Geburtsdatum→`dateShift`, Notiz→scanText-Hinweis.

Die Spaltennamen mit Umlaut (`Straße`) wurden korrekt gelesen.

### P-02 — Pfad der Ersetzungstabelle

```
$ obfuskation mapping path --config docs/beispiel/profil-demo.json
/home/gregor/.local/share/obfuskation/demo/mapping.json
EXIT=0
```

### P-03 — Ersetzen, drei Dateien mit demselben Profil

```
$ obfuskation obfuscate docs/beispiel/stammdaten.csv -o stammdaten.pseudo.csv --strict --config profil-demo.json
obfuscate: .../stammdaten.csv [csv]
  Zeichensatz utf-8, Trennzeichen ';'
  Datensaetze: 120
  Ersetzungen: city=120, dateShift=120, email=120, firstName=120, lastName=120,
               numericId=120, phone=120, postalCode=120, redact=108, street=120
  Tabelle: 545 neu, 545 gesamt
  Dauer: 77 ms
EXIT=0

$ obfuskation obfuscate docs/beispiel/konten.csv -o konten.pseudo.csv --strict --config profil-demo.json
  Datensaetze: 120
  Ersetzungen: belegNummer=120, bic=120, dateShift=120, iban=120, numericId=120
  Tabelle: 236 neu, 781 gesamt
  Dauer: 69 ms
EXIT=0

$ obfuskation obfuscate docs/beispiel/buchungen.csv -o buchungen.pseudo.csv --strict --config profil-demo.json
  Datensaetze: 2000
  Ersetzungen: belegNummer=2000, dateShift=2000, email=250, iban=502, numericId=2000, phone=230
  Tabelle: 656 neu, 1437 gesamt
  Dauer: 105 ms
EXIT=0
```

Zeichensatz und Trennzeichen wurden in allen drei Läufen selbständig erkannt
(`utf-8`, `;`). `redact=108` bei den Stammdaten: 12 der 120 Notizen waren leer,
leere Werte bleiben leer.

### P-04 — Verknüpfung über Dateigrenzen

Zeile 2 der Stammdaten, vorher und nachher:

```
10000;Grünwald;Dieter;Innsbrucker Ring 129;70173;Stuttgart;
      dieter.gruenwald@beispiel-firma.de;(079) 5540670;16.12.1973;Mailverkehr läuft über …
04745;Eschenbach;Monika;Am Anger 141;95661;Eisenach;
      anna.schuster398@example.invalid;(030) 1976441;01.08.1973;***
```

Personennummer in `stammdaten.pseudo.csv` Zeile 2: **04745**
Personennummer in `konten.pseudo.csv` Zeile 2: **04745** — identisch.

Beobachtete Formtreue: fünf Stellen bleiben fünf Stellen, die führende Null
inbegriffen; die Telefongliederung `(079) 5540670` bleibt `(030) 1976441`; die
E-Mail liegt unter `example.invalid`; das Geburtsdatum ist um denselben Betrag
verschoben wie alle anderen Daten.

### P-05 — Eigener Namensraum trennt gleiche Zahlen

Die Zahl **10679** kommt im Bestand zweimal vor, einmal als Belegnummer und
einmal als Personennummer:

```
als Belegnummer     -> 04063
als Personennummer  -> 86724
```

Ohne den Eintrag `"belegNummer": { "type": "numericId" }` unter `generators`
hätten beide dasselbe Pseudonym bekommen.

### P-06 — Auskunft über die Ersetzungstabelle

```
$ obfuskation mapping list --config docs/beispiel/profil-demo.json
Tabelle: /home/gregor/.local/share/obfuskation/demo/mapping.json
Profil : demo
  belegNummer: 767 Eintraege
  bic: 5 Eintraege
  city: 12 Eintraege
  email: 109 Eintraege
  firstName: 30 Eintraege
  iban: 120 Eintraege
  lastName: 23 Eintraege
  numericId: 120 Eintraege
  phone: 120 Eintraege
  postalCode: 12 Eintraege
  street: 119 Eintraege
Gesamt : 1437
EXIT=0
```

**Es wurde kein einziger Wert angezeigt, nur Zähler.**

Einschränkung auf einen Namensraum:

```
$ obfuskation mapping list --namespace iban --config docs/beispiel/profil-demo.json
  iban: 120 Eintraege
Gesamt : 1437
EXIT=0
```

### P-07 — Dateirechte der Ersetzungstabelle

```
$ stat -c '%A %n' ~/.local/share/obfuskation/demo/mapping.json
-rw------- /home/gregor/.local/share/obfuskation/demo/mapping.json
```

0600, nur für den Eigentümer lesbar.

### P-08 — Prüfen einer sauberen Datei

```
$ obfuskation scan stammdaten.pseudo.csv --config profil-demo.json
  Datensaetze: 120
  Tabelle: 0 neu, 1437 gesamt
  Dauer: 62 ms
Keine Restbestaende gefunden.
EXIT=0
```

### P-09 — Probelauf

```
$ obfuskation obfuscate docs/beispiel/konten.csv -o trocken.csv --strict --dry-run --config profil-demo.json
Probelauf: es wurde nichts geschrieben.
  Datensaetze: 120
  Ersetzungen: belegNummer=120, bic=120, dateShift=120, iban=120, numericId=120
  Tabelle: 0 neu, 1437 gesamt
EXIT=0
```

`trocken.csv` wurde **nicht** angelegt (`ls`: Datei nicht gefunden), und die
Tabelle meldet `0 neu` — der Probelauf hat nichts hinterlassen.

### P-10 — Bericht als JSON

```
$ obfuskation obfuscate docs/beispiel/konten.csv -o j.csv --strict --json --config profil-demo.json
{
  "command": "obfuscate",
  "input": ".../konten.csv",
  "output": ".../j.csv",
  "format": "csv",
  "encoding": "utf-8",
  "delimiter": ";",
  "rowsProcessed": 120,
  "ruleHits": { "belegNummer": 120, "bic": 120, "dateShift": 120, "iban": 120, "numericId": 120 },
  "fieldActions": {
    "BIC": "pseudonymize (bic)",
    "Belegnummer": "pseudonymize (belegNummer)",
    "Eröffnungsdatum": "pseudonymize (dateShift)",
    "IBAN": "pseudonymize (iban)",
    "Kontostand": "passthrough",
    "Personennummer": "pseudonymize (numericId)"
  },
  "unhandledFields": [],
  "newMappings": 0,
  "totalMappings": 1437,
  "findings": [],
  "warnings": [],
  "durationMs": 60
}
EXIT=0
```

**Der Bericht enthält Feldnamen und Zähler, aber keinen einzigen Eingabewert.**
Er darf deshalb protokolliert werden.

### P-11 — Rückholen einer Datei

```
$ obfuskation deobfuscate stammdaten.pseudo.csv -o stammdaten.zurueck.csv --config profil-demo.json
  Datensaetze: 120
  Ersetzungen: city=120, dateShift=120, email=120, firstName=120, lastName=120,
               numericId=120, phone=120, postalCode=120, street=120
  BEFUNDE: 108
    Zeile 1, Spalte Notiz: redact (nichtWiederherstellbar)
    … und 88 weitere
EXIT=0
```

Vergleich Original gegen Rückholung, Spalten 1–9:

```
$ diff <(cut -d';' -f1-9 stammdaten.csv) <(cut -d';' -f1-9 stammdaten.zurueck.csv)
Spalten 1-9 IDENTISCH
```

Spalte 10 (`Notiz`, `redact`) weicht ab und wird als Befund
`nichtWiederherstellbar` gemeldet — 108 Mal, genau so oft, wie beim Ersetzen
`redact` gezählt wurde. **Das ist kein Fehler, sondern die zugesagte
Eigenschaft von `redact`.**

### P-12 — Rückübersetzung einer KI-Antwort von der Standardeingabe

Eingabe (`docs/beispiel/antwort-der-ki.txt`, Auszug):

```
- Monika Eschenbach (Personennummer 04745),
  erreichbar unter anna.schuster398@example.invalid und (030) 1976441,
  Konto DE08659117027800930329 bei EROMDECDPFS, Stand 6836,65.
- Tim Thiel (Personennummer 38903),
  Konto DE15143437567521874410, Stand 10742,82.

auffaellig = df[df["Personennummer"].isin([04745, 38903])]

Die Anschrift „Am Anger 141, 95661 Eisenach" …
```

```
$ cat docs/beispiel/antwort-der-ki.txt | obfuskation deobfuscate --config profil-demo.json
```

Ausgabe:

```
- Dieter Grünwald (Personennummer 10000),
  erreichbar unter dieter.gruenwald@beispiel-firma.de und (079) 5540670,
  Konto DE12999999990000010000 bei MUSTDEFF001, Stand 6836,65.

auffaellig = df[df["Personennummer"].isin([10000, 10007])]

Die Anschrift „Innsbrucker Ring 129, 70173 Stuttgart" …

deobfuscate: (Standardeingabe) [text]
  Zeichensatz utf-8
  Datensaetze: 24
  Ersetzungen: freitext=16
  Tabelle: 0 neu, 1437 gesamt
  Dauer: 40 ms
EXIT=0
```

**Das ist der eigentliche Nutzen des Werkzeugs:** Namen, Kundennummern, IBAN,
BIC, E-Mail, Telefonnummer und Anschrift wurden im Fließtext *und im
Quelltextblock* zurückübersetzt. Der Betrag `6836,65` (passthrough) blieb
unverändert. Die Codeformatierung blieb unversehrt.

---

## 3. Negativfälle Kommandozeile

### N-01 — Ersetzen bei offenen Feldern

```
$ obfuskation obfuscate stammdaten.csv -o n01.csv --config <Regelgeruest aus init>
Fehler: Für folgende Felder steht noch keine Entscheidung fest: Personennummer,
Nachname, Vorname, Straße, PLZ, Ort, EMail, Telefon, Geburtsdatum, Notiz
  Fuer jedes genannte Feld in 'fields' eine action setzen: pseudonymize,
  passthrough, redact oder drop. Solange das aussteht, wird bewusst nichts geschrieben.
EXIT=3
```

**Alle zehn offenen Felder werden auf einmal gemeldet**, nicht eines nach dem
anderen. `n01.csv` wurde nicht angelegt.

### N-02 — Nachlässige Vorgabe, mit und ohne `--strict`

Profil mit `"unknownField": "passthrough"` und ohne Regel für `EMail`.

**Ohne `--strict`:**

```
  Ersetzungen: city=120, dateShift=120, firstName=120, … (kein email)
  Ohne eigene Regel: EMail
EXIT=0
$ sed -n '2p' n02.csv | cut -d';' -f7
dieter.gruenwald@beispiel-firma.de
```

**Die echte E-Mail-Adresse steht unverändert in der Ausgabe.** Der Lauf meldet
das unter „Ohne eigene Regel", bricht aber nicht ab. Genau davor warnt die
Profilprüfung mit dem Befund zu `defaults.unknownField`.

**Mit `--strict`:**

```
Fehler: Für folgende Felder steht noch keine Entscheidung fest: EMail
EXIT=3
```

`n02b.csv` wurde nicht angelegt. `--strict` übersteuert die nachlässige Vorgabe.

### N-03 — Fehlerhaftes Profil

`docs/beispiel/kaputt-profil.json` enthält absichtlich fünf Fehler.

```
$ obfuskation obfuscate stammdaten.csv -o n03.csv --config docs/beispiel/kaputt-profil.json
Fehler: Die Konfiguration ist fehlerhaft:
  textRules[3].pattern: Ungültiger regulärer Ausdruck: Invalid pattern
      '(?<![\w-/])\(?(?:\+49|0)[' at offset 25. Unterminated [] set.
  fields[2].generator: Unbekannter Generator 'familienName'. Verfügbar: bic, city,
      companyName, dateShift, email, firstName, iban, lastName, numericId,
      personName, phone, postalCode, redact, street, token
  fields[3].generator: Bei 'pseudonymize' muss ein Generator angegeben sein.
  fields[6].match: Ungültiger regulärer Ausdruck: Invalid pattern '^(Ort|Wohnort'
      at offset 13. Not enough )'s.
  fields[18].textRules: Die Textregel 'telefon' ist nicht definiert.
EXIT=2
```

Alle fünf Fehler werden mit Feldpfad genannt. Der Lauf startet gar nicht erst.

> **Befund D-1 (offen, kosmetisch):** Die Liste der fünf Befunde wird
> **zweimal** ausgegeben. Ursache: `ObfuscationEngine` (src/Obfuskation.Core/ObfuscationEngine.cs:86)
> setzt die Befunde bereits in den Meldungstext der `ConfigurationException`,
> und `CommandContext.Run` (src/Obfuskation.Cli/CommandContext.cs:103) hängt sie
> anschließend noch einmal einzeln an. Keine Auswirkung auf das Ergebnis oder den
> Rückgabewert, aber die Ausgabe ist doppelt so lang wie nötig.

### N-04 — `deobfuscate` mit dem falschen Profil

Dieselbe pseudonymisierte Datei, aber Profil `zweit` (anderer Name, andere
Tabelle):

```
$ obfuskation deobfuscate stammdaten.pseudo.csv -o n04.csv --config profil-zweit.json
  Ersetzungen: dateShift=120
  BEFUNDE: 1068
    Zeile 1, Spalte Personennummer: numericId (unbekanntesPseudonym)
    Zeile 1, Spalte Nachname: lastName (unbekanntesPseudonym)
    …
EXIT=0
```

Ergebniszeile 2, daneben zum Vergleich die pseudonymisierte Zeile und der
Echtwert:

```
pseudonymisiert : 04745;Eschenbach;Monika;Am Anger 141;95661;Eisenach;
                  anna.schuster398@example.invalid;(030) 1976441;01.08.1973;***
falsches Profil : 04745;Eschenbach;Monika;Am Anger 141;95661;Eisenach;
                  anna.schuster398@example.invalid;(030) 1976441;08.01.1974;***
Echtwert        : 10000;Grünwald;Dieter;Innsbrucker Ring 129;70173;Stuttgart;
                  dieter.gruenwald@beispiel-firma.de;(079) 5540670;16.12.1973;…
```

Zwei Beobachtungen:

1. Alle Pseudonyme bleiben stehen und werden als `unbekanntesPseudonym`
   gemeldet — 1068 Befunde. Es entstehen keine falschen Klartexte.
2. **Das Geburtsdatum ändert sich trotzdem**: aus dem Pseudonym `01.08.1973`
   wird `08.01.1974`, während der Echtwert `16.12.1973` lautet. `dateShift`
   rechnet ohne Tabelleneintrag über den Offset des jeweiligen Profils zurück;
   mit dem falschen Profil ist der Offset ein anderer und das Ergebnis ein
   plausibles, aber falsches Datum. Der Rückgabewert bleibt 0.

> **Befund D-2 (Eigenschaft, dokumentationspflichtig):** Mit dem falschen Profil
> liefert `deobfuscate` bei Datumsspalten stillschweigend falsche Werte, während
> alle anderen Spalten als Befund gemeldet werden. Wer die Befundzahl nicht
> beachtet, merkt es nicht. Gegenmittel: die Befundzahl im Bericht prüfen, sie
> muss bei richtigem Profil 0 sein (außer bei `redact`/`drop`).

### N-05 — Ersetzungstabelle im Git-Arbeitsverzeichnis

```
$ obfuskation obfuscate stammdaten.csv -o n05.csv --config profil-imrepo.json
Fehler: Der Mapping-Store soll unter /mnt/daten/Entwicklung/cs/obfuskation/mapping.json
abgelegt werden, das liegt in einem Git-Arbeitsverzeichnis. Die Datei enthält sämtliche
Echtdaten und darf dort nicht liegen. Pfad in der Konfiguration ändern oder
--allow-unsafe-store setzen.
EXIT=5
```

Es wurde **keine** `mapping.json` im Repository angelegt.

### N-06 — Zwei gleichzeitige Läufe auf dieselbe Tabelle

Erster Lauf über eine 200 000-Zeilen-Datei (Laufzeit 864 ms), zweiter Lauf nach
0,4 s gestartet:

```
Fehler: Der Mapping-Store wird bereits verwendet:
/home/gregor/.local/share/obfuskation/demo/mapping.json.lock.
Laeuft ein anderer Vorgang, oder ist eine verwaiste Sperrdatei uebrig?
EXIT=5
```

Der erste Lauf lief unbeeinträchtigt zu Ende.

### N-07 — Eingabedatei fehlt

```
$ obfuskation obfuscate gibtesnicht.csv -o n06.csv --config profil-demo.json
Fehler: Eingabedatei nicht gefunden: .../gibtesnicht.csv
EXIT=1
```

Eine Zeile, kein Stapelauszug.

### N-08 — `--json` ohne `-o`

```
$ obfuskation obfuscate stammdaten.csv --json --config profil-demo.json
Fehler: Mit --json muss die Ausgabedatei ueber -o angegeben werden, sonst
vermischen sich Bericht und Daten auf der Standardausgabe.
EXIT=1
```

### N-09 — Falsches Format erzwungen

```
$ obfuskation obfuscate stammdaten.csv -o n08.csv --format json --config profil-demo.json
Fehler: Die Eingabe ist kein gültiges JSON: 'P' is an invalid start of a value.
LineNumber: 0 | BytePositionInLine: 0.
EXIT=2
```

Das `P` ist das erste Zeichen von `Personennummer` — die Meldung zeigt genau,
woran der JSON-Leser gescheitert ist.

### N-10 — Falsches Trennzeichen erzwungen

Profil mit `"csvDelimiter": ","` auf einer Semikolon-Datei:

```
Fehler: Für folgende Felder steht noch keine Entscheidung fest:
Personennummer;Nachname;Vorname;Straße;PLZ;Ort;EMail;Telefon;Geburtsdatum;Notiz
EXIT=3
```

Die ganze Kopfzeile wurde zu **einem einzigen Feldnamen**. Der Lauf bricht ab,
statt die Datei falsch zu verarbeiten — das ist der gewünschte Ausgang, aber die
Meldung ist der einzige Hinweis auf die eigentliche Ursache. Ohne Zwang hätte
die Erkennung `;` gefunden.

### N-11 — `scan` findet stehengebliebene Echtwerte

`buchungen.csv` mit `"action": "scanText"` auf `Verwendungszweck` und den
Mustern `iban`, `email`, `phone`:

```
$ obfuskation scan buchungen.pseudo.csv --config profil-demo.json
  Ersetzungen: fund:echtwert=1112
  BEFUNDE: 1112
    Zeile 6, Spalte Verwendungszweck: numericId (echtwertAusTabelle)
    Zeile 6, Spalte Verwendungszweck: belegNummer (echtwertAusTabelle)
    Zeile 10, Spalte Verwendungszweck: lastName (echtwertAusTabelle)
    … und 1092 weitere (siehe --json)
Fehler: 1112 Verdachtsfaelle. Die Datei nicht weitergeben, bevor sie geklaert sind.
EXIT=4
```

**Das sind echte Funde, keine Fehlalarme.** Die Verwendungszwecke enthalten
Sätze wie „Überweisung an Dieter Grünwald" und „Beitrag Januar – Kundennummer
10000". Die drei Muster erkennen IBAN, E-Mail und Telefonnummer — Personennamen
und blanke Zahlen erkennen sie nicht. Genau das meint der Satz, dass Textregeln
ein Ausschlussverfahren sind.

**Behebung:** `Verwendungszweck` von `scanText` auf `redact` umgestellt
(`docs/beispiel/profil-streng.json`, gleicher Profilname und dieselbe Tabelle):

```
$ obfuskation obfuscate buchungen.csv -o buchungen.streng.csv --strict --config profil-streng.json
  Ersetzungen: belegNummer=2000, dateShift=2000, numericId=2000, redact=2000
  Tabelle: 0 neu, 1437 gesamt
EXIT=0

$ obfuskation scan buchungen.streng.csv --config profil-streng.json
  Datensaetze: 2000
Keine Restbestaende gefunden.
EXIT=0
```

`Tabelle: 0 neu` belegt nebenbei, dass der zweite Lauf dieselben Pseudonyme
verwendet hat wie der erste — die Verknüpfung zu den anderen beiden Dateien
bleibt bestehen.

### N-12 — Verdachtsfall ohne Fund

```
$ obfuskation scan konten.pseudo.csv --config profil-demo.json
  BEFUNDE: 3
    Zeile 2, Spalte Kontostand: numericId (echtwertAusTabelle)
    Zeile 2, Spalte Kontostand: belegNummer (echtwertAusTabelle)
    Zeile 43, Spalte Kontostand: belegNummer (echtwertAusTabelle)
EXIT=4
```

`Kontostand` steht auf `passthrough`. Der Wert `13535,10` enthält die
Zeichenfolge `13535`, und die ist im Bestand als Belegnummer vergeben — deshalb
schlägt die Prüfung an, obwohl kein Personenbezug entsteht.

**Die Prüfung ist bewusst übervorsichtig.** Ein Verdachtsfall ist eine Aufgabe,
kein Urteil: er ist zu klären, nicht zu ignorieren. Hier ist die Klärung, dass
`Kontostand` ein Betrag ist. Wer den Verdacht dauerhaft ausräumen will, stellt
die Spalte auf `drop`.

---

## 4. Oberfläche

**Aufnahmeverfahren.** Die Bilder entstanden auf einem virtuellen Bildschirm
(Xvfb, 1600×1000, Skalierung 1:1), nicht auf dem Arbeitsbildschirm. Grund: die
Fenstergröße ist damit fest (1040×720, die Vorgabe der Anwendung), die Bilder
sind zwischen zwei Läufen vergleichbar, und ein gesperrter oder abgeschalteter
Bildschirm hält den Lauf nicht auf — GNOME verweigert dann jede Aufnahme mit
`Screenshot is not allowed`. Der erste Anlauf auf dem echten Bildschirm
scheiterte genau daran. Das Verfahren ist in `docs/bilder/aufnehmen.sh`
festgehalten und wiederholbar.

Die Oberfläche lief mit eigenem `XDG_CONFIG_HOME`; die Datei
`~/.config/obfuskation/gui.json` des Benutzers wurde nicht angefasst.

### G-01 — Erststart ohne Profil → `gui-leer.png`

Titelzeile „kein Profil", leere Feldliste mit dem Hinweis „Noch kein Profil
geladen." und der Anleitung. `Ersetzen`, `Zurückholen` und `Prüfen` sind
abgeblendet. Statuszeile: „Bereit."

### G-02 — Regelgerüst geladen, alle Felder offen → `gui-alle-offen.png`

Aufruf: `obfuskation-gui --config profil-geruest.json stammdaten.csv`

- Kopfzeile: `stammdaten.csv`, darunter `CSV · utf-8 · ';'` — Format,
  Zeichensatz und Trennzeichen wurden selbständig erkannt.
- Alle zehn Felder tragen einen **roten offenen Kreis** und rechts das Wort
  „offen".
- Unten rechts: **„10 Felder offen"**.
- Der Regelbereich listet unter „Hinweise zur Konfiguration" alle zehn Befunde
  mit Feldpfad `fields[0].action` … `fields[9].action`.

### G-03 — Ersetzen bei offenen Feldern → `gui-abbruch-offene-felder.png`

Klick auf `Ersetzen`. Es erschien **kein Speichern-Dialog**; die Statuszeile
meldet:

> 10 Felder offen — jedes Feld braucht eine Entscheidung, bevor ersetzt werden kann.

Die Auswahl sprang auf das erste offene Feld. Es wurde nichts geschrieben.
Entspricht N-01 auf der Kommandozeile (dort Rückgabewert 3).

### G-04 — Entschiedenes Profil, Feldregel und Vorschau → `gui-feld-regel.png`

Aufruf mit `profil-demo.json`. Alle zehn Punkte sind **türkis gefüllt**, rechts
steht statt „offen" der Generatorname. Unten rechts: „alle Felder entschieden".

Feld `Nachname` gewählt: Aktion „ersetzen", Generator `lastName` („nur
Nachname"), Vorschau:

> **Grünwald → Eschenbach**

Das ist derselbe Wert, den der Lauf auf der Kommandozeile erzeugt hat (P-04).
**Die Vorschau lügt nicht.**

### G-05 — Auswahllisten → `gui-aktion-auswahl.png`, `gui-generator-auswahl.png`

Aktionen, in dieser Reihenfolge: ersetzen · durchlassen · Freitext durchsuchen ·
schwärzen · Feld entfernen · offen — Entscheidung fehlt.

Generatoren mit deutscher Erklärung: bic, city, companyName, dateShift, email,
firstName, iban, lastName, numericId, personName, phone, postalCode, redact,
street, token — und als letzter Eintrag **`belegNummer · eigener Namensraum,
wie numericId`**, also der im Profil selbst angelegte.

### G-06 — Vorschau ohne bestehende Tabelle → `gui-vorschau-beispielhaft.png`

Profil `frisch` mit einer Ablage, die es noch nicht gibt. Vorschau:

> Grünwald → **Bramkamp**
> Nur ein Beispiel — es gibt noch keine Ersetzungstabelle. Der erste echte Lauf
> legt sie an und bestimmt die endgültigen Werte.

Derselbe Klartext, ein anderes Pseudonym als in G-04 — weil das Salt flüchtig
ist. Der Hinweis sagt das ausdrücklich.

### G-07 — Ersetzen → `gui-ergebnis-ersetzen.png`

Klick auf `Ersetzen`, im Speichern-Dialog war `stammdaten.pseudo.csv`
vorbelegt — der Zusatz `.pseudo` kommt vom Programm. Die Vorschau des
gewählten Feldes zeigte `10000 → 04745`, denselben Wert wie der Lauf auf der
Kommandozeile.

Ergebniskarte:

> **Ersetzt** — 120 Datensaetze · 20 ms · 1437 in der Tabelle
> city 120 · dateShift 120 · email 120 · firstName 120 · lastName 120 ·
> numericId 120 · phone 120 · postalCode 120 · **redact 108** · street 120

**Dieselben Zähler wie der Lauf auf der Kommandozeile (P-03).**

Neben den Schaltflächen erschien der Hinweis **„↖ vor der Weitergabe prüfen"**.
Statuszeile: „Geschrieben: …/stammdaten.pseudo.csv".

### G-08 — Gleichwertigkeit Oberfläche und Kommandozeile

```
$ diff demo/stammdaten.pseudo.csv arbeit/stammdaten.pseudo.csv
GUI und CLI liefern dieselbe Datei
```

**Byteweise identisch.**

### G-09 — Prüfen ohne Befund → `gui-pruefen-sauber.png`

Die pseudonymisierte Datei geöffnet, `Prüfen` geklickt:

> **Geprueft** — 120 Datensaetze · 35 ms · 1437 in der Tabelle
> Statuszeile: „Keine Restbestände gefunden."

Der Hinweis „↖ vor der Weitergabe prüfen" verschwand.

*Nebenbeobachtung:* Wird eine bereits pseudonymisierte Datei geöffnet, zeigt die
Vorschau die Ersetzung **des Pseudonyms** (`04745 → 35394`). Das ist folgerichtig
— die Vorschau kennt den Zusammenhang nicht —, kann aber verwirren.

### G-10 — Prüfen mit Verdachtsfällen → `gui-pruefen-befund.png`

`buchungen.pseudo.csv` (Freitext mit `scanText`) geprüft:

> **Geprueft** — 2000 Datensaetze · 395 ms
> `fund:echtwert 1112`
> **Verdachtsfälle**: Zeile 6, Spalte Verwendungszweck — numericId — Echtwert
> aus der Tabelle … und 1062 weitere
> Statuszeile: „1112 Verdachtsfälle — die Datei nicht weitergeben, bevor sie
> geklärt sind."

**Dieselbe Zahl wie auf der Kommandozeile (N-11).** Die Oberfläche zeigt 50
Fundstellen einzeln, die Kommandozeile 20.

### G-11 — Zurückholen → `gui-zurueckholen.png`

`stammdaten.pseudo.csv` geöffnet, `Zurückholen` geklickt, Vorschlag
`stammdaten.pseudo.klartext.csv`:

> **Zurueckgeholt** — 120 Datensaetze · 17 ms
> city 120 · dateShift 120 · email 120 · firstName 120 · lastName 120 ·
> numericId 120 · phone 120 · postalCode 120 · street 120
> **Verdachtsfälle**: Zeile 1–9, Spalte Notiz — redact — nicht wiederherstellbar
> … und 58 weitere

Prüfung des Ergebnisses:

```
$ diff <(cut -d';' -f1-9 stammdaten.csv) <(cut -d';' -f1-9 stammdaten.pseudo.klartext.csv)
IDENTISCH
```

Spalte 10 (`Notiz`) bleibt `***` — 108 Meldungen „nicht wiederherstellbar",
so viele wie `redact` beim Ersetzen gezählt hat.

### G-12 — Fortschritt → `gui-fortschritt.png`

Datei mit 100 000 Zeilen, `Prüfen` geklickt. Während des Laufs erscheinen in der
Aktionsleiste ein Fortschrittsbalken, die Zählung **„36000 Datensätze …"** und
die Schaltfläche `Abbrechen`; die drei Vorgangsschaltflächen sind abgeblendet.
Statuszeile: „Prüfen läuft …". Der vollständige Lauf brauchte 9338 ms.

### G-13 — Abbruch → `gui-abbruch.png`

Derselbe Lauf, nach 3 s `Abbrechen` geklickt:

> Statuszeile: „Prüfen abgebrochen. Es wurde nichts geschrieben."

Es erschien keine Ergebniskarte. Die Ersetzungstabelle stand danach unverändert
bei **1437 Einträgen** (`obfuskation mapping list`) — der Abbruch hat nichts
hinterlassen.

*Nebenbeobachtung:* Wird abgebrochen, während noch die Ergebniskarte eines
früheren Laufs steht, bleibt diese stehen. Nur die Statuszeile sagt, dass der
neue Lauf abgebrochen wurde.

### G-14 — Textregeln mit Erprobungsfeld → `gui-textregeln.png`

Vier Regeln mit Priorität: iban 100 · email 90 · bic 85 · phone 80. Im
Erprobungsfeld steht ein Beispieltext; darunter **„3 Treffer"**:

| Regel | Ort | Treffer |
|---|---|---|
| iban | Zeile 1 | `DE02120300000000202051` |
| email | Zeile 2 | `max.mustermann@beispiel.de` |
| phone | Zeile 2 | `+49 30 12345678` |

Die Rechnungsnummer `2024-0815` und der Betrag `1.234,56` blieben unberührt.

### G-15 — Ungültiges Muster → `gui-textregel-fehlerhaft.png`

Muster der Regel `iban` auf `\b[A-Z]{2}\d{2}(` geändert. Statt der Trefferliste:

> Ungültiger regulärer Ausdruck in der Textregel 'iban': Invalid pattern
> '\b[A-Z]{2}\d{2}(' at offset 16. Not enough )'s.

Kein Absturz, keine leere Anzeige, sondern eine Meldung, die die Stelle nennt.

### G-16 — Zu weit gefasstes Muster → `gui-textregel-zu-weit.png`

Muster auf `\d{4}` geändert. **10 Treffer** statt 3, darunter die Bruchstücke
`0212`, `0300`, `0000`, `0020`, `2051` einer einzigen IBAN und die harmlose
Jahreszahl `2024` aus der Rechnungsnummer.

**Das ist der Zweck des Erprobungsfelds:** ein zu weit gefasstes Muster fällt
hier auf und nicht erst in den ausgelieferten Daten.

### G-17 — Ersetzungstabelle → `gui-ersetzungstabelle.png`

Warnkasten oben: „Diese Datei enthält sämtliche Echtdaten … Deshalb werden hier
nur Anzahlen gezeigt, keine Werte."

| | |
|---|---|
| Profil | demo |
| Datei | `/home/gregor/.local/share/obfuskation/demo/mapping.json` |
| Rechte | `0600 (nur für Sie lesbar)` |

Darunter die Namensräume mit Anzahlen, unten „1437 Einträge insgesamt" —
dieselben Zahlen wie `mapping list` (P-06). **Kein einziger Wert sichtbar.**

### G-18 — Über → `gui-ueber.png`

Fassung 1.0.0, „Erstellt von Gregor Stübner und Claude (Anthropic)", die fünf
Hinweise im Wortlaut und die beiden benutzten Pfade.

### G-19 — Dunkles Thema → `gui-dunkel.png`

Umschalter oben rechts, Zustand „Dunkel". Vorschau und Statuspunkte bleiben
lesbar, die Akzentfarbe wechselt von Petrol auf Hellblau.

### G-20 — Fehlerhafte Konfiguration → `gui-kaputte-konfiguration.png`

Aufruf mit `kaputt-profil.json` und `stammdaten.csv`.

> Statuszeile: „Die Konfiguration ist fehlerhaft — siehe Hinweise."

Die Kopfzeile nennt `stammdaten.csv`, die Feldliste sagt aber „Keine Datei
geöffnet." `Ersetzen` ist abgeblendet, es wurde nichts geschrieben — das
Schutzziel ist erreicht.

> **Befund D-3 (offen, Bedienbarkeit):** Die Statuszeile verweist auf Hinweise,
> **die nirgends sichtbar sind**. Der Hinweisbereich hängt am ausgewählten Feld,
> und weil die Feldliste bei fehlerhafter Konfiguration leer bleibt, lässt sich
> kein Feld auswählen. Der Anwender erfährt nur, *dass* etwas nicht stimmt,
> nicht *was*. Die Kommandozeile nennt dieselben fünf Fehler mit Feldpfad
> (N-03). Bis das behoben ist, ist der Umweg über
> `obfuskation scan <datei> --config <profil>` die schnellste Auskunft.

### G-21 — Feld mit eigenem Namensraum → `gui-fehler-generator-leer.png`

*(Beobachtung aus Durchführung 1, Fassung 1.0.0.)*

`konten.csv` geöffnet, Feld `Belegnummer` gewählt. Die Vorschau arbeitet,
**aber das Auswahlfeld „Generator" ist leer.** Schlimmer noch: in der Feldliste
steht rechts nicht mehr `belegNummer`, sondern **`?`**, und im Regelbereich
erscheint der Befund

> `fields[12].generator`  Bei 'pseudonymize' muss ein Generator angegeben sein.

**Das bloße Anklicken des Feldes hat den Generator aus der Regel entfernt.**

> **Befund D-4 (behoben in 1.0.1, war irreführend und zerstörend):** Für ein
> Feld, dessen Regel einen im Profil selbst angelegten Generator verwendet,
> zeigte die Generatorauswahl nichts an — **und die Anzeige löschte den
> Generator aus der Regel.**
>
> Ursache: `GeneratorOption.Find` suchte nur in `BuiltIn` und legte sonst ein
> neues `GeneratorOption(name, "eigener Namensraum")` an. Die Auswahlliste
> stammt aber aus `GeneratorOption.For(profile)`, wo derselbe Eintrag die
> Erklärung `"eigener Namensraum, wie numericId"` trägt. `GeneratorOption` ist
> ein `record` und vergleicht über den Wert — die beiden waren also **nicht**
> gleich. Die ComboBox fand keinen passenden Eintrag, setzte ihren
> `SelectedItem` auf `null`, und der Setter von `SelectedGenerator` schrieb
> dieses `null` brav in die Regel zurück.
>
> **Warum das gefährlich war:** Der Lauf selbst arbeitete richtig, solange
> nichts gespeichert wurde. Wer das Feld aber ansah und das Profil danach
> speicherte, hatte den Generator dauerhaft verloren; der nächste Lauf brach mit
> „Bei 'pseudonymize' muss ein Generator angegeben sein" ab. Wer stattdessen den
> vermeintlich fehlenden Generator aus der Liste ergänzte und dabei `numericId`
> statt `belegNummer` wählte, hob die Trennung der Namensräume auf — Belegnummer
> und Personennummer bekämen wieder dasselbe Pseudonym, und die Testdaten
> zeigten eine Verbindung, die es nie gab. Beides ohne Warnung.
>
> Dieselbe Ursache traf das Fenster der Textregeln
> (`TextRuleViewModel.Generator`).
>
> **Behebung siehe Abschnitt 6.**

---

## 5. Zusammenstellung der Befunde

Eingestuft nach dem, was schiefgehen kann — nicht nach dem Aufwand der
Behebung.

| Nr. | Art | Gegenstand | Ausmaß | Stand |
|---|---|---|---|---|
| D-1 | Fehler, kosmetisch | Die Befundliste eines fehlerhaften Profils wird auf der Kommandozeile doppelt ausgegeben (`ObfuscationEngine.cs:86` und `CommandContext.cs:103`) | Ausgabe doppelt so lang; Ergebnis und Rückgabewert richtig | offen |
| D-2 | Eigenschaft | `deobfuscate` mit dem falschen Profil liefert bei Datumsspalten stillschweigend falsche Werte, während alle anderen Spalten als Befund gemeldet werden | Rückgabewert bleibt 0; nur die Befundzahl verrät es | offen, dokumentiert |
| D-3 | Bedienbarkeit | Bei fehlerhafter Konfiguration verweist die Oberfläche auf Hinweise, die nicht sichtbar sind | Anwender ist auf die Kommandozeile angewiesen | offen |
| **D-4** | **Fehler, zerstörend** | **Generatorauswahl blieb bei einem eigenen Namensraum leer und löschte den Generator aus der Regel** | **Gefahr, die Namensraumtrennung unbemerkt aufzuheben oder das Profil unbrauchbar zu speichern** | **behoben in 1.0.1** |
| D-5 | Darstellung | Im Fenster „Ersetzungstabelle" überdeckt die Bildlaufleiste die rechtsbündigen Anzahlen | Zahlen teilweise angeschnitten | offen |
| D-6 | Darstellung | Im Fenster „Textregeln" wird der Hinweis „höhere Priorität gewinnt bei Überlappung" rechts abgeschnitten | Text unvollständig lesbar | offen |
| D-7 | Sprache | `--help` mischt Deutsch und Englisch (`Description:`, `Show help and usage information`) — Vorgaben von System.CommandLine | kosmetisch | offen |
| **D-8** | **Testhygiene** | **`dotnet test` überschrieb die echte `~/.config/obfuskation/gui.json` des Benutzers** | **zuletzt geöffnete Profile und gewählte Ansicht gingen verloren** | **behoben in 1.0.1** |
| D-9 | Fehler, Auslieferung | In der veröffentlichten Oberfläche schlägt „Speichern" mit `Could not load file or assembly 'System.IO.Pipelines, Version=9.0.0.0'` fehl | Profile lassen sich aus der Oberfläche nicht speichern | offen, siehe unten |

**Kein Befund betrifft die Richtigkeit der Ersetzung, die Umkehrbarkeit oder den
Schutz der Ersetzungstabelle.**

### Zu D-8, beobachtet

Vor dem Testlauf verwies `recentProfiles` auf
`/tmp/obfuskation-gui-tests/5611c308…/obfuskation.json`, danach auf
`/tmp/obfuskation-gui-tests/09e0ee64…/obfuskation.json`. Ursache:
`MainViewModelTests` legt zwar ein Wegwerfverzeichnis für Profile an, erzeugte
die Einstellungen aber mit `new GuiSettings()`; `MainViewModel` ruft an vier
Stellen `_settings.Save()` auf, und `GuiSettings.FilePath`
(src/Obfuskation.Gui/Services/GuiSettings.cs:47) löst statisch auf
`$XDG_CONFIG_HOME/obfuskation/gui.json` auf — ohne gesetzte Variable also auf
das echte Benutzerverzeichnis. Der Testlauf war damit nicht rückwirkungsfrei.
Enthält keine Datenschutzgefahr — die Datei speichert nur Pfade und die gewählte
Ansicht, keine Werte aus verarbeiteten Dateien.

### Zu D-9, beobachtet

Beim Klick auf **Speichern** in der veröffentlichten Oberfläche erscheint in der
Statuszeile:

> Could not load file or assembly 'System.IO.Pipelines, Version=9.0.0.0,
> Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. The system cannot find the
> file specified.

Das Profil wird **nicht** geschrieben; die Datei auf der Platte bleibt
unverändert. Eingegrenzt wurde:

| Weg | Speichern |
|---|---|
| `publish/linux-x64/obfuskation-gui` (Einzeldatei, framework-abhängig) | **schlägt fehl** |
| `dotnet src/Obfuskation.Gui/bin/Debug/net8.0/obfuskation-gui.dll` | funktioniert (`Gespeichert: …`) |
| `obfuskation init` (Kommandozeile, schreibt über denselben `ProfileStore`) | funktioniert |
| `ScanAndConfigTests.Profile_ueberstehen_das_Schreiben_und_Lesen_unveraendert` | besteht |

Der Fehler steckt also nicht in `ProfileStore`, sondern in der Auslieferung als
Einzeldatei. Das Bündel enthält `System.IO.Pipelines` in der Fassung 8.0.23
(über CsvHelper hereingezogen, siehe `obfuskation-gui.deps.json`). Auf diesem
Rechner ist keine .NET-8-Laufzeit installiert, `RollForward=Major` schiebt die
Anwendung auf eine neuere Laufzeit, und deren `System.Text.Json` verlangt
`System.IO.Pipelines` in der Assemblyfassung 9.0.0.0. Die mitgebündelte 8.0
verdeckt die der Laufzeit, und die Ladung scheitert.

**Folge für die Auslieferung:** Auf einem Rechner mit der in der README
geforderten .NET-8-Laufzeit tritt der Fehler nicht auf. Wer nur eine neuere
Laufzeit hat, kann aus der Oberfläche keine Profile speichern — alles andere
funktioniert. **Nicht behoben**, weil die Behebung die Art der Auslieferung
betrifft (Bündelung, Zielframework oder `--self-contained`) und damit über die
beiden beauftragten Korrekturen hinausginge.

---

## 6. Behebung der Befunde D-4 und D-8

Beauftragt am 5. September 2026, umgesetzt in Fassung **1.0.1**.

### Was geändert wurde

| Datei | Änderung |
|---|---|
| `src/Obfuskation.Gui/ViewModels/FieldRuleViewModel.cs` | `GeneratorOption.Find` nimmt jetzt das Profil entgegen und sucht in `For(profile)` statt nur in `BuiltIn`. Damit ist der gefundene Eintrag wertgleich mit dem der Auswahlliste. |
| `src/Obfuskation.Gui/ViewModels/TextRulesViewModel.cs` | `TextRuleViewModel` bekommt das Profil und reicht es an `Find` weiter — dieselbe Ursache im Fenster der Textregeln. |
| `tests/Obfuskation.Gui.Tests/TestUmgebung.cs` (neu) | Ein Modulinitialisierer setzt `XDG_CONFIG_HOME` auf ein Wegwerfverzeichnis, bevor der erste Test läuft, und räumt es beim Beenden weg. |
| `tests/Obfuskation.Gui.Tests/MainViewModelTests.cs` | Drei Regressionstests, siehe unten. |
| `Directory.Build.props` | `<Version>1.0.1</Version>`, zentral für alle Projekte. Vorher stand die Fassung nur im Projekt der Oberfläche, das Kommandozeilenprogramm lief auf der Vorgabe 1.0.0 mit. |

### Die drei neuen Tests

| Test | Hält fest |
|---|---|
| `Ein_Feld_mit_eigenem_Namensraum_zeigt_seinen_Generator_an` | Der gewählte Eintrag ist nicht nur gleichnamig, sondern **in der Auswahlliste enthalten** (`Assert.Contains`). Genau daran scheiterte es. |
| `Auch_eine_Textregel_zeigt_einen_eigenen_Namensraum_an` | Dasselbe für das Fenster der Textregeln. |
| `Der_Testlauf_fasst_die_Einstellungen_des_Anwenders_nicht_an` | `GuiSettings.FilePath` liegt im Wegwerfverzeichnis und nicht unter `~/.config`. |

### Gegenprobe: fangen die Tests den Fehler wirklich?

Die Behebung in `FieldRuleViewModel` wurde versuchsweise zurückgenommen
(`For(profile)` wieder auf `BuiltIn`) und der Testlauf wiederholt:

```
$ dotnet test tests/Obfuskation.Gui.Tests --filter "Namensraum"
   Assert.Contains() Failure: Item not found in collection
   Assert.Contains() Failure: Item not found in collection
Fehler!      : Fehler:     2, erfolgreich:     0, übersprungen:     0, gesamt:     2
```

Beide Tests schlagen ohne die Behebung fehl. Sie sind damit echte
Regressionstests und nicht bloß Beiwerk. Danach wurde die Behebung
wiederhergestellt.

### Nachweis am laufenden Programm

**D-4 vorher** (Fassung 1.0.0, eigens neu gebaut) — Auswahlfeld leer, in der
Feldliste steht `?`, und die Profilprüfung meldet den fehlenden Generator:
`gui-fehler-generator-leer.png`

**D-4 nachher** (Fassung 1.0.1) — `belegNummer · eigener Namensraum, wie
numericId` steht im Auswahlfeld, die Feldliste zeigt wieder `belegNummer`, kein
Befund: `gui-d4-behoben.png`. Aufgeklappt ist derselbe Eintrag in der Liste
markiert: `gui-d4-behoben-auswahl.png`

Die Vorschau liefert in beiden Fassungen denselben Wert — die Behebung betrifft
die Anzeige, nicht die Ersetzung.

**D-8 nachher:**

```
$ md5sum ~/.config/obfuskation/gui.json
3397e5b4ccd87659453e09ccae7aef22
$ dotnet test
Bestanden!   : … erfolgreich:   101 …
Bestanden!   : … erfolgreich:    24 …
$ md5sum ~/.config/obfuskation/gui.json
3397e5b4ccd87659453e09ccae7aef22
UNVERAENDERT
```

### Vollständige Wiederholung des Katalogs

Nach der Behebung wurde der gesamte Testfallkatalog mit Fassung 1.0.1 erneut
abgearbeitet — Durchführung 2, aus der alle Werte und Bilder dieses Protokolls
stammen. **Alle Testfälle bestanden, kein neuer Befund außer D-9**, der schon in
Durchführung 1 vorlag, aber erst beim gezielten Nachstellen von D-4 auffiel.

Ergebnisgleichheit von Oberfläche und Kommandozeile wurde erneut geprüft:

```
$ diff nachlauf/stammdaten.pseudo.csv final/stammdaten.pseudo.csv
(kein Unterschied)
```
