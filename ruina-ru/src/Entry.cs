using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Models;

namespace RuinaRussian;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    private static SceneTree? _tree;
    private static bool _initialized;
    private static string SettingsPath => ProjectSettings.GlobalizePath("user://ruina-russian-language.txt");

    public static void Initialize()
    {
        if (_initialized) return;
        TranslationCatalog.Initialize();
        try { TranslationCatalog.SetEnglish(File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "eng"); }
        catch (IOException error) { GD.PrintErr("[Ruina Russian] " + error.Message); }
        var harmony = new Harmony("RuinaRussian.local-text");
        harmony.PatchAll(typeof(Entry).Assembly);
        CardDisplayState.Register(harmony);
        Compatibility.Initialize(harmony);
        TranslationCatalog.Changed += OnChanged;
        // SceneTree's signal runs in both solo and multiplayer, including pause.
        // No dependency on a local character, host status or event layout.
        _tree = Engine.GetMainLoop() as SceneTree;
        if (_tree == null) throw new InvalidOperationException("The game scene tree is unavailable.");
        _tree.ProcessFrame += Tick;
        _initialized = true;
        GD.Print("[Ruina Russian] Loaded. P switches only Ruina2: Русский / English.");
    }

    private static void Tick()
    {
        if (_tree?.Root == null) return;
        Window window = _tree.Root.GetLastExclusiveWindow() ?? _tree.Root;
        Control? focus = window.GuiGetFocusOwner();
        bool typing = false;
        for (Node? node = focus; node != null; node = node.GetParent())
            if (node is LineEdit or TextEdit) { typing = true; break; }
        Compatibility.TryRegister();
        bool modified = Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl)
            || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Meta);
        TranslationCatalog.Poll(Input.IsPhysicalKeyPressed(Key.P), typing || modified);
    }

    private static void OnChanged()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath + ".tmp", TranslationCatalog.English ? "eng" : "rus");
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch (IOException error) { GD.PrintErr("[Ruina Russian] Could not save language: " + error.Message); }
        // Notify existing locale subscribers without changing the game's language
        // or reloading tables shared with the base game and other mods.
        try
        {
            if (LocManager.Instance != null)
                AccessTools.Method(typeof(LocManager), "TriggerLocaleChange")!.Invoke(LocManager.Instance, null);
            if (_tree != null) RefreshVisibleCards(_tree.Root);
        }
        catch (Exception error) { GD.PrintErr("[Ruina Russian] Display refresh: " + error); }
        GD.Print("[Ruina Russian] " + (TranslationCatalog.English ? "English" : "Русский"));
    }

    private static void RefreshVisibleCards(Node node)
    {
        if (node is Control control && control.IsVisibleInTree()
            && node.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Cards.NCard")
        {
            object? model = AccessTools.Property(node.GetType(), "Model")?.GetValue(node);
            if (model != null && (model.GetType().Assembly.GetName().Name == "Ruina2"
                || model.GetType().Namespace?.StartsWith("Ruina2.Ruina2Code.Cards", StringComparison.Ordinal) is true))
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
