# Feature-Sammlung: Anwenderfreundlichkeit

Fassung 1.9.0 · Stand 24. September 2026

Arbeitsdokument für die nächste größere Version. Ziel ist ein echter Schub
an Anwenderfreundlichkeit: Obfuskation soll sich **selbst erklären**, für
Anfänger wie für Profis. Wir gehen in drei Schritten vor:

1. **Ideen sammeln.** Jetzt. Dieses Dokument darf wachsen, auch durch
   Durchsichten anderer Modelle (siehe Abschnitt 5). Ideen werden hier noch
   nicht verworfen, nur ergänzt und kommentiert.
2. **Neu bewerten.** Nutzen für Anfänger und Profi, Aufwand, Risiko (Abschnitt 4).
3. **Umsetzen.** Eine Version mit den Ideen, die den größten Schub bringen.

## 1. Für wen

| | Anfänger | Profi |
|---|---|---|
| Wer | Sachbearbeitung, Fachabteilung. Will eine Tabelle oder einen Text an eine KI geben, ohne Echtdaten preiszugeben. | Entwicklung, IT, Datenschutz. Pflegt hauseigene Muster, nutzt auch die Kommandozeile. |
| Weiß | was vertraulich ist | was ein regulärer Ausdruck ist, wie JSON aussieht |
| Weiß nicht | was ein Generator, Namensraum oder RegEx ist, was „Priorität“ hier bewirkt | wo in der Oberfläche was steckt, wenn es nicht dokumentiert ist |
| Braucht | geführte Wege, Klartext, Sicherheit, dass nichts durchrutscht | Überblick, Tempo, volle Kontrolle, Nachvollziehbarkeit |

## 2. Leitlinien (Vorschlag, zur Diskussion)

1. **Selbsterklärend.** Jede Option erklärt sich dort, wo sie steht, in
   sichtbarem Text und nicht nur im Tooltip. Was sich nicht in einem Satz
   erklären lässt, gehört hinter einen klar benannten Profi-Bereich.
2. **Überblick vor Detail.** Wer eine Option anbietet, zeigt auch, was sie
   bewirkt. Beispiel: Eine Priorität ohne Übersicht, wo sich Regeln
   überschneiden, ist eine Zahl ohne Bedeutung.
3. **Ein Wort für eine Sache**, gleich in Oberfläche, Doku und Kommandozeile.
4. **Sicher als Voreinstellung.** Eine falsche oder ausgelassene Wahl darf
   keine Echtdaten durchlassen. Das ist heute schon Grundsatz („offen —
   Entscheidung fehlt“ bricht ab) und soll es bleiben.
5. **Stufenweise Tiefe, sichtbar gekennzeichnet.** Einfache Wege vorn, Profi-
   Wege erreichbar und als solche benannt, nichts überraschend Verstecktes.

## 3. Ist-Zustand (1.9.0)

### 3.1 Wege durch das Programm

- **Startseite:** drei Kacheln („Text säubern“, „Dateien pseudonymisieren“,
  „Antwort zurückholen“), dazu das zuletzt benutzte Projekt und „Kurzhilfe…“.
- **Kopfzeile:** ← Start, Profilname mit ⓘ, „Neu aus Datei…“, „Profile…“,
  „⚙ Einstellungen“ (Strg+,), „Speichern“, „Mehr ▾“ (Ersetzungstabelle…,
  Einstellungen…, Kurzhilfe…, Über Obfuskation…), Farbthema.
- **Textansicht:**
  - Richtung: „Text säubern“ oder „Antwort zurückübersetzen“.
  - Links der Text als farbige Prüffassung („Bearbeiten“/„Fertig“), rechts
    das Ergebnis.
  - Fundliste mit Häkchen je Fund, „immer…“, „Regel entfernen“,
    „bearbeiten…“ und „Regeln bearbeiten…“.
  - Markieren und „immer ersetzen…“ (Strg+M).
- **Dateiansicht:**
  - Datei mit „Zuletzt ▾“ und „Öffnen…“.
  - Links die Feldliste (gefüllter oder offener Punkt).
  - Rechts die Regelkarte: Feldinhalt und Vorschau, Aktion, Generator,
    „Optionen…“, Kennzeichnung.
  - „Immer ersetzen…“ und „Felder automatisch erkennen…“.
  - Unten: „Pseudodatei erzeugen…“, „Klartextdatei erzeugen…“, „Prüfen“ und
    „Alle ▾“.
