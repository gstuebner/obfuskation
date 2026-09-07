// PathHelperTests muss XDG_CONFIG_HOME fuer einen Fall kurzzeitig loeschen, um
// den Rueckfall auf ~/.config/obfuskation zu pruefen. xUnit fuehrt Testklassen
// standardmaessig parallel aus; ein zeitgleich laufender Test wuerde in genau
// diesem Moment in die echte Konfiguration des Anwenders schreiben (Befund
// D-8). Deshalb hier sequenziell.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
