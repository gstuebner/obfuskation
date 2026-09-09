using System.CommandLine;
using Obfuskation.Cli;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

// Gemeinsame Optionen. Sie werden mehreren Unterbefehlen zugeordnet, damit
// ueberall dieselben Namen gelten.
var configOption = new Option<string?>("--config", "-c")
{
    Description = "Pfad zur Konfigurationsdatei (Vorgabe: obfuskation-projekt.json, aufwaerts gesucht)",
};

var outputOption = new Option<string?>("--output", "-o")
{
    Description = "Ausgabedatei (ohne Angabe: Standardausgabe)",
};

var jsonOption = new Option<bool>("--json")
{
    Description = "Bericht als JSON auf die Standardausgabe; erfordert -o",
};

var strictOption = new Option<bool>("--strict")
{
    Description = "Felder ohne eigene Regel fuehren zum Abbruch",
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Nichts schreiben, nur berichten, was geschehen wuerde",
};

var formatOption = new Option<DataFormat>("--format")
{
    Description = "Dateiformat erzwingen statt aus der Endung zu bestimmen",
    DefaultValueFactory = _ => DataFormat.Auto,
};

var allowUnsafeStoreOption = new Option<bool>("--allow-unsafe-store")
{
    Description = "Ersetzungstabelle auch in einem Git-Arbeitsverzeichnis ablegen (nicht empfohlen)",
};

var noExtensionsOption = new Option<bool>("--no-extensions")
{
    Description = "Ohne die Erweiterungsdatei arbeiten (siehe 'obfuskation extensions path'); " +
                  "fuer Fehlersuche und reproduzierbare Laeufe",
    Recursive = true,
};

var rootCommand = new RootCommand(
    "Tauscht Echtdaten in CSV-, JSON- und Textdateien gegen Pseudodaten aus und kann " +
    "den Austausch wieder rueckgaengig machen." + Environment.NewLine +
    Environment.NewLine +
    "Erstellt von Gregor Stuebner und Claude (Anthropic).");

rootCommand.Subcommands.Add(BuildInitCommand());
rootCommand.Subcommands.Add(BuildObfuscateCommand());
rootCommand.Subcommands.Add(BuildDeobfuscateCommand());
rootCommand.Subcommands.Add(BuildScanCommand());
rootCommand.Subcommands.Add(BuildMappingCommand());
rootCommand.Subcommands.Add(BuildProfileCommand());
rootCommand.Subcommands.Add(BuildExtensionsCommand());

// Recursive gilt fuer alle Unterbefehle, ohne dass jeder sie einzeln
// eintragen muss — siehe System.CommandLine, Option.Recursive.
rootCommand.Options.Add(noExtensionsOption);

// Fassung und Ersteller stehen unter jeder Hilfe, wie in allen Programmen.
ProgramInfo.AddHelpFooter(rootCommand);

return rootCommand.Parse(args).Invoke();