- **Einstellungen:** vier Reiter.
  - Textregeln: Liste plus Formular mit Bezeichnung, „Was wird gesucht?“,
    „Ersetzen durch“, „Gilt für“, „Erweitert“ (Priorität, Groß/klein) und
    Erprobung.
  - Eigene Generatoren.
  - Spalten-Vorschläge.
  - Ablageort.
- **Dialoge:** Immer ersetzen, Profile (Suchen, Öffnen, Umbenennen, Aus
  Liste entfernen, Löschen), Neues Profil, Profil umbenennen,
  Ersetzungstabelle (Werte verdeckt, Suchen, Löschen), Generator-Optionen,
  Muster erkennen, Rückfragen, Über, Kurzhilfe.

### 3.2 Stellschrauben

| Stellschraube | Wo | Erklärt? |
|---|---|---|
| Aktion je Feld (ersetzen, durchlassen, Freitext durchsuchen, schwärzen, Feld entfernen, offen) | Dateiansicht | ja, Satz unter der Auswahl |
| Generator je Feld (20 eingebaute + eigene) | Dateiansicht | Kurzbeschreibung, aber technischer Name vorn (`numericId`, `token`, `dateShift`) |
| Generator-Optionen (Kennzeichnung, Platzhalter, Zeitraum, Rundungsstufe, Zeichenmaske, Werteliste, Teilmaskierung) | „Optionen…“ | teilweise |
| Textregel: Bezeichnung, Muster in 3 Modi, Ersetzen durch, Gilt für | Einstellungen | ja |
| Textregel: **Priorität** (0–1000), Groß/klein | Einstellungen → „Erweitert“ | ein Satz, **keine Übersicht der Überschneidungen** |
| Eigene Generatoren: Name, Präfix, Entfernen | Einstellungen | Einleitungssatz |
| Spalten-Vorschläge: Muster, Generator, Groß/klein, Kommentar, Reihenfolge | Einstellungen | Einleitungssatz, ?-Hilfe |
| Regelauswahl je Freitextfeld (`fields[].textRules`) | **nur in der JSON-Datei** | – |
| Profilvorgaben: unbekannte Felder, Schwärzungsplatzhalter, Leerwerte | **nur in der JSON-Datei** | – |
| Eingabe: CSV-Trennzeichen, Kodierung, Kopfzeile | **nur in der JSON-Datei** (sonst automatisch erkannt) | – |
| Feldzuordnung: exakter Name oder Muster (`matchType`) | **nur in der JSON-Datei** | – |
| Ersetzungstabelle: anzeigen, suchen, Einträge oder Namensräume löschen | Mehr → Ersetzungstabelle… | ja, mit Warnhinweis |

### 3.3 Begriffe, die ein Anwender heute lernen muss

| Begriff | Problem |
|---|---|
| **Profil** und **Projekt** | zwei Wörter für dieselbe Sache: „Profile…“ in der Kopfzeile, „Dieses Projekt“ in den Einstellungen |
| **Kennzeichnung** und **Präfix** | zwei Wörter für dieselbe Sache (Dateiansicht und Einstellungen) |
| **Generator** | technischer Begriff, und die Namen sind englische Kennungen |
| **Namensraum** | nur in der Ersetzungstabelle sichtbar, nirgends erklärt |
| **Textregel**, **Muster**, **Ausdruck**, **RegEx** | vier Wörter im Umfeld derselben Sache |
| **Spalten-Vorschläge**, **Spaltenmuster**, `fieldRules` | Oberfläche, Doku und Datei benennen es verschieden |
| **Erweiterungsdatei**, **hauseigen**, **alle Projekte**, **global** | vier Wörter für denselben Bereich |
| **Pseudonym**, **Pseudodatei**, **Klartextdatei** | verständlich, aber Fachsprache |
| **Freitext durchsuchen** vs. Textregeln | Zusammenhang nicht offensichtlich: nur dort greifen die Textregeln in Dateien |
| **Priorität** | ohne Überblick bedeutungslos (siehe 3.4) |

### 3.4 Beobachtete Stolpersteine

Aus Gregors Test und dem Review vom 23. und 24. September 2026:

1. **Priorität taucht unter „Erweitert“ unvermittelt auf.** Es gibt keine
   Stelle, die zeigt, welche Regeln sich überschneiden und welche dann
   gewinnt. „Verwirrend“, und bei den vielen Optionen verliert man den
   Überblick.
