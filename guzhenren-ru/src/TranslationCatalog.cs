using System.Reflection;
using System.Text.Json;

namespace GuZhenRenRussian;

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
        if (Tables.Count != 26) throw new InvalidDataException("Expected 13 Russian and 13 English localization tables.");
    }

    public static bool TryGet(string table, string key, out string text)
    {
        text = string.Empty;
        if (_originalTextScope > 0 || !key.StartsWith("GU_ZHEN_REN_", StringComparison.Ordinal)) return false;
        return Tables.TryGetValue((English ? "eng/" : "rus/") + table, out var entries)
            && entries.TryGetValue(key, out text!);
    }

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