Command BuildInitCommand()
{
    var profileNameOption = new Option<string>("--profile", "-p")
    {
        Description = "Name des Profils; bestimmt auch die Ablage der Ersetzungstabelle",
        DefaultValueFactory = _ => "default",
    };

    var fromOption = new Option<string?>("--from")
    {
        Description = "Beispieldatei, aus der die Feldnamen uebernommen werden",
    };

    var descriptionOption = new Option<string?>("--description")
    {
        Description = "Freitext, wofuer dieses Profil da ist",
    };

    var centralOption = new Option<bool>("--central")
    {
        Description = "Im zentralen Profilordner ablegen (~/.config/obfuskation/profile/<name>.json) " +
                      "statt als obfuskation-projekt.json im aktuellen Verzeichnis",
    };

    var forceOption = new Option<bool>("--force")
    {
        Description = "Vorhandene Konfigurationsdatei ueberschreiben",
    };

    var command = new Command("init", "Legt eine Konfigurationsdatei mit einem Regelgeruest an.");
    command.Options.Add(profileNameOption);
    command.Options.Add(fromOption);
    command.Options.Add(descriptionOption);
    command.Options.Add(centralOption);
    command.Options.Add(forceOption);
    command.Options.Add(configOption);

    command.SetAction(parseResult => CommandContext.Run(() =>
    {
        var profileName = parseResult.GetValue(profileNameOption) ?? "default";
        var from = parseResult.GetValue(fromOption);
        var description = parseResult.GetValue(descriptionOption);
        var central = parseResult.GetValue(centralOption);
        var force = parseResult.GetValue(forceOption);
        var explicitTarget = parseResult.GetValue(configOption);

        // --config gewinnt immer, wenn ausdruecklich angegeben; sonst
        // entscheidet --central zwischen zentralem Ordner und der bisherigen
        // Vorgabe im aktuellen Verzeichnis.
        var target = explicitTarget
            ?? (central ? PathHelper.DefaultProfilePath(profileName) : ProfileStore.DefaultFileName);
        var fullTarget = Path.GetFullPath(target);

        if (File.Exists(target) && !force)
        {
            ConsoleOutput.WriteError(
                $"{fullTarget} ist bereits vorhanden. Mit --force ueberschreiben oder --config setzen.");
            return ExitCodes.Failure;
        }

        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        var profile = ProfileScaffolder.Create(profileName, from, description, extensions);
        ProfileStore.Save(profile, target);

        ConsoleOutput.WriteInfo($"{fullTarget} angelegt (Profil '{profileName}').");
        ConsoleOutput.WriteInfo($"Ersetzungstabelle: {profile.MappingStore}");

        if (profile.Fields.Count > 0)
        {
            ConsoleOutput.WriteInfo(
                $"{profile.Fields.Count} Felder uebernommen — alle stehen auf action \"error\".");
            ConsoleOutput.WriteInfo(
                "Jedes Feld durchgehen und bewusst entscheiden: pseudonymize, passthrough, redact oder drop.");
        }
        else if (from is not null)
        {
            ConsoleOutput.WriteWarning(
                "Aus der Beispieldatei liessen sich keine Feldnamen lesen. " +
                "Bei Freitextdateien greifen nur die Textregeln.");
        }

        return ExitCodes.Success;
    }));

    return command;
}

Command BuildObfuscateCommand()
{
    var inputArgument = new Argument<string>("input")
    {
        Description = "Eingabedatei; '-' liest von der Standardeingabe",
    };

    var command = new Command("obfuscate", "Ersetzt Echtwerte durch Pseudonyme.");
    command.Arguments.Add(inputArgument);
    command.Options.Add(outputOption);
    command.Options.Add(configOption);
    command.Options.Add(strictOption);
    command.Options.Add(dryRunOption);
    command.Options.Add(formatOption);
    command.Options.Add(jsonOption);
    command.Options.Add(allowUnsafeStoreOption);

    command.SetAction(parseResult => CommandContext.Run(() =>
    {
        var input = parseResult.GetValue(inputArgument)!;
        var output = parseResult.GetValue(outputOption);
        var json = parseResult.GetValue(jsonOption);
        var dryRun = parseResult.GetValue(dryRunOption);

        var profile = CommandContext.LoadProfile(parseResult.GetValue(configOption));
        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        var engine = new ObfuscationEngine(profile, extensions);

        var options = new RunOptions
        {
            Strict = parseResult.GetValue(strictOption),
            DryRun = dryRun,
            Format = parseResult.GetValue(formatOption),
            AllowUnsafeStore = parseResult.GetValue(allowUnsafeStoreOption),
        };

        var content = CommandContext.ReadInput(input);
        var result = engine.Obfuscate(content, input == "-" ? null : input, options);
        result.Report.Output = output;

        if (dryRun)
            ConsoleOutput.WriteInfo("Probelauf: es wurde nichts geschrieben.");
        else
            CommandContext.WriteOutput(result.Content, output, json);

        CommandContext.Report(result.Report, json);
        return ExitCodes.Success;
    }));

    return command;
}

