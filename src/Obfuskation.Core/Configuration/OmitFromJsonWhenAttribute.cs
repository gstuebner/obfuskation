namespace Obfuskation.Core.Configuration;

/// <summary>
/// Markiert eine Eigenschaft, deren Wert beim Schreiben der Konfiguration
/// entfaellt, sobald er dem angegebenen Vorgabewert entspricht.
///
/// Der eingebaute <c>JsonIgnoreCondition.WhenWritingDefault</c> hilft hier
/// nicht: er vergleicht mit <c>default(T)</c>, also 0 bzw. false -- aber etwa
/// <see cref="GeneratorSettings.MaxDays"/> hat als Vorgabe 400, nicht 0. Die
/// Dateien sollen dennoch nur zeigen, was jemand tatsaechlich eingestellt
/// hat: ein <c>int</c>- oder <c>bool</c>-Feld mit einem von 0/false
/// abweichenden Vorgabewert braucht darum eine eigene Vergleichsbasis, die
/// dieses Attribut liefert. Ausgewertet vom Modifier
/// <c>ProfileStore.OmitConfiguredDefaults</c>, der ihn in
/// <see cref="ProfileStore.JsonOptions"/> eintraegt.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OmitFromJsonWhenAttribute(object value) : Attribute
{
    public object Value { get; } = value;
}
