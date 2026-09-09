using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Ein einzelner Vorschlag im Dialog "Muster erkennen": Feldname, vorgeschlagene
/// Aktion (immer "ersetzen") und Generator, sowie der Beleg-Beispielwert, der
/// zum Vorschlag gefuehrt hat -- siehe <see cref="ValueSuggestion.Evidence"/>.
/// </summary>
public sealed class PatternSuggestionItemViewModel : ObservableObject
{
    private bool _isChecked;

    public PatternSuggestionItemViewModel(ValueSuggestion suggestion, bool isChecked)
    {
        Suggestion = suggestion;
        _isChecked = isChecked;
    }

    public ValueSuggestion Suggestion { get; }

    public string FieldName => Suggestion.FieldName;
    public string Generator => Suggestion.Generator;
    public string Evidence => Suggestion.Evidence;

    /// <summary>Kurztext: was das Uebernehmen fuer dieses Feld eintraegt.</summary>
    public string ActionSummary => $"ersetzen mit „{Generator}“";

    /// <summary>
    /// Vorbelegt ist nur ein Feld, das beim Oeffnen des Dialogs noch auf
    /// "offen" (<c>error</c>) stand -- ein bereits entschiedenes Feld soll nie
    /// ohne ausdrueckliches Zutun ueberschrieben werden.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>
/// Der Dialog "Muster erkennen": zeigt, welche Felder der offenen Datei
/// vollstaendig auf ein bekanntes Muster passen -- die eingebauten Muster aus
/// <see cref="ProfileScaffolder.DefaultTextRules"/> und die Textregeln der
/// Erweiterungsdatei (siehe <see cref="ValueSuggester"/>) -- und uebernimmt
/// die angehakten auf Wunsch.
///
/// Traegt bewusst keine Fensterreferenz -- wie <see cref="NewProfileViewModel"/>
/// meldet sie das Ende ueber <see cref="CloseRequested"/>; gelesen wird das
/// Ergebnis danach aus <see cref="Confirmed"/> und <see cref="Accepted"/>.
/// </summary>
public sealed class PatternSuggestionsViewModel : ObservableObject
{
    public PatternSuggestionsViewModel(
        IReadOnlyList<PatternSuggestionItemViewModel> items, string extensionPath)
    {
        Items = new ObservableCollection<PatternSuggestionItemViewModel>(items);
        ExtensionPath = extensionPath;

        ApplyCommand = new RelayCommand(Apply);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public ObservableCollection<PatternSuggestionItemViewModel> Items { get; }

    public bool HasSuggestions => Items.Count > 0;

    /// <summary>
    /// Pfad der Erweiterungsdatei, fuer den Hinweis ohne Vorschlaege -- das ist
    /// der Moment, in dem sichtbar wird, dass eigene Muster dort hingehoeren.
    /// </summary>
    public string ExtensionPath { get; }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>Ob "Übernehmen" gewaehlt wurde, statt abzubrechen.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Die beim Bestaetigen angehakten Vorschlaege, als Feldname/Generator-Paare.</summary>
    public IReadOnlyList<PatternSuggestionAcceptance> Accepted { get; private set; } =
        Array.Empty<PatternSuggestionAcceptance>();

    public event Action? CloseRequested;

    private void Apply()
    {
        Accepted = Items
            .Where(item => item.IsChecked)
            .Select(item => new PatternSuggestionAcceptance(item.FieldName, item.Generator))
            .ToList();

        Confirmed = true;
        CloseRequested?.Invoke();
    }
}