Command BuildDeobfuscateCommand()
{
    var inputArgument = new Argument<string>("input")
    {
        Description = "Eingabedatei; ohne Angabe wird von der Standardeingabe gelesen",
        DefaultValueFactory = _ => "-",
        Arity = ArgumentArity.ZeroOrOne,
    };

    var command = new Command("deobfuscate",
        "Fuehrt Pseudonyme auf die Echtwerte zurueck — auch in beliebigem Text von der Standardeingabe.");
    command.Arguments.Add(inputArgument);
    command.Options.Add(outputOption);
    command.Options.Add(configOption);
    command.Options.Add(formatOption);
    command.Options.Add(jsonOption);
    command.Options.Add(allowUnsafeStoreOption);

    command.SetAction(parseResult => CommandContext.Run(() =>
    {
        var input = parseResult.GetValue(inputArgument) ?? "-";
        var output = parseResult.GetValue(outputOption);
        var json = parseResult.GetValue(jsonOption);

        var profile = CommandContext.LoadProfile(parseResult.GetValue(configOption));
        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        var engine = new ObfuscationEngine(profile, extensions);

        var options = new RunOptions
        {
            Format = parseResult.GetValue(formatOption),
            AllowUnsafeStore = parseResult.GetValue(allowUnsafeStoreOption),
        };

        var content = CommandContext.ReadInput(input);
        var result = engine.Deobfuscate(content, input == "-" ? null : input, options);
        result.Report.Output = output;

        CommandContext.WriteOutput(result.Content, output, json);

        // Der Bericht geht auf die Standardfehlerausgabe, damit sich die
        // zurueckuebersetzten Daten weiterleiten lassen.
        CommandContext.Report(result.Report, json);
        return ExitCodes.Success;
    }));

    return command;
}

Command BuildScanCommand()
{
    var inputArgument = new Argument<string>("input")
    {
        Description = "Zu pruefende Datei; '-' liest von der Standardeingabe",
    };

    var command = new Command("scan",
        "Prueft eine bereits pseudonymisierte Datei auf Restbestaende. Vor jeder Weitergabe ausfuehren.");
    command.Arguments.Add(inputArgument);
    command.Options.Add(configOption);
    command.Options.Add(formatOption);
    command.Options.Add(jsonOption);
    command.Options.Add(allowUnsafeStoreOption);

    command.SetAction(parseResult => CommandContext.Run(() =>
    {
        var input = parseResult.GetValue(inputArgument)!;
        var json = parseResult.GetValue(jsonOption);

        var profile = CommandContext.LoadProfile(parseResult.GetValue(configOption));
        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        var engine = new ObfuscationEngine(profile, extensions);

        var options = new RunOptions
        {
            Format = parseResult.GetValue(formatOption),
            AllowUnsafeStore = parseResult.GetValue(allowUnsafeStoreOption),
        };

        var content = CommandContext.ReadInput(input);
        var result = engine.Scan(content, input == "-" ? null : input, options);

        if (json)
            ConsoleOutput.WriteJson(result.Report);
        else
            ConsoleOutput.WriteSummary(result.Report);

        if (result.Report.HasFindings)
        {
            ConsoleOutput.WriteError(
                $"{result.Report.Findings.Count} Verdachtsfaelle. Die Datei nicht weitergeben, " +
                "bevor sie geklaert sind.");
            return ExitCodes.ScanFindings;
        }

        ConsoleOutput.WriteInfo("Keine Restbestaende gefunden.");
        return ExitCodes.Success;
    }));

    return command;
}

