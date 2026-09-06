using System.CommandLine;
using Obfuskation.Cli;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;

// Gemeinsame Optionen. Sie werden mehreren Unterbefehlen zugeordnet, damit
// ueberall dieselben Namen gelten.
var configOption = new Option<string?>("--config", "-c")
{
    Description = "Pfad zur Konfigurationsdatei (Vorgabe: obfuskation.json, aufwaerts gesucht)",
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

    var forceOption = new Option<bool>("--force")
    {
        Description = "Vorhandene Konfigurationsdatei ueberschreiben",
    };

    var command = new Command("init", "Legt eine Konfigurationsdatei mit einem Regelgeruest an.");
    command.Options.Add(profileNameOption);
    command.Options.Add(fromOption);
    command.Options.Add(forceOption);
    command.Options.Add(configOption);

    command.SetAction(parseResult => CommandContext.Run(() =>
    {
        var profileName = parseResult.GetValue(profileNameOption) ?? "default";
        var from = parseResult.GetValue(fromOption);
        var force = parseResult.GetValue(forceOption);
        var target = parseResult.GetValue(configOption) ?? ProfileStore.DefaultFileName;

        if (File.Exists(target) && !force)
        {
            ConsoleOutput.WriteError(
                $"{target} ist bereits vorhanden. Mit --force ueberschreiben oder --config setzen.");
            return ExitCodes.Failure;
        }

        var profile = ProfileScaffolder.Create(profileName, from);
        ProfileStore.Save(profile, target);

        ConsoleOutput.WriteInfo($"{target} angelegt (Profil '{profileName}').");
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
        var engine = new ObfuscationEngine(profile);

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
        var engine = new ObfuscationEngine(profile);

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
        var engine = new ObfuscationEngine(profile);

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
        var engine = new ObfuscationEngine(profile);
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
        Console.Out.WriteLine(new ObfuscationEngine(profile).ResolveMappingStorePath());
        return ExitCodes.Success;
    }));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(pathCommand);
    return command;
}
