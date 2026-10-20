using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FeaturesPatch
{
    static IEnumerable<TypeDefinition> Types(ModuleDefinition m) => m.Types.SelectMany(Expand);
    static IEnumerable<TypeDefinition> Expand(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(Expand));
    static void Widen(MethodDefinition m)
    {
        foreach (var i in m.Body.Instructions) i.OpCode = i.OpCode.Code switch
        {
            Code.Br_S => OpCodes.Br, Code.Brtrue_S => OpCodes.Brtrue, Code.Brfalse_S => OpCodes.Brfalse,
            Code.Beq_S => OpCodes.Beq, Code.Bne_Un_S => OpCodes.Bne_Un, Code.Bge_S => OpCodes.Bge,
            Code.Bge_Un_S => OpCodes.Bge_Un, Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un,
            Code.Ble_S => OpCodes.Ble, Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt,
            Code.Blt_Un_S => OpCodes.Blt_Un, Code.Leave_S => OpCodes.Leave, _ => i.OpCode
        };
    }
    static void BeforeReturn(MethodDefinition m, Action<ILProcessor, Instruction> inject)
    {
        var il = m.Body.GetILProcessor();
        foreach (var ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            // Keep the old return instruction as the branch destination, so
            // branches targeting it cannot skip the injected postfix.
            ret.OpCode = OpCodes.Nop; ret.Operand = null;
            inject(il, ret);
        }
        Widen(m);
    }
    public static void Apply(ModuleDefinition mod, string runtimePath)
    {
        if (Types(mod).Any(t => t.FullName == "RefreshAncient.Compat2.UniqueOptions")) throw new InvalidOperationException("Features already applied.");
        using var runtime = ModuleDefinition.ReadModule(runtimePath);
        var feature = RuntimeMerge.Merge(mod, runtime);
        MethodDefinition Feature(string name, int parameters = -1) => feature.Methods.Single(m => m.Name == name && (parameters < 0 || m.Parameters.Count == parameters));
        var ancientHistory = Types(mod).Single(t => t.FullName == "RefreshAncient.Compat2.AncientHistory");
        var catalog = Types(mod).Single(t => t.FullName == "RefreshAncient.Compat2.CatalogUi");
        var settings = Types(mod).Single(t => t.FullName == "RefreshAncient.Compat2.ModSettings");
        MethodDefinition Ancient(string name) => ancientHistory.Methods.Single(m => m.Name == name);
        MethodDefinition Catalog(string name) => catalog.Methods.Single(m => m.Name == name);
        MethodDefinition Settings(string name) => settings.Methods.Single(m => m.Name == name);
        var logic = Types(mod).Single(t => t.Name == "AncientRefreshDualButtonsLogic");
        MethodDefinition Logic(string name) => logic.Methods.Single(m => m.Name == name);
        void Replace(string name, string called, bool cost = false)
        {
            var method = Logic(name);
            method.Body = new MethodBody(method); var il = method.Body.GetILProcessor();
            if (cost) il.Emit(OpCodes.Call, Logic(name.Contains("Ancient") ? "get_RefreshAncientCost" : "get_RefreshOptionsCost"));
            else foreach (var p in method.Parameters) il.Emit(OpCodes.Ldarg, p);
            il.Emit(OpCodes.Call, Feature(called));
            if (method.ReturnType.FullName != Feature(called).ReturnType.FullName) il.Emit(OpCodes.Castclass, method.ReturnType);
            il.Emit(OpCodes.Ret);
        }
        Replace("GenerateFreshOptions", "Generate");
        Replace("RebuildOptionsFromTextKeys", "Rebuild");
        Replace("GetRefreshOptionsButtonText", "OptionsText", true);
        Replace("GetRefreshAncientButtonText", "AncientText", true);
        Replace("GetSharedDisabledTooltip", "SharedTooltip");
        Replace("GetNoReplacementTooltip", "ReplacementTooltip");
        Replace("GetButtonFontSize", "ButtonFontSize");
        BeforeReturn(Logic("SetCurrentAndGeneratedOptions"), (il, ret) =>
        {
            var first = Instruction.Create(OpCodes.Ldarg_0); var second = Instruction.Create(OpCodes.Ldarg_1);
            var call = Instruction.Create(OpCodes.Call, Feature("Commit")); var end = Instruction.Create(OpCodes.Ret);
            il.InsertAfter(ret, first); il.InsertAfter(first, second); il.InsertAfter(second, call); il.InsertAfter(call, end);
        });
        BeforeReturn(Logic("UpdateButtons"), (il, ret) =>
        {
            var first = Instruction.Create(OpCodes.Ldarg_0); var call = Instruction.Create(OpCodes.Call, Catalog("Update"));
            var second = Instruction.Create(OpCodes.Ldarg_0); var update = Instruction.Create(OpCodes.Call, Feature("UpdateUi"));
            il.InsertAfter(ret, first); il.InsertAfter(first, call); il.InsertAfter(call, second); il.InsertAfter(second, update); il.InsertAfter(update, Instruction.Create(OpCodes.Ret));
        });
        // H/J/K are polled by UpdateButtons, including while H hides the UI.
        // The original mod skips its frame monitor whenever Players.Count > 1,
        // leaving multiplayer layouts with only occasional dialogue updates.
        // Remove only that initial guard; keep the per-layout duplicate guard,
        // process-frame loop and cleanup unchanged for both host and clients.
        var monitor = Logic("EnsureVisibilityMonitorStarted");
        var monitorEntry = monitor.Body.Instructions.First(i => i.OpCode == OpCodes.Ldarg_0);
        var monitorPrefix = monitor.Body.Instructions.TakeWhile(i => i != monitorEntry).ToArray();
        if (monitorPrefix.Count(i => i.Operand is MethodReference m && m.Name == "TryGetRunState") != 1
            || monitorPrefix.Count(i => i.Operand is MethodReference m && m.Name == "get_Count") != 1
            || monitorPrefix.Count(i => i.OpCode == OpCodes.Cgt) != 1
            || monitorPrefix.Last().OpCode.Code is not (Code.Br or Code.Br_S)
            || monitorPrefix.Last().Operand is not Instruction exit || exit.OpCode != OpCodes.Ret)
            throw new InvalidOperationException("Unexpected multiplayer visibility-monitor guard.");
        foreach (var instruction in monitorPrefix) monitor.Body.GetILProcessor().Remove(instruction);
        Widen(monitor);
        var click = Logic("OnRefreshOptionsPressed");
        var entry = click.Body.Instructions[0]; var edit = click.Body.GetILProcessor();
        edit.InsertBefore(entry, Instruction.Create(OpCodes.Ldarg_0));
        edit.InsertBefore(entry, Instruction.Create(OpCodes.Call, Feature("CanPress")));
        edit.InsertBefore(entry, Instruction.Create(OpCodes.Brtrue, entry));
        edit.InsertBefore(entry, Instruction.Create(OpCodes.Ret));
        Widen(click);
        // Keep the repository's act/unlock logic intact and filter only its
        // completed pool. Every solo/host/lookup caller uses the wrapper.
        var rawPool = Logic("BuildRefreshableAncientsForCurrentAct");
        var poolCalls = Types(mod).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Where(i => i.Operand == rawPool).ToArray();
        rawPool.Name = "BuildRefreshableAncientsUnfiltered";
        var pool = new MethodDefinition("BuildRefreshableAncientsForCurrentAct", rawPool.Attributes, rawPool.ReturnType);
        foreach (var p in rawPool.Parameters) pool.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
        logic.Methods.Add(pool);
        var pi = pool.Body.GetILProcessor();
        pi.Emit(OpCodes.Ldarg_0); pi.Emit(OpCodes.Ldarg_1); pi.Emit(OpCodes.Ldarg_0); pi.Emit(OpCodes.Ldarg_1);
        pi.Emit(OpCodes.Call, rawPool); pi.Emit(OpCodes.Call, Ancient("Filter")); pi.Emit(OpCodes.Castclass, pool.ReturnType); pi.Emit(OpCodes.Ret);
        foreach (var call in poolCalls) call.Operand = pool;
        if (poolCalls.Length != 5) throw new InvalidOperationException("Unexpected Ancient pool caller count: " + poolCalls.Length);
        var ancientClick = Logic("OnRefreshAncientPressed");
        var ancientEntry = ancientClick.Body.Instructions[0]; var ac = ancientClick.Body.GetILProcessor();
        ac.InsertBefore(ancientEntry, Instruction.Create(OpCodes.Ldarg_0));
        ac.InsertBefore(ancientEntry, Instruction.Create(OpCodes.Call, Ancient("CanPress")));
        ac.InsertBefore(ancientEntry, Instruction.Create(OpCodes.Brtrue, ancientEntry));
        ac.InsertBefore(ancientEntry, Instruction.Create(OpCodes.Ret));
        Widen(ancientClick);
        var replaceGuard = Logic("TryReplaceAncientSafely");
        var replaceEntry = replaceGuard.Body.Instructions[0]; var rg = replaceGuard.Body.GetILProcessor();
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Ldarg_2));
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Ldarg_3));
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Call, Ancient("CanReplace")));
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Brtrue, replaceEntry));
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Ldc_I4_0));
        rg.InsertBefore(replaceEntry, Instruction.Create(OpCodes.Ret));
        var replaceCall = replaceGuard.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "ReplaceAncientForSinglePlayer");
        var old = Instruction.Create(OpCodes.Ldarg_2); var replacement = Instruction.Create(OpCodes.Ldarg_3);
        var commit = Instruction.Create(OpCodes.Call, Ancient("CommitIfSuccessful"));
        rg.InsertAfter(replaceCall, old); rg.InsertAfter(old, replacement); rg.InsertAfter(replacement, commit);
        Widen(replaceGuard);
        var cancel = Logic("HideButtonsIfCancelPressed");
        var cancelEntry = cancel.Body.Instructions[0]; var ci = cancel.Body.GetILProcessor();
        ci.InsertBefore(cancelEntry, Instruction.Create(OpCodes.Call, Catalog("ShouldSuppressCancel")));
        ci.InsertBefore(cancelEntry, Instruction.Create(OpCodes.Brfalse, cancelEntry));
        ci.InsertBefore(cancelEntry, Instruction.Create(OpCodes.Ret));
        Widen(cancel);
        var config = Types(mod).Single(t => t.Name == "ModConfigBridge");
        BeforeReturn(config.Methods.Single(m => m.Name == "BuildEntries"), (il, ret) =>
        {
            var call = Instruction.Create(OpCodes.Call, Settings("ExtendEntries"));
            il.InsertAfter(ret, call); il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
        });
        foreach (string name in new[] { "GetOptionsCost", "GetAncientCost" })
        {
            var cost = Logic(name); cost.Body = new MethodBody(cost);
            var body = cost.Body.GetILProcessor();
            body.Emit(name == "GetAncientCost" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Conv_I4); body.Emit(OpCodes.Call, Settings("Cost")); body.Emit(OpCodes.Ret);
        }
        BeforeReturn(config.Methods.Single(m => m.Name == "L"), (il, ret) =>
        {
            var call = Instruction.Create(OpCodes.Call, Feature("AddRussianLabels"));
            il.InsertAfter(ret, call); il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
        });
        var register = config.Methods.Single(m => m.Name == "Register");
        // Some ModConfig versions expose saved values only after Register.
        BeforeReturn(register, (il, ret) =>
        {
            var reload = Instruction.Create(OpCodes.Call, Settings("ReloadBindings"));
            il.InsertAfter(ret, reload); il.InsertAfter(reload, Instruction.Create(OpCodes.Ret));
        });
        var labelDictionary = register.Body.Variables.Single(v => v.VariableType.FullName == "System.Collections.Generic.Dictionary`2<System.String,System.String>");
        Instruction? store = null;
        foreach (var i in register.Body.Instructions)
        {
            int index = i.OpCode.Code switch { Code.Stloc_0 => 0, Code.Stloc_1 => 1, Code.Stloc_2 => 2, Code.Stloc_3 => 3, Code.Stloc or Code.Stloc_S => ((VariableDefinition)i.Operand).Index, _ => -1 };
            if (index == labelDictionary.Index) { store = i; break; }
        }
        if (store == null) throw new InvalidOperationException("ModConfig title dictionary initializer was not found.");
        register.Body.GetILProcessor().InsertBefore(store, Instruction.Create(OpCodes.Call, Feature("AddRussianLabels")));
        Widen(register);
        var info = mod.Assembly.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute");
        info.ConstructorArguments[0] = new CustomAttributeArgument(mod.TypeSystem.String, "1.4.3+unofficial.sts2.0.111.0.compat7.multiplayer-hotkeys.ru");
        mod.Mvid = Guid.NewGuid();
        if (mod.GetTypeReferences().Any(t => t.Scope?.Name == "RuntimeFeatures")) throw new InvalidOperationException("Unmerged runtime reference.");
        Console.WriteLine("Merged unique options/Ancients, view-only visual catalogs, modal/cancel guards and Russian localization.");
    }
}
