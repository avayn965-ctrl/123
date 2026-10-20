using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RefreshAncient.Compat2;

// This type is merged into RefreshAncient.dll. No extra runtime DLL is needed.
// The boundary uses reflection so the history/selection/UI logic can be tested
// without loading the game or Godot in the test process.
public static class UniqueOptions
{
    private sealed class History
    {
        public readonly HashSet<string> Seen = new(StringComparer.Ordinal);
        public readonly Dictionary<string, object> Pool = new(StringComparer.Ordinal);
        public readonly Dictionary<string, object> Catalog = new(StringComparer.Ordinal);
        public readonly HashSet<string> Prepared = new(StringComparer.Ordinal);
        public Type ListType = null!;
        public int BatchSize;
    }

    private static readonly ConditionalWeakTable<object, History> Histories = new();
    private static readonly object Gate = new();
    private static readonly HashSet<string> ReportedErrors = new(StringComparer.Ordinal);
    private static Type? _locManager;
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static object? Get(object? target, string name)
    {
        if (target == null) return null;
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(name, InstanceFlags);
            if (property != null) return property.GetValue(target);
            FieldInfo? field = type.GetField(name, InstanceFlags);
            if (field != null) return field.GetValue(target);
        }
        return null;
    }

    internal static object? Call(object target, string name, params object?[] args)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        foreach (MethodInfo method in type.GetMethods(InstanceFlags))
        {
            if (method.Name != name || method.IsGenericMethodDefinition) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length) continue;
            bool matches = true;
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i])) matches = false;
            if (matches) return method.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }

    internal static void Set(object target, string name, object value)
    {
        PropertyInfo? property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite) throw new MissingMemberException(target.GetType().FullName, name);
        property.SetValue(target, value);
    }

    internal static IEnumerable Items(object? value) => value as IEnumerable ?? Array.Empty<object>();
    private static string Key(object option) => Get(option, "TextKey") as string ?? string.Empty;
    private static bool Usable(object option) => !string.IsNullOrWhiteSpace(Key(option))
        && Get(option, "IsLocked") is not true && Get(option, "IsProceed") is not true
        && Get(option, "OnChosen") != null;

    private static bool HasBaseName(object obj, string name)
    {
        for (Type? type = obj.GetType(); type != null; type = type.BaseType)
            if (type.FullName == "MegaCrit.Sts2.Core.Models.Events." + name) return true;
        return false;
    }

    private static int EnchantableCards(object ancient, string enchantment)
    {
        Assembly game = FindGameAssembly(ancient);
        Type enchantmentType = game.GetType("MegaCrit.Sts2.Core.Models.Enchantments." + enchantment, true)!;
        Type db = game.GetType("MegaCrit.Sts2.Core.Models.ModelDb", true)!;
        MethodInfo? factory = null;
        foreach (MethodInfo method in db.GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (method.Name == "Enchantment" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0) factory = method;
        if (factory == null) throw new MissingMethodException(db.FullName, "Enchantment");
        object model = factory.MakeGenericMethod(enchantmentType).Invoke(null, null)!;
        int count = 0;
        foreach (object card in Items(Get(Get(Get(ancient, "Owner"), "Deck"), "Cards")))
            if (Call(model, "CanEnchant", card) is true) count++;
        return count;
    }

    private static Assembly FindGameAssembly(object ancient)
    {
        for (Type? type = ancient.GetType(); type != null; type = type.BaseType)
            if (type.Assembly.GetName().Name == "sts2") return type.Assembly;
        throw new InvalidOperationException("The game assembly was not found for the ancient event.");
    }

    private static bool Eligible(object ancient, object option)
    {
        if (!Usable(option)) return false;
        object? owner = Get(ancient, "Owner");
        object? relic = Get(option, "Relic");
        string relicName = relic?.GetType().Name ?? string.Empty;
        if (HasBaseName(ancient, "Neow"))
        {
            foreach (object ignored in Items(Get(Get(owner, "RunState"), "Modifiers"))) return false;
            if (relic != null && Call(relic, "IsAllowedAtNeow", owner) is false) return false;
        }
        if (HasBaseName(ancient, "Darv") && relic != null && relicName != "DustyTome")
        {
            FieldInfo? sets = null;
            for (Type? type = ancient.GetType(); type != null && sets == null; type = type.BaseType)
                sets = type.GetField("_validRelicSets", BindingFlags.Static | BindingFlags.NonPublic);
            if (sets == null) throw new MissingFieldException("Darv", "_validRelicSets");
            bool valid = false;
            foreach (object set in Items(sets.GetValue(null)))
            {
                if (Get(set, "filter") is not Delegate filter || filter.DynamicInvoke(owner) is not true) continue;
                foreach (object candidate in Items(Get(set, "relics")))
                    if (Equals(Get(candidate, "Id"), Get(relic, "Id"))) valid = true;
            }
            if (!valid) return false;
        }
        if (HasBaseName(ancient, "Nonupeipe") && relicName == "BeautifulBracelet" && EnchantableCards(ancient, "Swift") < 4) return false;
        if (HasBaseName(ancient, "Tanx") && relicName == "TriBoomerang" && EnchantableCards(ancient, "Instinct") < 3) return false;
        if (HasBaseName(ancient, "Pael"))
        {
            if (relicName == "PaelsClaw" && EnchantableCards(ancient, "Goopy") < 3) return false;
            if (relicName == "PaelsTooth")
            {
                int removable = 0;
                foreach (object card in Items(Get(Get(owner, "Deck"), "Cards"))) if (Get(card, "IsRemovable") is true) removable++;
                if (removable < 5) return false;
            }
            if (relicName == "PaelsLegion" && owner != null && Call(owner, "HasEventPet") is true) return false;
        }
        if (HasBaseName(ancient, "Tezcatara") && relicName == "NutritiousSoup")
        {
            bool basicStrike = false;
            foreach (object card in Items(Get(Get(owner, "Deck"), "Cards")))
                if (Convert.ToString(Get(card, "Rarity")) == "Basic")
                    foreach (object tag in Items(Get(card, "Tags"))) if (tag.ToString() == "Strike") basicStrike = true;
            if (!basicStrike) return false;
        }
        if (HasBaseName(ancient, "Orobas") && relicName == "SeaGlass")
        {
            object? characterId = Get(relic, "CharacterId");
            object? ownId = Get(Get(owner, "Character"), "Id");
            bool hasOther = false, allowed = false;
            foreach (object character in Items(Get(Get(owner, "UnlockState"), "Characters")))
            {
                object? id = Get(character, "Id");
                if (!Equals(id, ownId)) hasOther = true;
                if (Equals(id, characterId)) allowed = true;
            }
            if (!allowed || hasOther && Equals(characterId, ownId)) return false;
        }
        return true;
    }

    private static void Observe(History history, object ancient)
    {
        foreach (object option in Items(Get(ancient, "CurrentOptions")))
            if (!string.IsNullOrWhiteSpace(Key(option))) history.Seen.Add(Key(option));
    }

    private static History GetHistory(object ancient)
    {
        if (!Histories.TryGetValue(ancient, out History? history))
        {
            history = new History();
            object? current = Get(ancient, "CurrentOptions");
            foreach (object ignored in Items(current)) history.BatchSize++;
            history.BatchSize = Math.Max(1, history.BatchSize);
            for (Type? type = ancient.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField("_currentOptions", InstanceFlags);
                if (field != null) { history.ListType = field.FieldType; break; }
            }
            if (history.ListType == null || !typeof(IList).IsAssignableFrom(history.ListType))
                throw new MissingFieldException(ancient.GetType().FullName, "_currentOptions");
            Observe(history, ancient);
            // AllPossibleOptions is materialized once. UI polling never advances
            // the game RNG; random setup such as DustyTome happens only on select.
            foreach (object option in Items(Get(ancient, "AllPossibleOptions")))
            {
                string key = Key(option);
                if (!string.IsNullOrWhiteSpace(key) && Get(option, "IsProceed") is not true) history.Catalog.TryAdd(key, option);
                if (Eligible(ancient, option))
                {
                    history.Pool.TryAdd(key, option);
                    history.Catalog[key] = history.Pool[key];
                }
            }
            foreach (object option in Items(current))
                if (!string.IsNullOrWhiteSpace(Key(option)) && Get(option, "IsProceed") is not true) history.Catalog[Key(option)] = option;
            Histories.Add(ancient, history);
        }
        Observe(history, ancient);
        return history;
    }

    private static object Prepare(object ancient, History history, object option)
    {
        string key = Key(option);
        if (history.Prepared.Contains(key)) return history.Pool[key];
        object? relic = Get(option, "Relic");
        if (HasBaseName(ancient, "Darv") && relic?.GetType().Name == "DustyTome")
        {
            Call(relic, "SetupForPlayer", Get(ancient, "Owner"));
            // Rebuild the option after setup so its localization variables and
            // hover tips describe the chosen card, just as native generation does.
            option = Call(ancient, "RelicOption", relic, "INITIAL", null)!;
            if (Key(option) != key) throw new InvalidOperationException("Prepared relic option changed its text key.");
            history.Pool[key] = option;
            history.Catalog[key] = option;
        }
        history.Prepared.Add(key);
        return option;
    }

    public static object? Generate(object ancient)
    {
        try
        {
            lock (Gate)
            {
                History history = GetHistory(ancient);
                List<object> available = new();
                foreach (KeyValuePair<string, object> pair in history.Pool)
                    if (!history.Seen.Contains(pair.Key)) available.Add(pair.Value);
                if (available.Count == 0) return null;
                IList result = (IList)Activator.CreateInstance(history.ListType)!;
                object rng = Get(ancient, "Rng") ?? throw new MissingMemberException("EventModel.Rng");
                int count = Math.Min(history.BatchSize, available.Count);
                for (int i = 0; i < count; i++)
                {
                    int index = (int)Call(rng, "NextInt", available.Count)!;
                    object option = available[index];
                    available.RemoveAt(index);
                    result.Add(Prepare(ancient, history, option));
                }
                return result;
            }
        }
        catch (Exception error) { Log(error); return null; }
    }

    // Called only after the original mod has applied/paid for a new list.
    // Failed/unpaid previews do not consume the remaining choices.
    public static void Commit(object ancient, object options)
    {
        lock (Gate)
        {
            History history = GetHistory(ancient);
            foreach (object option in Items(options)) if (!string.IsNullOrWhiteSpace(Key(option))) history.Seen.Add(Key(option));
        }
    }

    public static object? Rebuild(object ancient, object keys)
    {
        lock (Gate)
        {
            History history = GetHistory(ancient);
            IList result = (IList)Activator.CreateInstance(history.ListType)!;
            HashSet<string> packetKeys = new(StringComparer.Ordinal);
            foreach (object keyObject in Items(keys))
            {
                string key = keyObject as string ?? string.Empty;
                if (!packetKeys.Add(key) || history.Seen.Contains(key) || !history.Pool.TryGetValue(key, out object? option)) return null;
                if (result.Count == history.BatchSize) return null;
                result.Add(Prepare(ancient, history, option));
            }
            return result.Count > 0 && result.Count <= history.BatchSize ? result : null;
        }
    }

    public static int Remaining(object ancient)
    {
        lock (Gate)
        {
            History history = GetHistory(ancient);
            int count = 0;
            foreach (string key in history.Pool.Keys) if (!history.Seen.Contains(key)) count++;
            return count;
        }
    }

    internal static object? CurrentAncient()
    {
        Type? room = LoadedType("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "sts2");
        return Get(room?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null), "_event");
    }

    // Godot and mods can be loaded in different AssemblyLoadContexts. Resolve
    // the existing game/engine types before attempting a name-based load.
    internal static Type? LoadedType(string name, string assemblyName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == assemblyName && assembly.GetType(name, false) is Type type) return type;
        return Type.GetType(name + ", " + assemblyName, false);
    }

    public static bool CanPress(object layout)
    {
        if (CatalogUi.IsOpen() || CatalogUi.ButtonsHidden(layout) || ButtonPlacement.BlocksClick(layout)) return false;
        try
        {
            object? ancient = CurrentAncient();
            if (ancient != null && Remaining(ancient) > 0) return true;
        }
        catch (Exception error) { Log(error); }
        UpdateUi(layout);
        return false;
    }

    public static void UpdateUi(object layout)
    {
        object? button = null;
        try
        {
            button = Call(layout, "FindChild", "RefreshAncient_RefreshOptionsButton", true, false);
            if (button == null || Get(button, "Visible") is not true) return;
            object? ancient = CurrentAncient();
            if (ancient == null || Remaining(ancient) > 0) return;
            Set(button, "Disabled", true);
            Set(button, "Text", ExhaustedLabel(Language()));
            Set(button, "TooltipText", ExhaustedTooltip(Language()));
        }
        catch (Exception error)
        {
            Log(error);
            if (button != null)
            {
                Set(button, "Disabled", true);
                Set(button, "Text", Translate(Language(), "Обновление недоступно", "Refresh unavailable", "无法刷新"));
                Set(button, "TooltipText", Translate(Language(), "Не удалось получить список вариантов. Золото не списывается.", "Could not load available choices. No gold is charged.", "无法加载选项，不会扣除金币。"));
            }
        }
    }

    internal static void Log(Exception error)
    {
        string key = error.GetType().FullName + ": " + error.Message;
        lock (Gate) if (ReportedErrors.Add(key)) Console.WriteLine("[RefreshAncient UniqueOptions] " + error);
    }

    public static string Language()
    {
        try
        {
            _locManager ??= LoadedType("MegaCrit.Sts2.Core.Localization.LocManager", "sts2");
            object? manager = _locManager?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return Get(manager, "Language") as string ?? "eng";
        }
        catch { return "eng"; }
    }

    public static string Translate(string language, string russian, string english, string chinese)
    {
        if (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return russian;
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return chinese;
        return english;
    }

    public static string OptionsText(int cost) => OptionsTextFor(Language(), cost);
    public static string AncientText(int cost) => AncientTextFor(Language(), cost);
    public static int ButtonFontSize() => Language().StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? 18
        : Language().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? 20 : 22;
    public static string OptionsTextFor(string language, int cost) => Translate(language, $"Обновить выбор ({cost} з.)", $"Refresh Options ({cost}g)", $"刷新选项（{cost}金币）");
    public static string AncientTextFor(string language, int cost) => Translate(language, $"Сменить Древнего ({cost} з.)", $"Refresh Ancient ({cost}g)", $"刷新先古之民（{cost}金币）");
    public static string SharedTooltip() => Translate(Language(), "Это общее событие. Независимое обновление недоступно.", "This event is treated as shared. Independent refresh is disabled in this version.", "当前事件被视为共享事件，此版本未启用独立刷新。");
    public static string ReplacementTooltip() => Translate(Language(), "Нет доступного Древнего для замены.", "No replacement ancient is available.", "没有可替换的先古之民。");
    public static string ExhaustedLabel(string language) => Translate(language, "Новых вариантов нет", "No new options", "没有新选项");
    public static string ExhaustedTooltip(string language) => Translate(language, "Все доступные варианты этого Древнего уже были показаны. Золото не списывается.", "All available choices for this Ancient have already been shown. No gold is charged.", "这位先古之民的所有可用选项都已展示，不会扣除金币。");

    public static string[][] OptionCatalog(object ancient) => OptionCatalogCore(ancient, false);
    internal static string[][] OptionCatalogRich(object ancient) => OptionCatalogCore(ancient, true);
    internal static object[] OptionModels(object ancient)
    {
        lock (Gate) return GetHistory(ancient).Catalog.Values.ToArray();
    }
    private static string[][] OptionCatalogCore(object ancient, bool rich)
    {
        lock (Gate)
        {
            History history = GetHistory(ancient);
            HashSet<string> current = new(StringComparer.Ordinal);
            foreach (object option in Items(Get(ancient, "CurrentOptions"))) current.Add(Key(option));
            List<string[]> rows = new();
            foreach (KeyValuePair<string, object> entry in history.Catalog)
            {
                string status = current.Contains(entry.Key) ? "current" : history.Seen.Contains(entry.Key) ? "seen"
                    : history.Pool.ContainsKey(entry.Key) ? "available" : "unavailable";
                object? relic = Get(entry.Value, "Relic");
                bool randomCard = HasBaseName(ancient, "Darv") && relic?.GetType().Name == "DustyTome" && !history.Prepared.Contains(entry.Key);
                string description = randomCard
                    ? Translate(Language(), "Случайная древняя карта определится при появлении этого варианта.", "The random Ancient card is determined when this option is offered.", "此选项出现时才会确定随机远古卡牌。")
                    : CatalogUi.ModelDisplayText(entry.Value, "Description", string.Empty, rich);
                rows.Add(new[] { CatalogUi.ModelDisplayText(entry.Value, "Title", entry.Key, rich), description, status });
            }
            return rows.ToArray();
        }
    }

    public static Dictionary<string, string> AddRussianLabels(Dictionary<string, string> labels)
    {
        if (!labels.TryGetValue("en", out string? english)) return labels;
        string translated = RussianLabel(english);
        labels["rus"] = translated;
        labels["ru"] = translated;
        labels["ru_RU"] = translated;
        return labels;
    }

    public static string RussianLabel(string text)
    {
        if (text == "Refresh Ancient") return "Смена Древнего";
        if (text == "General") return "Общие настройки";
        if (text == "Refresh Ancient Button") return "Кнопка смены Древнего";
        if (text == "Refresh Cost Mode") return "Стоимость обновления";
        if (text == "Custom Options Cost") return "Своя стоимость обновления выбора";
        if (text == "Custom Ancient Cost") return "Своя стоимость смены Древнего";
        if (text == "Enabled = show both buttons; Disabled = hide Refresh Ancient and keep Refresh Options only") return "Включено — показать обе кнопки; выключено — оставить только обновление выбора.";
        if (text == "Standard = Ancient 100 / Options 75; Easy = Ancient 75 / Options 50; Test = Ancient 0 / Options 0; Custom = use the two inputs below") return "Обычный: смена Древнего — 100, обновление выбора — 75. Лёгкий: 75 и 50. Тестовый: бесплатно. Свой: значения ниже.";
        if (text == "Only used when Refresh Cost Mode is Custom. Enter a non-negative integer.") return "Используется при своей стоимости. Введите целое неотрицательное число.";
        return text;
    }
}