2. **Die mitgelieferten Regeln sind rohe Ausdrücke.** `iban`, `email`, `bic`
   und `phone` werden als Regex in jedes neue Profil kopiert. In den
   Einstellungen ist das Erste, was ein Anfänger sieht, deshalb viermal
   „Eigener Ausdruck (für Profis)“ mit Regex-Zeichensalat.
3. **Die eingebaute Erkennung ist schmal.** In Gregors Testtext standen
   Steuer-ID, Kreditkartennummer, Server-IP, Admin-Benutzer, Kennwort und
   API-Token (`ghp_…`). Keines davon erkennt das Programm von sich aus.
4. **Bezeichnung „begriff“ schien nicht änderbar.** Behoben in 1.9.0: Das
   Feld steht jetzt oben, auch im Dialog „Immer ersetzen“.
5. **Kein Gesamtbild.** Was passiert mit dieser Datei oder diesem Text,
   welche Regel greift wo, was bleibt stehen? Es gibt Einzelansichten
   (Fundliste, Feldvorschau, Erprobung), aber keine Übersicht.
6. **Wichtiges steht nur im Tooltip.** Beispiele: Sperrgründe, der Hinweis
   zu anderen Projekten beim Umbenennen, Knopferklärungen. Tooltips werden
   übersehen und fehlen auf Touch-Geräten.
7. **Die Tragweite globaler Regeln ist versteckt.** Wer sie ändern darf und
   wen eine Änderung betrifft, steht erst unter „Ablageort“, und dort als
   Dateipfad.
8. **Einiges ist nur in der JSON-Datei einstellbar** (siehe 3.2). Ein
   Anfänger kommt dort nie an.
9. **Layoutfehler fielen nur beim Messen auf:** abgeschnittenes Formular,
   abgeschnittene Klapptafel, abgeschnittene Knöpfe. Es gibt keine
   automatische Prüfung der Oberfläche auf Abschneiden.
10. **Teil der Fachsprache sind Programmkennungen** (`token`, `numericId`,
    `scanText`, `dateShift`), auch in Listen, die Anfänger sehen
    („→ token“).

## 4. Ideen

Format: **Kennung · Titel.** Beschreibung. *Nutzen:* A = Anfänger,
P = Profi. *Aufwand:* S/M/L, grob geschätzt. Offene Fragen stehen kursiv.

### A · Überblick und Nachvollziehbarkeit

- **A1 · „Was passiert hier?“: eine Wirkungsansicht.** Für den aktuellen
  Text oder die aktuelle Datei eine Liste jedes Treffers mit Regel,
  Herkunft (Projekt, alle Projekte, eingebaut) und Ersatz. Bei
  Überschneidungen sichtbar: „verdeckt von iban“. Macht Priorität erst
  verständlich. *A+P · M*
- **A2 · Priorität ersetzen statt erklären.** Vorschlag: Reihenfolge per
  Ziehen („bei Überschneidung gewinnt die obere Regel“), oder automatisch
  „der längere Treffer gewinnt“. Nur wenn sich zwei Regeln wirklich
  überschneiden, fragt das Programm nach („FW-Nummer und Inventar treffen
  beide ‚FW123‘ – welche soll gewinnen?“). *A+P · M. Offene Frage: Wie
  sieht die Umstellung für bestehende Dateien mit `priority` aus?*
- **A3 · Regelübersicht als Tabelle.** Alle Regeln auf einen Blick mit den
  Spalten Bezeichnung, findet (Klartext), ersetzt durch, gilt für, Treffer
  im aktuellen Text und ob sie überschrieben ist. Sortier- und filterbar.
  *A+P · M*
- **A4 · Überschneidungen in der Erprobung markieren.** Zwei Regeln auf
  derselben Stelle werden farbig markiert, mit Hinweis, welche gewinnt.
  *P · S*
- **A5 · Projekt-Steckbrief.** Eine Seite je Projekt mit Dateien, Feldern
  und Behandlung, Regeln, Tabellengröße und offenen Punkten. Druck- oder
  exportierbar, auch als Nachweis für den Datenschutz. *A+P · M*
- **A6 · Vorher/Nachher für die ganze Datei.** Die Dateiansicht zeigt eine
  Handvoll Zeilen als Tabelle, links Original, rechts Pseudo, nicht nur
  Feld für Feld. *A · M*

### B · Sprache und Begriffe

- **B1 · Glossar festlegen, dann überall durchziehen.** Ein Wort je Sache:
  Profil oder Projekt, Kennzeichnung oder Präfix, Regel oder Muster, „alle
  Projekte“ oder hauseigen. Gilt für Oberfläche, Doku und Meldungen der
  Kommandozeile. *A · S–M (viele Stellen)*
