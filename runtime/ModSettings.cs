using System.Globalization;
using System.Reflection;

namespace RefreshAncient.Compat2;

public static class ModSettings
{
    private const string ModId = "refreshancient.mod";
    private static Type? _api;
    private static bool _changing;
    private static readonly string[] KeyIds = { "hideButtonsKey", "moveButtonsKey", "resetButtonsKey" };
    private static readonly string[] Defaults = { "H", "J", "K" };
    private static readonly string[] Keys = { "H", "J", "K" };
    private static readonly string[] KeyTitles = { "Hide/show buttons key", "Move buttons key", "Reset button positions key" };
    private static readonly string[] RussianTitles = { "Клавиша скрытия кнопок", "Клавиша перемещения кнопок", "Клавиша сброса позиций" };

    private static Type? Api()
    {
        if (_api != null) return _api;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetType("ModConfig.ModConfigApi", false) is Type type) { _api = type; break; }
        return _api;
    }
    private static string Read(string key, string fallback)
    {
        try
        {
            MethodInfo? get = Api()?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetValue" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            return Convert.ToString(get?.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { ModId, key }), CultureInfo.InvariantCulture) is string value && !string.IsNullOrEmpty(value) ? value : fallback;
        }
        catch { return fallback; }
    }
    private static void Write(string key, string value)
    {
        MethodInfo? set = Api()?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "SetValue" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
        set?.Invoke(null, new object[] { ModId, key, value });
    }
    public static int Cost(bool ancient, int mode)
    {
        if (mode == 2) return 0;
        if (mode == 1) return ancient ? 75 : 50;
        int fallback = ancient ? 100 : 75;
        if (mode != 3) return fallback;
        string raw = Read(ancient ? "customAncientCost" : "customOptionsCost", fallback.ToString(CultureInfo.InvariantCulture));
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? (int)Math.Clamp(value, 0, 65535) : fallback;
    }
    public static int CurrentCost(bool ancient)
    {
        string mode = Read("costMode", "Standard");
        return Cost(ancient, mode.Equals("Custom", StringComparison.OrdinalIgnoreCase) || mode == "自定义" ? 3
            : mode.Equals("Test", StringComparison.OrdinalIgnoreCase) || mode == "测试" ? 2
            : mode.Equals("Easy", StringComparison.OrdinalIgnoreCase) || mode == "简单" ? 1 : 0);
    }
    internal static void SyncLabels(object? options, object? ancients)
    {
        if (options != null) UniqueOptions.Set(options, "Text", UniqueOptions.OptionsText(CurrentCost(false)));
        if (ancients != null) UniqueOptions.Set(ancients, "Text", UniqueOptions.AncientText(CurrentCost(true)));
    }
    public static string KeyName(int action) => Keys[action];
    public static void ReloadBindings()
    {
        string[] loaded = new string[Keys.Length];
        for (int i = 0; i < loaded.Length; i++)
        {
            string saved = NormalizeKey(Read(KeyIds[i], Defaults[i]), Defaults[i]);
            bool duplicate = false;
            for (int j = 0; j < i; j++) if (loaded[j] == saved) duplicate = true;
            if (duplicate)
            {
                foreach (string candidate in Defaults)
                {
                    bool used = false;
                    for (int j = 0; j < i; j++) if (loaded[j] == candidate) used = true;
                    if (!used) { saved = candidate; break; }
                }
            }
            loaded[i] = saved;
        }
        Array.Copy(loaded, Keys, Keys.Length);
    }
    public static string NormalizeKey(string value, string fallback)
    {
        string text = value.Trim();
        if (text.Length == 1 && char.ToUpperInvariant(text[0]) is >= 'A' and <= 'Z') return text.ToUpperInvariant();
        if (text.Length > 1 && char.ToUpperInvariant(text[0]) == 'F' && int.TryParse(text.AsSpan(1), out int f) && f is >= 1 and <= 24) return "F" + f;
        foreach (string name in new[] { "Space", "Tab", "Insert", "Delete", "Home", "End", "Pageup", "Pagedown" })
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase)) return name;
        return fallback;
    }
    private static Dictionary<string, string> Labels(string english, string russian)
        => new() { ["en"] = english, ["rus"] = russian, ["ru"] = russian, ["ru_RU"] = russian, ["zhs"] = english };
    private static void Label(object entry, string english, string russian, string description, string russianDescription)
    {
        UniqueOptions.Set(entry, "Label", english);
        UniqueOptions.Set(entry, "Labels", Labels(english, russian));
        UniqueOptions.Set(entry, "Description", description);
        UniqueOptions.Set(entry, "Descriptions", Labels(description, russianDescription));
    }
    private static void ChangePrice(string key, object value)
    {
        if (_changing) return;
        _changing = true;
        try
        {
            int fallback = key == "customOptionsCost" ? 75 : 100;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            int price = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? (int)Math.Clamp(parsed, 0, 65535) : fallback;
            Write(key, price.ToString(CultureInfo.InvariantCulture));
            Write("costMode", "Custom");
        }
        catch (Exception error) { UniqueOptions.Log(error); }
        finally { _changing = false; }
    }
    private static void ChangeKey(int action, object value)
    {
        if (_changing) return;
        _changing = true;
        try
        {
            string key = NormalizeKey(Convert.ToString(value) ?? string.Empty, Keys[action]);
            for (int i = 0; i < Keys.Length; i++) if (i != action && Keys[i] == key) key = Keys[action];
            Keys[action] = key;
            Write(KeyIds[action], key);
        }
        catch (Exception error) { UniqueOptions.Log(error); }
        finally { _changing = false; }
    }
    public static Array ExtendEntries(Array original)
    {
        Type entryType = original.GetType().GetElementType()!;
        List<object> entries = new();
        foreach (object entry in original)
        {
            string? key = UniqueOptions.Get(entry, "Key") as string;
            if (key == "customOptionsCost" || key == "customAncientCost")
            {
                bool ancient = key == "customAncientCost";
                Label(entry, ancient ? "Refresh Ancient price" : "Refresh Options price", ancient ? "Цена смены Древнего" : "Цена обновления выбора",
                    "0–65535 gold. Editing automatically enables Custom prices; the buttons update immediately.",
                    "0–65535 золота. Изменение автоматически включает свою стоимость. Цена на кнопке обновляется сразу.");
                UniqueOptions.Set(entry, "OnChanged", new Action<object>(value => ChangePrice(key, value)));
            }
            entries.Add(entry);
        }
        ReloadBindings();
        for (int i = 0; i < Keys.Length; i++)
        {
            int action = i;
            object entry = Activator.CreateInstance(entryType)!;
            UniqueOptions.Set(entry, "Key", KeyIds[i]);
            Label(entry, KeyTitles[i], RussianTitles[i], "A–Z, F1–F24 or Space/Tab/Insert/Delete/Home/End/Pageup/Pagedown. Use different keys.",
                "A–Z, F1–F24 или Space/Tab/Insert/Delete/Home/End/Pageup/Pagedown. Для действий нужны разные клавиши.");
            Type enumType = entryType.GetProperty("Type")!.PropertyType;
            UniqueOptions.Set(entry, "Type", Enum.Parse(enumType, "TextInput"));
            UniqueOptions.Set(entry, "DefaultValue", Defaults[i]);
            UniqueOptions.Set(entry, "OnChanged", new Action<object>(value => ChangeKey(action, value)));
            entries.Add(entry);
        }
        Array result = Array.CreateInstance(entryType, entries.Count);
        for (int i = 0; i < entries.Count; i++) result.SetValue(entries[i], i);
        return result;
    }
}
