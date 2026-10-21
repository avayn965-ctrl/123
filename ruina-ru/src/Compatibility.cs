using Godot;
using HarmonyLib;
using System.Reflection;
using System.Diagnostics;

namespace RuinaRussian;

// Optional coexistence with the previously released GuZhenRen translation.
// No references to game actions, run state, RNG or networking are used here.
internal static class Compatibility
{
    private static Harmony? _harmony;
    private static bool _registered;
    private static FieldInfo? _guOriginalScope;
    private static FieldInfo? _guAddonReady;
    private static Type? _guRelic;
    private static long _nextProbe;

    internal static bool OriginalTextRequested
    {
        get
        {
            if (_guOriginalScope?.GetValue(null) is int depth && depth > 0) return true;
            if (_guRelic == null || _guAddonReady?.GetValue(null) is true) return false;
            // GuZhenRen can also be installed WITHOUT its Russian addon. In
            // that case preserve original text strictly during this relic's
            // synchronous title lookup, including its async MoveNext frame.
            foreach (StackFrame frame in new StackTrace(1, false).GetFrames())
            {
                MethodBase? method = frame.GetMethod();
                Type? owner = method?.DeclaringType;
                if (owner == _guRelic || owner?.DeclaringType == _guRelic) return true;
                if (owner == null || owner.Assembly == typeof(Compatibility).Assembly) continue;
                if (method?.Name is "get_Title" or "get_TitleLocString" or "GetRawText"
                    or "GetFormattedText" or "SmartFormat") continue;
                string ns = owner.Namespace ?? "";
                if (ns.StartsWith("System.", StringComparison.Ordinal)
                    || ns.StartsWith("MonoMod.", StringComparison.Ordinal)
                    || ns.StartsWith("HarmonyLib", StringComparison.Ordinal)) continue;
                // Stop at the closest real caller. Task completion can invoke
                // UI continuations inline while the relic is still deeper in
                // the stack; those continuations are NOT relic calculations.
                return false;
            }
            return false;
        }
    }

    internal static void Initialize(Harmony harmony)
    {
        _harmony = harmony;
        TryRegister();
    }

    internal static void TryRegister()
    {
        if (_registered || _harmony == null) return;
        if (System.Environment.TickCount64 < _nextProbe) return;
        _nextProbe = System.Environment.TickCount64 + 2000;
        _guRelic = AccessTools.TypeByName("GuZhenRen.Relics.ShuiWenGu");
        Type? catalog = AccessTools.TypeByName("GuZhenRenRussian.TranslationCatalog");
        if (catalog == null) return;
        _guOriginalScope = AccessTools.Field(catalog, "_originalTextScope");
        Type? guEntry = AccessTools.TypeByName("GuZhenRenRussian.Entry");
        _guAddonReady = guEntry == null ? null : AccessTools.Field(guEntry, "_initialized");
        MethodInfo? poll = AccessTools.Method(catalog, "Poll", new[] { typeof(bool), typeof(bool) });
        if (poll == null) return;
        _harmony.Patch(poll, prefix: new HarmonyMethod(typeof(Compatibility), nameof(GuHotkey)));
        _registered = true;
        GD.Print("[Ruina Russian] P: Ruina 2; Shift+P: GuZhenRen. Languages remain independent.");
    }

    // Consume the physical P edge in both addons, including modified/typing
    // presses, so releasing Shift while holding P cannot toggle either twice.
    private static void GuHotkey(ref bool typing)
        => typing |= !Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl)
            || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Meta);
}
