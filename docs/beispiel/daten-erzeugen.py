# -*- coding: utf-8 -*-
"""Erzeugt den Demo-Datenbestand fuer die Dokumentation. Alle Daten sind
frei erfunden; die IBANs liegen im Testbereich DE..9999 9999 9."""
import random, os
from datetime import date, timedelta

random.seed(20260904)
ZIEL = "/mnt/daten/Entwicklung/cs/obfuskation/docs/beispiel"

VOR_M = ["Andreas","Bernd","Christoph","Dieter","Elias","Frank","Gerd","Holger",
         "Ingo","Jens","Klaus","Lars","Martin","Norbert","Olaf","Peter"]
VOR_W = ["Anja","Birgit","Claudia","Doris","Elke","Franziska","Gisela","Heike",
         "Irene","Jutta","Katrin","Lena","Monika","Nadine","Petra","Renate"]
NACH = ["Achterberg","Baumgartner","Cordes","Dettmer","Eichhorn","Fassbender",
        "Grünwald","Hasselbach","Illigen","Jahnke","Kettenbach","Lindhorst",
        "Mühlbauer","Niederegger","Obermeier","Pflüger","Quandtner","Reinsberg",
        "Steinkamp","Thelen","Uhlenbrock","Vollmering","Wachsmuth","Zangerle"]
STRASSEN = ["Ahornweg","Birkenallee","Cranachstraße","Dorfanger","Erlenkamp",
            "Feldmühlenweg","Gartenstraße","Hafenstraße","Innsbrucker Ring",
            "Jahnplatz","Kirchsteig","Lindenhof","Mühlenkamp","Nordring",
            "Osterbrooksweg","Parkstraße","Rosenweg","Schulstraße","Talblick",
            "Uferpromenade","Vogelsang","Wiesengrund"]
ORTE = [("22765","Hamburg"),("28195","Bremen"),("30159","Hannover"),
        ("40213","Düsseldorf"),("50667","Köln"),("60311","Frankfurt am Main"),
        ("70173","Stuttgart"),("80331","München"),("04109","Leipzig"),
        ("01067","Dresden"),("24103","Kiel"),("39104","Magdeburg")]
BICS = ["TESTDEFFXXX","MUSTDEFF001","DEMODEFFXXX","BEISDEFF100","PROBDEFFXXX"]
NOTIZ = [
    "Stammkunde seit 1999",
    "Rückfragen bitte an die Zentrale",
    "Zweitanschrift bekannt, siehe Vorgang 2024-0815",
    "erreichbar unter 030/1234-5678",
    "Mailverkehr läuft über k.lindhorst@beispiel-firma.de",
    "",
    "Konto wird zum Jahresende geschlossen",
    "SEPA-Mandat liegt vor",
]

UMLAUT = str.maketrans({"ä":"ae","ö":"oe","ü":"ue","ß":"ss","Ä":"ae","Ö":"oe","Ü":"ue"})

def entumlaute(w):
    """E-Mail-Ortsteile tragen keine Umlaute."""
    return w.lower().translate(UMLAUT)

def iban(n):
    """Test-IBAN im reservierten Bereich; Pruefziffer wird nicht berechnet,
    das Werkzeug rechnet sie fuer die Pseudonyme selbst."""
    return "DE%02d99999999%010d" % (n % 90 + 2, n)

personen = []
for i in range(120):
    pnr = 10000 + i * 7
    if i % 2:
        vor = random.choice(VOR_W)
    else:
        vor = random.choice(VOR_M)
    nach = random.choice(NACH)
    plz, ort = random.choice(ORTE)
    personen.append({
        "pnr": pnr,
        "vor": vor,
        "nach": nach,
        "strasse": "%s %d" % (random.choice(STRASSEN), random.randint(1, 148)),
        "plz": plz, "ort": ort,
        "mail": "%s.%s@beispiel-firma.de" % (entumlaute(vor), entumlaute(nach)),
        "tel": random.choice(["+49 %d %d" % (random.randint(30,89), random.randint(1000000,99999999)),
                              "0%d/%d-%d" % (random.randint(200,899), random.randint(100000,999999), random.randint(10,99)),
                              "(0%d) %d" % (random.randint(30,89), random.randint(1000000,9999999))]),
        "geb": date(random.randint(1950, 2001), random.randint(1,12), random.randint(1,28)),
        "notiz": random.choice(NOTIZ),
        "iban": iban(pnr),
        "bic": random.choice(BICS),
    })

def schreib(name, kopf, zeilen):
    pfad = os.path.join(ZIEL, name)
    with open(pfad, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(kopf + "\n")
        for z in zeilen:
            f.write(z + "\n")
    print(name, len(zeilen), "Zeilen")

def feld(w):
    w = str(w)
    return '"%s"' % w.replace('"', '""') if (";" in w or '"' in w) else w

# --- stammdaten.csv --------------------------------------------------------
schreib("stammdaten.csv",
    "Personennummer;Nachname;Vorname;Straße;PLZ;Ort;EMail;Telefon;Geburtsdatum;Notiz",
    [";".join(feld(x) for x in (p["pnr"], p["nach"], p["vor"], p["strasse"], p["plz"],
                                p["ort"], p["mail"], p["tel"],
                                p["geb"].strftime("%d.%m.%Y"), p["notiz"]))
     for p in personen])

# --- konten.csv ------------------------------------------------------------
# Die Belegnummern liegen absichtlich im selben Zahlenraum wie die
# Personennummern: ohne eigenen Namensraum bekaemen sie dasselbe Pseudonym.
konten = []
for p in personen:
    konten.append(";".join(feld(x) for x in (
        p["pnr"], p["iban"], p["bic"],
        10000 + random.randint(0, 833) * 7,
        ("%.2f" % random.uniform(-2500, 48000)).replace(".", ","),
        (p["geb"] + timedelta(days=random.randint(7000, 15000))).strftime("%d.%m.%Y"))))
schreib("konten.csv",
    "Personennummer;IBAN;BIC;Belegnummer;Kontostand;Eröffnungsdatum", konten)

# --- buchungen.csv ---------------------------------------------------------
ZWECKE = [
    "Miete {monat}; Konto {iban}",
    "Rückzahlung an {mail}",
    "Rechnung 2024-{nr:04d}, Rückfragen unter {tel}",
    "Dauerauftrag {monat}",
    "Gutschrift Vorgang {nr:04d}",
    "Lastschrift {iban}",
    "Überweisung an {vor} {nach}",
    "Beitrag {monat} – Kundennummer {pnr}",
]
MONATE = ["Januar","Februar","März","April","Mai","Juni","Juli","August",
          "September","Oktober","November","Dezember"]
buch = []
for i in range(2000):
    p = random.choice(personen)
    z = random.choice(ZWECKE).format(
        monat=random.choice(MONATE), iban=p["iban"], mail=p["mail"],
        tel=p["tel"], nr=random.randint(1, 9999), vor=p["vor"], nach=p["nach"],
        pnr=p["pnr"])
    buch.append(";".join(feld(x) for x in (
        10000 + random.randint(0, 833) * 7, p["pnr"],
        (date(2025, 1, 1) + timedelta(days=random.randint(0, 364))).strftime("%d.%m.%Y"),
        ("%.2f" % random.uniform(-3000, 9000)).replace(".", ","), z)))
schreib("buchungen.csv",
    "Belegnummer;Personennummer;Buchungsdatum;Betrag;Verwendungszweck", buch)
