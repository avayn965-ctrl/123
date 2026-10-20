using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Models;

namespace GuZhenRenRussian;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    private static SceneTree? _tree;
    private static bool _initialized;
    private static string SettingsPath => ProjectSettings.GlobalizePath("user://guzhenren-russian-language.txt");

    public static void Initialize()
    {
        if (_initialized) return;
        TranslationCatalog.Initialize();
        try { TranslationCatalog.SetEnglish(File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "eng"); }
        catch (IOException error) { GD.PrintErr("[GuZhenRen Russian] " + error.Message); }
        var harmony = new Harmony("GuZhenRenRussian.local-text");
        harmony.PatchAll(typeof(Entry).Assembly);
        CardDisplayState.Register(harmony);
        TranslationCatalog.Changed += OnChanged;
        // SceneTree's signal runs in both solo and multiplayer, including pause.
        // No dependency on a local character, host status or event layout.
        _tree = Engine.GetMainLoop() as SceneTree;
        if (_tree == null) throw new InvalidOperationException("The game scene tree is unavailable.");
        _tree.ProcessFrame += Tick;
        _initialized = true;
        GD.Print("[GuZhenRen Russian] Loaded. P switches only GuZhenRen: Русский / English.");
    }

    private static void Tick()
    {
        if (_tree?.Root == null) return;
        Window window = _tree.Root.GetLastExclusiveWindow() ?? _tree.Root;
        Control? focus = window.GuiGetFocusOwner();
        bool typing = false;
        for (Node? node = focus; node != null; node = node.GetParent())
            if (node is LineEdit or TextEdit) { typing = true; break; }
        TranslationCatalog.Poll(Input.IsPhysicalKeyPressed(Key.P), typing);
    }

    private static void OnChanged()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath + ".tmp", TranslationCatalog.English ? "eng" : "rus");
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch (IOException error) { GD.PrintErr("[GuZhenRen Russian] Could not save language: " + error.Message); }
        // Notify existing locale subscribers without changing the game's language
        // or reloading tables shared with the base game and other mods.
        try
        {
            if (LocManager.Instance != null)
                AccessTools.Method(typeof(LocManager), "TriggerLocaleChange")!.Invoke(LocManager.Instance, null);
            if (_tree != null) RefreshVisibleCards(_tree.Root);
        }
        catch (Exception error) { GD.PrintErr("[GuZhenRen Russian] Display refresh: " + error); }
        GD.Print("[GuZhenRen Russian] " + (TranslationCatalog.English ? "English" : "Русский"));
    }

    private static void RefreshVisibleCards(Node node)
    {
        if (node is Control control && control.IsVisibleInTree()
            && node.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Cards.NCard")
        {
            object? model = AccessTools.Property(node.GetType(), "Model")?.GetValue(node);
            if (model != null && (model.GetType().Assembly.GetName().Name == "GuZhenRen"
                || model.GetType().Namespace?.StartsWith("GuZhenRen.Cards", StringComparison.Ordinal) is true))
            {
                MethodInfo? update = AccessTools.Method(node.GetType(), "UpdateVisuals");
                object? pile = AccessTools.Property(node.GetType(), "DisplayingPile")?.GetValue(node);
                if (update != null && pile != null)
                    update.Invoke(node, CardDisplayState.Arguments(node, pile, update.GetParameters()[1].ParameterType));
            }
        }
        foreach (Node child in node.GetChildren()) RefreshVisibleCards(child);
    }
}

internal static class CardDisplayState
{
    private sealed class State { public object[] Arguments = null!; }
    private static readonly ConditionalWeakTable<object, State> States = new();
    internal static void Register(Harmony harmony)
    {
        Type? type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Cards.NCard");
        MethodInfo? method = type == null ? null : AccessTools.Method(type, "UpdateVisuals");
        if (method != null)
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(CardDisplayState), nameof(Record)));
    }
    private static void Record(object __instance, object[] __args)
        => States.GetOrCreateValue(__instance).Arguments = (object[])__args.Clone();
    internal static object[] Arguments(object card, object pile, Type previewType)
        => States.TryGetValue(card, out State? state) ? state.Arguments : new[] { pile, Enum.ToObject(previewType, 0) };
}

[HarmonyPatch(typeof(LocString), nameof(LocString.GetRawText))]
public static class LocalTextPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    public static bool Prefix(LocString __instance, ref string __result)
    {
        if (!TranslationCatalog.TryGet(__instance.LocTable, __instance.LocEntryKey, out string text)) return true;
        __result = text;
        return false;
    }
}

// These four display strings are literals in the original mod, rather than
// localization keys. P affects them as well, in either game language.
[HarmonyPatch]
public static class SettingsTextPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type? type = AccessTools.TypeByName("GuZhenRen.Systems.GuZhenRenSettingsPage");
        if (type == null) yield break;
        foreach (string name in new[] { "GetPageTitle", "GetSectionTitle", "GetToggleLabel", "GetToggleDescription" })
            if (AccessTools.Method(type, name) is MethodInfo method) yield return method;
    }
    [HarmonyPrefix]
    private static bool Prefix(MethodBase __originalMethod, ref string __result)
    {
        bool english = TranslationCatalog.English;
        __result = __originalMethod.Name switch
        {
            "GetPageTitle" => english ? "Gu Zhen Ren" : "Гу Чжэнь Жэнь",
            "GetSectionTitle" => english ? "Text Display · P: Русский / English" : "Отображение текста · P: Русский / English",
            "GetToggleLabel" => english ? "Maggot Mode" : "Режим личинок",
            _ => english ? "Replaces this mod's Chinese text with its easter-egg version. P switches this mod between Russian and English locally."
                : "Заменяет китайские тексты мода пасхальной версией. P локально переключает тексты этого мода между русским и английским."
        };
        return false;
    }
}

// The upstream Hydrology Gu relic counts characters in card.Title to award
// gold. Its calculation must see the unmodified game's text, even when peers
// choose different display languages. Scope only the synchronous title getter;
// scoping MoveNext would leak into callbacks/inline task continuations.
[HarmonyPatch]
public static class HydrologyOriginalTextPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type? type = AccessTools.TypeByName("GuZhenRen.Relics.ShuiWenGu");
        MethodInfo? method = type == null ? null : AccessTools.Method(type, "AfterCardPlayed");
        Type? stateMachine = method?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        if (stateMachine != null && AccessTools.Method(stateMachine, "MoveNext") is MethodInfo moveNext)
            yield return moveNext;
        else if (method != null) yield return method;
    }
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(CardModel), "Title");
        MethodInfo replacement = AccessTools.Method(typeof(HydrologyOriginalTextPatch), nameof(OriginalCardTitle));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced == 0) throw new InvalidOperationException("ShuiWenGu title-counting API changed; refusing an unsafe localization hook.");
    }
    public static string OriginalCardTitle(CardModel card)
    {
        int previous = TranslationCatalog.EnterOriginalTextScope();
        try { return card.Title; }
        finally { TranslationCatalog.ExitOriginalTextScope(previous); }
    }
}
