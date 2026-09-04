# Einbinden ins Anwendungsmenü (Linux)

Die `.desktop`-Datei sorgt dafür, dass die Oberfläche im GNOME-Anwendungsmenü
erscheint und CSV- oder JSON-Dateien sich über „Öffnen mit“ an sie übergeben
lassen.

Für den angemeldeten Benutzer:

```fish
# Programm dorthin, wo es im Suchpfad liegt
install -Dm755 publish/linux-x64/obfuskation-gui ~/.local/bin/obfuskation-gui
install -Dm755 publish/linux-x64/obfuskation     ~/.local/bin/obfuskation

# Symbol und Menüeintrag
install -Dm644 src/Obfuskation.Gui/Assets/obfuskation.png \
    ~/.local/share/icons/hicolor/256x256/apps/obfuskation.png
install -Dm644 packaging/obfuskation.desktop \
    ~/.local/share/applications/obfuskation.desktop

update-desktop-database ~/.local/share/applications
gtk-update-icon-cache -f -t ~/.local/share/icons/hicolor 2>/dev/null; true
```

Entfernen: dieselben vier Dateien löschen und die beiden `update`-Befehle
erneut ausführen.