Command BuildMappingCommand()
{
    var namespaceOption = new Option<string?>("--namespace", "-n")
    {
        Description = "Auf einen Namensraum einschraenken",
    };

    var command = new Command("mapping", "Auskunft ueber die Ersetzungstabelle.");

    var listCommand = new Command("list",
        "Zaehlt die Eintraege je Namensraum. Zeigt keine Werte an — die Tabelle enthaelt Echtdaten.");
    listCommand.Options.Add(configOption);
    listCommand.Options.Add(namespaceOption);
    listCommand.SetAction(parseResult => CommandContext.Run(() =>
    {
        var profile = CommandContext.LoadProfile(parseResult.GetValue(configOption));
        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        var engine = new ObfuscationEngine(profile, extensions);
        var path = engine.ResolveMappingStorePath();

        if (!File.Exists(path))
        {
            ConsoleOutput.WriteInfo($"Noch keine Ersetzungstabelle vorhanden: {path}");
            return ExitCodes.Success;
        }

        using var store = MappingStore.Open(path, profile.ProfileName, readOnly: true,
            allowInsideGitWorkingTree: true);

        ConsoleOutput.WriteInfo($"Tabelle: {path}");
        ConsoleOutput.WriteInfo($"Profil : {store.ProfileName}");

        var filter = parseResult.GetValue(namespaceOption);
        foreach (var name in store.NamespaceNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (filter is not null && !string.Equals(filter, name, StringComparison.OrdinalIgnoreCase))
                continue;
            ConsoleOutput.WriteInfo($"  {name}: {store.Entries(name).Count} Eintraege");
        }

        ConsoleOutput.WriteInfo($"Gesamt : {store.TotalEntries}");
        return ExitCodes.Success;
    }));

    var pathCommand = new Command("path", "Gibt den Pfad der Ersetzungstabelle aus.");
    pathCommand.Options.Add(configOption);
    pathCommand.SetAction(parseResult => CommandContext.Run(() =>
    {
        var profile = CommandContext.LoadProfile(parseResult.GetValue(configOption));
        var extensions = CommandContext.LoadExtensions(parseResult.GetValue(noExtensionsOption));
        Console.Out.WriteLine(new ObfuscationEngine(profile, extensions).ResolveMappingStorePath());
        return ExitCodes.Success;
    }));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(pathCommand);
    return command;
}

Command BuildProfileCommand()
{
    var sortOption = new Option<ProfileListSort>("--sort")
    {
        Description = "Sortierung: name, used oder changed",
        DefaultValueFactory = _ => ProfileListSort.Name,
    };

    // Eigene --json-Option statt der geteilten: anders als bei obfuscate/
    // deobfuscate/scan braucht die Liste kein -o, der Hinweistext der
    // geteilten Option waere hier irrefuehrend.
    var profileJsonOption = new Option<bool>("--json")
    {
        Description = "Liste als JSON auf die Standardausgabe",
    };

    var command = new Command("profile", "Auskunft ueber bekannte Profile.");

    var listCommand = new Command("list",
        "Zeigt die Profile im zentralen Ordner sowie aus dem Nutzungs-Index. " +
        "Die Zuletzt-Liste der Oberflaeche (gui.json) wird nicht gelesen.");
    listCommand.Options.Add(sortOption);
    listCommand.Options.Add(profileJsonOption);
    listCommand.SetAction(parseResult => CommandContext.Run(() =>
    {
        var sort = parseResult.GetValue(sortOption);
        var json = parseResult.GetValue(profileJsonOption);

        var index = ProfileIndex.Load();
        var summaries = ProfileCatalog.Collect(index, Array.Empty<string>());
        var sorted = SortProfiles(summaries, sort);

        if (json)
            ConsoleOutput.WriteProfilesJson(sorted);
        else
            ConsoleOutput.WriteProfiles(sorted);

        return ExitCodes.Success;
    }));

    command.Subcommands.Add(listCommand);
    return command;
}

