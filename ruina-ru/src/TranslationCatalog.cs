using System.Reflection;
using System.Text.Json;

namespace RuinaRussian;

public static class TranslationCatalog
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.Ordinal);
    public static bool English { get; private set; }
    private static bool _wasDown;
    [ThreadStatic] private static int _originalTextScope;
    public static event Action? Changed;

    public static void Initialize()
    {
        if (Tables.Count != 0) return;
        Assembly assembly = typeof(TranslationCatalog).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("Localization.", StringComparison.Ordinal)) continue;
            string path = resource["Localization.".Length..].Replace('\\', '/');
            string[] parts = path.Split('/');
            if (parts.Length != 2) throw new InvalidDataException("Invalid translation resource: " + resource);
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            Tables.Add(parts[0] + "/" + Path.GetFileNameWithoutExtension(parts[1]),
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
        }
        if (Tables.Count != 44) throw new InvalidDataException("Expected 22 Russian and 22 English localization tables.");
    }

    public static bool TryGet(string table, string key, out string text)
    {
        text = string.Empty;
        if (_originalTextScope > 0 || !Owns(table, key) || Compatibility.OriginalTextRequested) return false;
        return Tables.TryGetValue((English ? "eng/" : "rus/") + table, out var entries)
            && entries.TryGetValue(key, out text!);
    }

    // Three upstream keys intentionally use non-model prefixes. Never override
    // a whole base-game table: only keys bundled by Ruina2 belong to this addon.
    private static bool Owns(string table, string key) =>
        key.StartsWith("RUINA2-", StringComparison.Ordinal)
        || key.StartsWith("RUINA2.", StringComparison.Ordinal)
        || key.StartsWith("OPTION_RUINA2-", StringComparison.Ordinal)
        || (table == "gameplay_ui" && key == "MELODY.COMPLETE");

    public static int EnterOriginalTextScope()
    {
        int previous = _originalTextScope;
        _originalTextScope++;
        return previous;
    }
    public static void ExitOriginalTextScope(int previous) => _originalTextScope = previous;

    public static void SetEnglish(bool value)
    {
        if (English == value) return;
        English = value;
        Changed?.Invoke();
    }

    // This is local input state. It never reads a run, player, RNG or network service.
    public static void Poll(bool pressed, bool typing)
    {
        bool edge = pressed && !_wasDown;
        _wasDown = pressed;
        if (edge && !typing) SetEnglish(!English);
    }
}