- **B2 · „Ersetzen durch“ in Anwendersprache.** Etwa „erfundener Name“,
  „erfundene IBAN (gültig)“, „Kennung mit Präfix“, „Datum verschoben“,
  „Sternchen (nicht umkehrbar)“. Die technische Kennung erscheint nur klein
  oder im Profi-Bereich. Die Listenzeile wird zu „→ Kennung“ statt
  „→ token“. *A · S*
- **B3 · Fachbegriffe klickbar erklären.** Ein ⓘ an jedem Fachbegriff
  öffnet per Klick (nicht per Hover) eine kurze Erklärung. Dazu ein Glossar
  in der Kurzhilfe. *A · S*
- **B4 · Pflichtwissen aus Tooltips holen.** Sperrgründe, Warnungen und
  Tragweite stehen als sichtbarer Text; Tooltips nur noch für Zusätze.
  *A · S*

### C · Geführte Wege

- **C1 · Erster Start mit Rundgang.** Ein mitgeliefertes Beispiel in drei
  Schritten durchspielen: Text säubern, Datei pseudonymisieren, Antwort
  zurückholen. Überspringbar und später wieder aufrufbar. *A · M*
- **C2 · Regel aus mehreren Beispielen.** Wer „4532 7511 8920 4311“ und
  „4532751189204311“ angibt, bekommt eine Form mit und ohne Leerzeichen.
  Dazu Gegenbeispiele („soll nicht treffen“). *A+P · M*
- **C3 · Vor der Weitergabe: Ampel statt Knopf.** „Prüfen“ als sichtbarer
  Schritt mit Ergebnis: grün (nichts gefunden), gelb (Verdachtsfälle), rot
  (offene Felder). *A · S–M*
- **C4 · Leerzustände erklären.** Eine leere Regelliste sagt, wie man eine
  Regel anlegt und wozu. Das gilt ebenso für leere Generator- und
  Spaltenlisten. *A · S*
- **C5 · Was heute nur in der JSON-Datei geht, in die Oberfläche holen.**
  Regelauswahl je Freitextfeld, Umgang mit unbekannten Feldern,
  Schwärzungsplatzhalter, Leerwerte, CSV-Einstellungen. Vorzugsweise
  geführt, zum Beispiel bei der Frage „Was soll mit neuen Spalten passieren,
  die das Profil nicht kennt?“. *A+P · M*

### D · Erkennung

- **D1 · Eingebaute Erkenner statt kopierter Regex.** IBAN, E-Mail, BIC
  und Telefon erscheinen als „Eingebaut: IBAN“ mit Ein/Aus-Schalter. Der
  Ausdruck ist nur für Profis einsehbar. Neue Profile bekommen keine Kopie
  mehr, Verbesserungen erreichen damit auch alte Profile. *A+P · M.
  Offene Frage: Wie werden bestehende Profile mit kopierten Regeln
  umgestellt?*
- **D2 · Mehr eingebaute Erkenner.** Kreditkarte mit Luhn-Prüfung,
  Steuer-ID, Sozialversicherungsnummer, IPv4/IPv6, Hostnamen, Kfz-Kennzeichen
  und Geburtsdaten im Text. Dazu Zugangsdaten: Kennwort nach
  „Kennwort:“/„Passwort:“, API-Token-Formate (`ghp_`, `sk-`, `AKIA…`),
  Benutzerkennungen nach „User:“. Belegt durch Gregors Testtext. *A · M–L*
- **D3 · Prüfziffern gegen Fehltreffer.** IBAN mod 97, Luhn, Steuer-ID.
  Weniger falscher Alarm, mehr Vertrauen in die Farben. *A+P · S–M*
- **D4 · Tabellenwerte im Freitext wiederfinden.** Namen und Orte, die aus
  Spalten schon in der Ersetzungstabelle stehen, werden auch in
  Freitextfeldern und in der Textansicht ersetzt. *Zuerst prüfen, was davon
  schon besteht.* *A · M*
- **D5 · Hinweis auf nicht Erkennbares.** Namen und Anschriften im
  Fließtext erkennt kein Muster. Die Oberfläche sagt das offen und bietet
  das Markieren an, statt Sicherheit vorzutäuschen. *A · S*

### E · Regeln für alle Projekte, Rollen