Command BuildExtensionsCommand()
{
    var command = new Command("extensions",
        "Auskunft ueber die Erweiterungsdatei -- hauseigene Generatoren, Textregeln und Spaltenmuster " +
        "ausserhalb jedes Profils (siehe 'extensions path' fuer den Ablageort).");

    var listCommand = new Command("list",
        "Zeigt Generatoren, Textregeln und Spaltenmuster der Erweiterungsdatei samt ihren Mustern. " +
        "Anders als bei 'mapping list' stehen hier keine Echtdaten, sondern nur Konfiguration -- die " +
        "Muster gehoeren deshalb mit in die Ausgabe. Nennt zudem, welcher der beiden Fundorte greift.");
    listCommand.SetAction(_ => CommandContext.Run(() =>
    {
        var resolution = ExtensionLibrary.ResolvePath();
        var extensions = ExtensionLibrary.Load(resolution.Path);

        if (resolution.Path is null)
            ConsoleOutput.WriteInfo("Erweiterungsdatei: keine gefunden.");
        else
            ConsoleOutput.WriteInfo(
                $"Erweiterungsdatei: {resolution.Path} ({DescribeOrigin(resolution.Origin!.Value)})");

        if (extensions.IsEmpty)
        {
            ConsoleOutput.WriteInfo("Keine Eintraege.");
            return ExitCodes.Success;
        }

        if (extensions.Generators.Count > 0)
        {
            ConsoleOutput.WriteInfo("Generatoren:");
            foreach (var (key, settings) in extensions.Generators.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var baseName = string.IsNullOrWhiteSpace(settings.Type) ? key : settings.Type;
                ConsoleOutput.WriteInfo($"  {key} ({GeneratorDescriptions.Label(baseName)})");
                if (!string.IsNullOrEmpty(settings.Pattern))
                    ConsoleOutput.WriteInfo($"    Muster: {settings.Pattern}");
            }
        }

        if (extensions.TextRules.Count > 0)
        {
            ConsoleOutput.WriteInfo("Textregeln:");
            foreach (var rule in extensions.TextRules.OrderBy(r => r.Name, StringComparer.Ordinal))
                ConsoleOutput.WriteInfo(
                    $"  {rule.Name} (Prio {rule.Priority}, Generator '{rule.Generator}'): {rule.Pattern}");
        }

        if (extensions.FieldRules.Count > 0)
        {
            ConsoleOutput.WriteInfo("Spaltenmuster:");
            foreach (var rule in extensions.FieldRules)
                ConsoleOutput.WriteInfo($"  {rule.Pattern} -> {rule.Generator}");
        }

        return ExitCodes.Success;
    }));

    var pathCommand = new Command("path", "Gibt den Pfad der Erweiterungsdatei aus.");
    pathCommand.SetAction(_ => CommandContext.Run(() =>
    {
        // Der eigentliche Pfad geht auf die Standardausgabe -- nur er, damit
        // ein Skript ihn per Kommandosubstitution abgreifen kann. Alles
        // Erklaerende (geprueft Orte, uebergangene Profildatei) geht auf die
        // Standardfehlerausgabe.
        var resolution = ExtensionLibrary.ResolvePath();

        if (resolution.Path is not null)
            Console.Out.WriteLine(resolution.Path);
        else
            ConsoleOutput.WriteInfo("Keine Erweiterungsdatei gefunden. Geprueft:");

        foreach (var candidate in resolution.Candidates)
        {
            if (candidate.SkippedAsProfile)
                ConsoleOutput.WriteInfo(
                    $"  {candidate.Path} ({DescribeOrigin(candidate.Origin)}): dort liegt ein Profil, uebergangen.");
            else if (resolution.Path is null)
                ConsoleOutput.WriteInfo(
                    $"  {candidate.Path} ({DescribeOrigin(candidate.Origin)}): nicht vorhanden.");
        }

        return ExitCodes.Success;
    }));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(pathCommand);
    return command;
}

string DescribeOrigin(ExtensionOrigin origin) => origin switch
{
    ExtensionOrigin.ProgramDirectory => "neben der Programmdatei",
    ExtensionOrigin.ConfigDirectory => "im Konfigurationsordner",
    _ => origin.ToString(),
};

IReadOnlyList<ProfileSummary> SortProfiles(IReadOnlyList<ProfileSummary> summaries, ProfileListSort sort)
    => sort switch
    {
        ProfileListSort.Used => summaries
            .OrderByDescending(p => p.LastUsedUtc ?? DateTimeOffset.MinValue)
            .ToList(),
        ProfileListSort.Changed => summaries
            .OrderByDescending(p => p.ModifiedUtc)
            .ToList(),
        _ => summaries
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList(),
    };

/// <summary>Sortierschluessel fuer 'obfuskation profile list'.</summary>
internal enum ProfileListSort
{
    Name,
    Used,
    Changed,
}
