// Mehrere Testklassen greifen inzwischen auf denselben, ueber XDG_CONFIG_HOME
// umgeleiteten Profil-Index zu (ProfilesViewModelTests, MainViewModelTests).
// xUnit fuehrt Testklassen standardmaessig parallel aus; zeitgleiche Laeufe
// wuerden sich beim Lesen/Schreiben dieser einen Datei gegenseitig Eintraege
// unterschieben, die eigentlich schon wieder verschwunden sein sollten
// (aufgeraeumtes Wegwerfverzeichnis der jeweils anderen Testklasse). Deshalb
// hier sequenziell, wie im Core-Testprojekt aus demselben Grund (Befund D-8).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
