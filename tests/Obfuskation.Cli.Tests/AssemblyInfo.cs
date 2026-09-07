// CommandContextTests setzt fuer einen Fall kurzzeitig das aktuelle
// Arbeitsverzeichnis (Directory.SetCurrentDirectory), ein Prozesszustand ohne
// Testisolierung. xUnit fuehrt Testklassen standardmaessig parallel aus, wie
// in den anderen beiden Testprojekten aus demselben Grund (Befund D-8)
// deshalb hier sequenziell.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