- **E1 · Rolle statt Dateipfad.** „Verwaltet von der IT, hier nur lesbar“
  statt `/opt/…/obfuskation.json`. Der Pfad bleibt für Profis unter
  Ablageort. *A · S*
- **E2 · „Für alle vorschlagen“.** Wer keine Schreibrechte hat, schickt
  eine Regel als Vorschlag an die Verwalter, als JSON-Schnipsel oder
  Mailtext. *A+P · S*
- **E3 · Schichten: Organisation → persönlich → Projekt.** Bisher gilt nur
  die zuerst gefundene Datei ganz. Schichten erlaubten Hausregeln plus
  eigene Ergänzungen. *P · L. Bewusst abzuwägen: mehr Macht, mehr zu
  erklären.*
- **E4 · Regeln teilen.** Export und Import einzelner Regeln oder Pakete
  (etwa „Bankdaten“, „IT-Zugangsdaten“). *P · M*

### F · Für Profis

- **F1 · Tastatur.** Strg+N für eine neue Regel, Entf zum Löschen, Suche
  in der Regelliste, Strg+Enter für „Übernehmen“. *P · S*
- **F2 · Testfälle je Regel.** „Soll treffen“ und „soll nicht treffen“
  werden mit der Regel gespeichert und bei jeder Änderung geprüft. Das
  schützt vor Rückschritten. *P · M*
- **F3 · JSON-Ansicht in der App** mit Validierung und Sprung zur
  fehlerhaften Stelle. *P · M*
- **F4 · Regelverwaltung auf der Kommandozeile.** `rules list`,
  `rules add`, `rules test` und die Gegenstücke für die Erweiterungsdatei.
  *P · M*
- **F5 · Änderungsprotokoll der Erweiterungsdatei** (wer, wann, was), für
  Verwalter. *P · M*

### G · Vertrauen und Sicherheit in der Bedienung

- **G1 · Restrisiko sichtbar.** Das Ergebnis nennt, was bewusst behalten
  wurde (abgewählte Funde) und wofür es kein Muster gibt. Kopieren mit
  abgewählten Funden fragt einmal nach. *A · S*
- **G2 · Rückgängig** für Regeländerungen und Einstellungen, oder
  Versionen der Erweiterungsdatei statt nur einer `.bak`. *A+P · M*

### H · Oberfläche und Qualität

- **H1 · Layout-Regressionstest.** Screenshots aller Fenster in Mindest-
  und Standardgröße (Xvfb wie `docs/bilder/aufnehmen.sh`) mit automatischer
  Prüfung auf Abschneiden. Hätte alle drei Layoutfehler aus 1.8.0 gefunden.
  *Qualität · M*
- **H2 · Einheitliche Knopfreihenfolge und -sprache** in allen Dialogen:
  Verb plus Objekt, „Abbrechen“ immer an derselben Stelle. *A · S*
- **H3 · Barrierefreiheit.** Tastaturbedienung, Kontraste in beiden
  Themen, Schriftgröße, Bildschirmleser-Namen (AutomationProperties).
  *A · M*

## 5. Durchsicht durch andere Modelle

Gedacht zum Beispiel für Fable. Das Modell bekommt dieses Dokument und
diesen Auftrag:

> Du prüfst die Anwenderfreundlichkeit der Desktop-Anwendung
> „Obfuskation“ (C#/Avalonia). Sie pseudonymisiert Echtdaten in Texten und
> Tabellen, bevor sie an eine KI gehen, und führt die Antwort wieder auf die
> Echtwerte zurück. Lies `docs/Feature.md` (Ist-Zustand und bisherige
> Ideen), `README.md` und `docs/anwenderdokumentation.md`. Die Bilder in
> `docs/bilder/` sind teilweise veraltet.
>
> Liefere: (1) Stolpersteine, die im Ist-Zustand fehlen; (2) neue Ideen
> im Format der Liste in Abschnitt 4 mit Nutzen für Anfänger und Profi;
> (3) Widerspruch zu Ideen, die du für falsch oder schädlich hältst, mit
> Begründung; (4) deine fünf wichtigsten Maßnahmen für einen echten Schub
> an Anwenderfreundlichkeit. Schreibe keinen Code.

Ergebnisse von Durchsichten kommen als eigener Unterabschnitt hierher,
mit Modell und Datum, damit die Herkunft erkennbar bleibt.

## 6. Bewertung (Schritt 2, noch offen)

| Idee | Nutzen A | Nutzen P | Aufwand | Risiko | Entscheidung |
|---|---|---|---|---|---|
| | | | | | |
