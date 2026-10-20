namespace ModConfig;

public enum ConfigType { Header, Dropdown, TextInput }
public sealed class ConfigEntry
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, string> Labels { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Descriptions { get; set; } = new();
    public ConfigType Type { get; set; }
    public object? DefaultValue { get; set; }
    public string[]? Options { get; set; }
    public Action<object>? OnChanged { get; set; }
}
public static class ModConfigApi
{
    public static readonly Dictionary<string, object> Values = new();
    public static ConfigEntry[] Entries = Array.Empty<ConfigEntry>();
    public static T? GetValue<T>(string mod, string key)
    {
        object? value = Values.TryGetValue(key, out object? found) ? found : Entries.FirstOrDefault(e => e.Key == key)?.DefaultValue;
        return value is T typed ? typed : default;
    }
    public static void SetValue(string mod, string key, object value)
    {
        Values[key] = value;
        Entries.FirstOrDefault(e => e.Key == key)?.OnChanged?.Invoke(value);
    }
    public static ConfigEntry[] OriginalEntries() => new[]
    {
        new ConfigEntry { Label = "General", Type = ConfigType.Header },
        new ConfigEntry { Key = "refreshAncientButton", Type = ConfigType.Dropdown, DefaultValue = "Enabled" },
        new ConfigEntry { Key = "costMode", Type = ConfigType.Dropdown, DefaultValue = "Standard" },
        new ConfigEntry { Key = "customOptionsCost", Type = ConfigType.TextInput, DefaultValue = "75" },
        new ConfigEntry { Key = "customAncientCost", Type = ConfigType.TextInput, DefaultValue = "100" }
    };
    public static void Reset() { Values.Clear(); Entries = Array.Empty<ConfigEntry>(); }
}
