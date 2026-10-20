using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FeaturesVerification
{
    static IEnumerable<TypeDefinition> Types(ModuleDefinition m) => m.Types.SelectMany(Expand);
    static IEnumerable<TypeDefinition> Expand(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(Expand));
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Fingerprint(MethodDefinition method) => !method.HasBody ? "" : string.Join("\n", method.Body.Instructions.Select(i =>
        i.OpCode + " " + (i.Operand switch
        {
            Instruction target => "@" + method.Body.Instructions.IndexOf(target),
            Instruction[] targets => string.Join(",", targets.Select(t => method.Body.Instructions.IndexOf(t))),
            VariableDefinition v => "local " + v.Index, ParameterDefinition p => "arg " + p.Index, _ => i.Operand?.ToString() ?? ""
        })));
    public static void Run(ModuleDefinition baseline, ModuleDefinition result, ModuleDefinition game)
    {
        var allowed = new HashSet<string>
        {
            "GenerateFreshOptions", "RebuildOptionsFromTextKeys", "GetRefreshOptionsButtonText", "GetRefreshAncientButtonText",
            "GetSharedDisabledTooltip", "GetNoReplacementTooltip", "GetButtonFontSize", "SetCurrentAndGeneratedOptions",
            "UpdateButtons", "OnRefreshOptionsPressed", "L", "Register", "BuildRefreshableAncientsForCurrentAct",
            "TryReplaceAncientSafely", "OnRefreshAncientPressed", "HideButtonsIfCancelPressed", "BuildEntries", "GetOptionsCost", "GetAncientCost",
            "EnsureVisibilityMonitorStarted"
        };
        var resultMethods = Types(result).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        var changed = new List<string>();
        foreach (var old in Types(baseline).SelectMany(t => t.Methods))
        {
            Require(resultMethods.TryGetValue(old.FullName, out var patched), "Removed existing method: " + old.FullName);
            if (Fingerprint(old) != Fingerprint(patched!))
            {
                Require(allowed.Contains(old.Name), "Unexpected changed method: " + old.FullName);
                changed.Add(old.Name);
            }
        }
        Require(changed.Count == 20 && changed.ToHashSet().SetEquals(allowed), "Wrong feature hook set.");
        foreach (var old in baseline.Resources.OfType<EmbeddedResource>())
            Require(old.GetResourceData().SequenceEqual(result.Resources.OfType<EmbeddedResource>().Single(r => r.Name == old.Name).GetResourceData()), "Embedded game/mod resources changed.");
        Require(result.AssemblyReferences.All(a => a.Name != "RuntimeFeatures"), "External helper DLL dependency was left behind.");
        var logic = Types(result).Single(t => t.Name == "AncientRefreshDualButtonsLogic");
        var click = logic.Methods.Single(m => m.Name == "OnRefreshOptionsPressed");
        Require(click.Body.Instructions[0].OpCode == OpCodes.Ldarg_0
            && click.Body.Instructions[1].Operand is MethodReference guard && guard.Name == "CanPress"
            && click.Body.Instructions[2].OpCode == OpCodes.Brtrue && click.Body.Instructions[3].OpCode == OpCodes.Ret,
            "Click must return before the original payment/network path when exhausted.");
        void Hook(string method, string expected)
        {
            Require(logic.Methods.Single(m => m.Name == method).Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "RefreshAncient.Compat2.UniqueOptions" && m.Name == expected), "Missing feature hook: " + method);
        }
        Hook("GenerateFreshOptions", "Generate"); Hook("RebuildOptionsFromTextKeys", "Rebuild");
        Hook("SetCurrentAndGeneratedOptions", "Commit"); Hook("UpdateButtons", "UpdateUi");
        var rawPool = logic.Methods.Single(m => m.Name == "BuildRefreshableAncientsUnfiltered");
        var originalPool = Types(baseline).Single(t => t.Name == logic.Name).Methods.Single(m => m.Name == "BuildRefreshableAncientsForCurrentAct");
        Require(Fingerprint(rawPool) == Fingerprint(originalPool), "Original act/unlock pool generation changed.");
        void RuntimeHook(string method, string type, string expected)
        {
            Require(logic.Methods.Single(m => m.Name == method).Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "RefreshAncient.Compat2." + type && m.Name == expected), "Missing catalog/history hook: " + method);
        }
        RuntimeHook("UpdateButtons", "CatalogUi", "Update");
        RuntimeHook("BuildRefreshableAncientsForCurrentAct", "AncientHistory", "Filter");
        RuntimeHook("OnRefreshAncientPressed", "AncientHistory", "CanPress");
        RuntimeHook("TryReplaceAncientSafely", "AncientHistory", "CanReplace");
        RuntimeHook("TryReplaceAncientSafely", "AncientHistory", "CommitIfSuccessful");
        RuntimeHook("HideButtonsIfCancelPressed", "CatalogUi", "ShouldSuppressCancel");
        RuntimeHook("GetOptionsCost", "ModSettings", "Cost");
        RuntimeHook("GetAncientCost", "ModSettings", "Cost");
        var monitor = logic.Methods.Single(m => m.Name == "EnsureVisibilityMonitorStarted");
        Require(monitor.Body.Instructions.First().OpCode == OpCodes.Ldarg_0
            && !monitor.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name is "TryGetRunState" or "get_Players" or "get_Count"),
            "The frame monitor must start independently of player count, for both host and clients.");
        foreach (string method in new[] { "HasMeta", "GetMeta", "SetMeta", "MonitorLayoutVisibilityAsync", "RunSafely" })
            Require(monitor.Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == method) == 1,
                "Missing original monitor lifecycle/duplicate guard: " + method);
        var monitorLoop = Types(result).Single(t => t.Name.StartsWith("<MonitorLayoutVisibilityAsync>"));
        var tick = monitorLoop.Methods.Single(m => m.Name == "MoveNext");
        foreach (string method in new[] { "IsInstanceValid", "IsInsideTree", "HideButtonsIfCancelPressed", "RefreshButtonsRealtime", "ToSignal", "RemoveMeta" })
            Require(tick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == method),
                "Missing per-frame update or monitor cleanup: " + method);
        var entries = Types(result).Single(t => t.Name == "ModConfigBridge").Methods.Single(m => m.Name == "BuildEntries");
        Require(entries.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ExtendEntries" && m.DeclaringType.FullName == "RefreshAncient.Compat2.ModSettings"), "ModConfig keybindings and price callbacks are not registered.");
        Require(Types(result).Single(t => t.Name == "ModConfigBridge").Methods.Single(m => m.Name == "Register").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "ReloadBindings" && m.DeclaringType.FullName == "RefreshAncient.Compat2.ModSettings"), "Saved keys must reload after ModConfig registration.");
        foreach (var name in new[] { "RefreshOptionsSingleplayer", "SendIndependentOptionsRefreshResult", "HandleReceivedRefreshRequest" })
            Require(logic.Methods.Single(m => m.Name == name).Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GenerateFreshOptions"), "A solo/host path bypasses unique generation: " + name);
        foreach (var key in new[] { "Swift", "Goopy", "Instinct" })
            Require(Types(game).Any(t => t.FullName == "MegaCrit.Sts2.Core.Models.Enchantments." + key), "Enchantment API type missing: " + key);
        var db = Types(game).Single(t => t.FullName == "MegaCrit.Sts2.Core.Models.ModelDb");
        Require(db.Methods.Any(m => m.Name == "Enchantment" && m.GenericParameters.Count == 1 && m.Parameters.Count == 0), "Enchantment factory missing.");
        Require(Types(game).Single(t => t.Name == "Player").Methods.Any(m => m.Name == "HasEventPet" && m.Parameters.Count == 0), "Pet eligibility API missing.");
        Require(Types(game).Single(t => t.Name == "LocManager").Properties.Any(p => p.Name == "Language" && p.PropertyType.FullName == "System.String"), "Game language API missing.");
        Require(Types(game).Single(t => t.Name == "RunState").Properties.Any(p => p.Name == "RunLocation"), "Room identity API missing.");
        Require(Types(game).Single(t => t.Name == "Player").Properties.Any(p => p.Name == "NetId"), "Player identity API missing.");
        Require(Types(game).Single(t => t.Name == "Darv").Fields.Any(f => f.Name == "_validRelicSets"), "Darv eligibility table missing.");
        Require(Types(game).Single(t => t.Name == "DustyTome").Methods.Any(m => m.Name == "SetupForPlayer" && m.Parameters.Count == 1), "DustyTome setup API missing.");
        var ancient = Types(game).Single(t => t.FullName == "MegaCrit.Sts2.Core.Models.AncientEventModel");
        var relic = Types(game).Single(t => t.FullName == "MegaCrit.Sts2.Core.Models.RelicModel");
        foreach (var property in new[] { "RunHistoryIcon", "RunHistoryIconOutline" })
            Require(ancient.Properties.Any(p => p.Name == property && p.PropertyType.FullName == "Godot.Texture2D"), "Ancient top-bar icon API missing: " + property);
        foreach (var property in new[] { "Icon", "IconOutline" })
            Require(relic.Properties.Any(p => p.Name == property && p.PropertyType.FullName == "Godot.Texture2D"), "Reward icon API missing: " + property);
        var topBarIcon = Types(game).Single(t => t.FullName == "MegaCrit.sts2.Core.Nodes.TopBar.NTopBarRoomIcon");
        Require(topBarIcon.Methods.Single(m => m.Name == "UpdateIcon").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GetRoomIconPath"), "Top-bar icon no longer uses the room icon path.");
        var tips = Types(game).Single(t => t.FullName == "MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet");
        Require(tips.Methods.Any(m => m.IsStatic && m.Name == "CreateAndShow" && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.FullName == "Godot.Control" && m.Parameters[1].ParameterType.FullName == "System.Collections.Generic.IEnumerable`1<MegaCrit.Sts2.Core.HoverTips.IHoverTip>"), "Game hover-tip factory API missing.");
        Require(tips.Methods.Any(m => m.IsStatic && m.Name == "Remove" && m.Parameters.Count == 1) && tips.Methods.Any(m => m.Name == "SetAlignment" && m.Parameters.Count == 2), "Game hover-tip cleanup/alignment API missing.");
        Require(Types(game).Single(t => t.FullName == "MegaCrit.Sts2.Core.Events.EventOption").Properties.Any(p => p.Name == "HoverTips"), "Event option hover-tip data missing.");
        Console.WriteLine("PASS: exactly 20 original feature hooks changed; compat1 replacement core, refund paths and constructor fixes preserved.");
        Console.WriteLine("PASS: local frame monitoring starts for solo, multiplayer host and clients without a player-count guard; original duplicate prevention, loop and cleanup remain intact.");
        Console.WriteLine("PASS: displayed prices and solo/host cost resolvers share ModSettings.Cost; existing resolved-cost network/refund paths are preserved.");
        Console.WriteLine("PASS: solo, host, remote reconstruction, commit, exhaustion preflight and UI paths are connected to the new logic.");
        Console.WriteLine("PASS: original act/unlock pool is preserved; Ancient history filters every pool path and guards/commits actual replacements.");
        Console.WriteLine("PASS: catalog windows are wired to UI updates and Escape cannot latch the pause controls while a catalog closes.");
        Console.WriteLine("PASS: new eligibility/localization reflection API exists in the supplied v0.111.0 game DLL.");
        Console.WriteLine("PASS: reward textures and Ancient top-bar room icon textures exist in the supplied game API.");
        Console.WriteLine("PASS: original option hover-tip data, factory, alignment and removal API exist in the supplied game DLL.");
        Console.WriteLine("PASS: no extra runtime DLL dependency; embedded resources are unchanged.");
    }
}
