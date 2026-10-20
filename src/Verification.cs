using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Verification
{
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static IEnumerable<TypeDefinition> Types(ModuleDefinition m) => m.Types.SelectMany(Expand);
    static IEnumerable<TypeDefinition> Expand(TypeDefinition t) => new[] {t}.Concat(t.NestedTypes.SelectMany(Expand));
    static string Fingerprint(MethodDefinition m) => !m.HasBody ? "<no body>" : string.Join("\n", m.Body.Instructions.Select(i=>
        i.OpCode + " " + (i.Operand switch {
            Instruction target => "@"+m.Body.Instructions.IndexOf(target),
            Instruction[] targets => string.Join(",",targets.Select(t=>m.Body.Instructions.IndexOf(t))),
            VariableDefinition v => "local "+v.Index,
            ParameterDefinition p => "arg "+p.Index,
            _ => i.Operand?.ToString() ?? ""
        })));

    public static void Run(ModuleDefinition original, ModuleDefinition patched, ModuleDefinition game)
    {
        var allowed=new HashSet<string>{"RefreshAncientSingleplayer","ApplyAncientRefreshResult","ReplaceAncientForSinglePlayer","TargetMethod"};
        var patchedMethods=Types(patched).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
        var changed=new List<string>();
        foreach(var old in Types(original).SelectMany(t=>t.Methods)) {
            Require(patchedMethods.TryGetValue(old.FullName,out var current),"Removed method: "+old.FullName);
            if(Fingerprint(old)!=Fingerprint(current!)) {
                Require(allowed.Contains(old.Name),"Unexpected changed method: "+old.FullName);
                changed.Add(old.Name);
            }
        }
        Require(changed.Count==4,"Expected exactly four existing method bodies to change.");
        Require(Types(patched).Count()==Types(original).Count(),"Unexpected added/removed type.");
        var additions=patchedMethods.Keys.Except(Types(original).SelectMany(t=>t.Methods).Select(m=>m.FullName)).ToList();
        Require(additions.Count==2 && additions.Any(n=>n.Contains("::GetCombatSynchronizerForRefresh(")) && additions.Any(n=>n.Contains("::TryReplaceAncientSafely(")),"Unexpected added methods.");
        foreach(var r in original.Resources.OfType<EmbeddedResource>()) {
            var other=patched.Resources.OfType<EmbeddedResource>().Single(x=>x.Name==r.Name);
            Require(r.GetResourceData().SequenceEqual(other.GetResourceData()),"Changed embedded resource: "+r.Name);
        }
        var logic=Types(patched).Single(t=>t.Name=="AncientRefreshDualButtonsLogic");
        var core=logic.Methods.Single(m=>m.Name=="ReplaceAncientForSinglePlayer");
        var guard=logic.Methods.Single(m=>m.Name=="TryReplaceAncientSafely");
        var helper=logic.Methods.Single(m=>m.Name=="GetCombatSynchronizerForRefresh");
        var target=Types(patched).Single(t=>t.Name=="IndependentRefreshMultiplayerSyncCtorPatch").Methods.Single(m=>m.Name=="TargetMethod");
        var beginInstruction=core.Body.Instructions.Single(i=>i.Operand is MethodReference m && m.Name=="BeginEvent");
        Require(beginInstruction.Previous.OpCode==OpCodes.Ldloc_S && beginInstruction.Previous.Previous.Operand is MethodReference h && h.Name==helper.Name && beginInstruction.Previous.Previous.Previous.OpCode==OpCodes.Ldarg_0,"Bad combat-synchronizer argument order.");
        var sync=Types(game).Single(t=>t.Name=="EventSynchronizer");
        var ctor=sync.Methods.Single(m=>m.IsConstructor&&!m.IsStatic);
        var tokens=target.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldtoken).Select(i=>((TypeReference)i.Operand).FullName).ToArray();
        Require(tokens.SequenceEqual(new[]{sync.FullName}.Concat(ctor.Parameters.Select(p=>p.ParameterType.FullName))),"Constructor selector does not match supplied game DLL.");
        foreach(var (type,fields,methods) in new[]{
            ("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer",new[]{"_events","_combatSynchronizer","_messageBuffer","_netService"},Array.Empty<string>()),
            ("MegaCrit.Sts2.Core.Models.EventModel",new[]{"_currentOptions"},Array.Empty<string>()),
            ("MegaCrit.Sts2.Core.Models.AncientEventModel",new[]{"_generatedOptions"},new[]{"GenerateInitialOptionsWrapper"}),
            ("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom",new[]{"_event","_eventContainer","_connectedOptions","_isPreFinished"},new[]{"SetupLayout","SetOptions","RefreshEventState","OnEnteringEventCombat"})
        }) {
            var definition=Types(game).Single(t=>t.FullName==type);
            foreach(var field in fields)Require(definition.Fields.Any(f=>f.Name==field),"Missing reflected field: "+type+"."+field);
            foreach(var method in methods)Require(definition.Methods.Any(m=>m.Name==method),"Missing reflected method: "+type+"."+method);
        }
        var room=Types(game).Single(t=>t.FullName=="MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom");
        Require(room.Methods.Any(m=>m.Name=="SetupLayout"&&m.Parameters.Count==0&&m.ReturnType.FullName=="System.Threading.Tasks.Task"),"Unexpected SetupLayout signature.");
        Require(room.Methods.Any(m=>m.Name=="SetOptions"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.Name=="EventModel"),"Unexpected SetOptions signature.");
        var container=Types(game).Single(t=>t.FullName==room.Fields.Single(f=>f.Name=="_eventContainer").FieldType.FullName);
        Require(container.Methods.Any(m=>m.Name=="SetCurrentScene"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="Godot.Control"),"Unexpected SetCurrentScene signature.");
        foreach(var name in new[]{"RefreshAncientSingleplayer","ApplyAncientRefreshResult"}) {
            var method=logic.Methods.Single(m=>m.Name==name);
            Require(method.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name==guard.Name),"Caller bypasses replacement guard: "+name);
            Require(method.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="TryAddGold"),"Refund code missing: "+name);
        }
        Console.WriteLine("PASS: exactly four existing method bodies changed; two helpers added; embedded resources preserved.");
        Console.WriteLine("PASS: new constructor parameter list and BeginEvent argument order match the supplied game DLL.");
        Console.WriteLine("PASS: reflected fields and methods needed for event replacement/options exist with the expected room API signatures.");
        RunIsolatedHarness(patched,core,guard,helper,target,beginInstruction,ctor);
    }

    // Execute the actual patched IL against small API substitutes. No uploaded
    // game or mod code is loaded or executed, and no game runtime is simulated.
    static void RunIsolatedHarness(ModuleDefinition patched, MethodDefinition core, MethodDefinition guard, MethodDefinition helper, MethodDefinition target, Instruction begin, MethodDefinition gameCtor)
    {
        using var test=ModuleDefinition.CreateModule("RefreshAncient.Compatibility.Validation",ModuleKind.Dll);
        var map=new Dictionary<string,TypeDefinition>();
        TypeDefinition Stub(TypeReference source, bool isInterface=false) {
            if(map.TryGetValue(source.FullName,out var existing))return existing;
            var t=new TypeDefinition(source.Namespace,source.Name,TypeAttributes.Public|(isInterface ? TypeAttributes.Interface|TypeAttributes.Abstract : TypeAttributes.Class),isInterface?null:test.TypeSystem.Object);
            test.Types.Add(t);map.Add(source.FullName,t);return t;
        }
        MethodDefinition DefaultCtor(TypeDefinition t) {
            var c=new MethodDefinition(".ctor",MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.RTSpecialName|MethodAttributes.HideBySig,test.TypeSystem.Void);
            t.Methods.Add(c);var il=c.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,test.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));il.Emit(OpCodes.Ret);return c;
        }
        var stubSync=Stub(gameCtor.DeclaringType);
        var gameTypes=gameCtor.Parameters.Select(p=>p.ParameterType).Where(t=>t.Namespace.StartsWith("MegaCrit.")).ToList();
        foreach(var t in gameTypes)Stub(t,t.Name.StartsWith("I"));
        var stubCombat=Stub(helper.ReturnType);var combatCtor=DefaultCtor(stubCombat);
        var stubPlayer=Stub(core.Parameters[1].ParameterType);DefaultCtor(stubPlayer);
        Stub(core.Parameters[2].ParameterType);
        var stubEvent=Stub(((MethodReference)begin.Operand).DeclaringType);DefaultCtor(stubEvent);
        var combatField=new FieldDefinition("_combatSynchronizer",FieldAttributes.Private,stubCombat);stubSync.Fields.Add(combatField);
        TypeReference MapType(TypeReference type) {
            if(map.TryGetValue(type.FullName,out var local))return local;
            return type is ArrayType a ? new ArrayType(MapType(a.ElementType),a.Rank) : test.ImportReference(type);
        }
        var ctor=new MethodDefinition(".ctor",MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.RTSpecialName|MethodAttributes.HideBySig,test.TypeSystem.Void);
        foreach(var p in gameCtor.Parameters)ctor.Parameters.Add(new ParameterDefinition(p.Name,ParameterAttributes.None,MapType(p.ParameterType)));
        stubSync.Methods.Add(ctor);var ci=ctor.Body.GetILProcessor();ci.Emit(OpCodes.Ldarg_0);ci.Emit(OpCodes.Call,test.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));ci.Emit(OpCodes.Ldarg_0);ci.Emit(OpCodes.Newobj,combatCtor);ci.Emit(OpCodes.Stfld,combatField);ci.Emit(OpCodes.Ret);
        var seenPlayer=new FieldDefinition("SeenPlayer",FieldAttributes.Public,stubPlayer);stubEvent.Fields.Add(seenPlayer);
        var seenCombat=new FieldDefinition("SeenCombat",FieldAttributes.Public,stubCombat);stubEvent.Fields.Add(seenCombat);
        var seenFlag=new FieldDefinition("SeenPreFinished",FieldAttributes.Public,test.TypeSystem.Boolean);stubEvent.Fields.Add(seenFlag);
        var stubBegin=new MethodDefinition("BeginEvent",MethodAttributes.Public|MethodAttributes.HideBySig,test.ImportReference(typeof(Task)));
        stubBegin.Parameters.Add(new ParameterDefinition(stubPlayer));stubBegin.Parameters.Add(new ParameterDefinition(stubCombat));stubBegin.Parameters.Add(new ParameterDefinition(test.TypeSystem.Boolean));stubEvent.Methods.Add(stubBegin);
        var bi=stubBegin.Body.GetILProcessor();
        foreach(var pair in new[]{(1,seenPlayer),(2,seenCombat),(3,seenFlag)}) {bi.Emit(OpCodes.Ldarg_0);bi.Emit(OpCodes.Ldarg,stubBegin.Parameters[pair.Item1-1]);bi.Emit(OpCodes.Stfld,pair.Item2);}
        bi.Emit(OpCodes.Call,test.ImportReference(typeof(Task).GetProperty("CompletedTask")!.GetMethod!));bi.Emit(OpCodes.Ret);
        var harness=new TypeDefinition("Tests","Harness",TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed,test.TypeSystem.Object);test.Types.Add(harness);
        var shouldThrow=new FieldDefinition("ShouldThrow",FieldAttributes.Public|FieldAttributes.Static,test.TypeSystem.Boolean);harness.Fields.Add(shouldThrow);
        var replacementResult=new FieldDefinition("ReplacementResult",FieldAttributes.Public|FieldAttributes.Static,test.TypeSystem.Boolean);harness.Fields.Add(replacementResult);
        MethodDefinition CoreStub(string name,bool jitFailure) {
            var m=new MethodDefinition(name,MethodAttributes.Public|MethodAttributes.Static,test.TypeSystem.Boolean);
            foreach(var p in core.Parameters)m.Parameters.Add(new ParameterDefinition(MapType(p.ParameterType)));
            harness.Methods.Add(m);var il=m.Body.GetILProcessor();
            if(jitFailure) {
                il.Emit(OpCodes.Call,new MethodReference("MissingOldBeginEvent",test.TypeSystem.Void,stubEvent){HasThis=false});
                il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ret);
            } else {
                var ok=Instruction.Create(OpCodes.Ldsfld,replacementResult);
                il.Emit(OpCodes.Ldsfld,shouldThrow);il.Emit(OpCodes.Brfalse,ok);il.Emit(OpCodes.Ldstr,"Synthetic replacement failure");il.Emit(OpCodes.Newobj,test.ImportReference(typeof(InvalidOperationException).GetConstructor(new[]{typeof(string)})!));il.Emit(OpCodes.Throw);il.Append(ok);il.Emit(OpCodes.Ret);
            }
            return m;
        }
        var stubCore=CoreStub("ReplacementCore",false);var jitCore=CoreStub("MissingMethodCore",true);
        var findCtor=new MethodDefinition("FindConstructor",MethodAttributes.Public|MethodAttributes.Static,test.ImportReference(typeof(System.Reflection.ConstructorInfo)));
        findCtor.Parameters.Add(new ParameterDefinition(test.ImportReference(typeof(Type))));findCtor.Parameters.Add(new ParameterDefinition(new ArrayType(test.ImportReference(typeof(Type)))));findCtor.Parameters.Add(new ParameterDefinition(test.TypeSystem.Boolean));harness.Methods.Add(findCtor);
        var fc=findCtor.Body.GetILProcessor();fc.Emit(OpCodes.Ldarg_0);fc.Emit(OpCodes.Ldarg_1);fc.Emit(OpCodes.Callvirt,test.ImportReference(typeof(Type).GetMethod("GetConstructor",new[]{typeof(Type[])})!));fc.Emit(OpCodes.Ret);
        MethodDefinition Shell(MethodDefinition source,string name) {
            var m=new MethodDefinition(name,MethodAttributes.Public|MethodAttributes.Static,MapType(source.ReturnType));
            foreach(var p in source.Parameters)m.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));
            harness.Methods.Add(m);return m;
        }
        var copiedHelper=Shell(helper,"GetCombat");var copiedGuard=Shell(guard,"Guard");var copiedJitGuard=Shell(guard,"JitGuard");var copiedTarget=Shell(target,"FindTarget");
        MethodReference MapMethod(MethodReference method,bool useJit=false) {
            if(method.FullName==helper.FullName)return copiedHelper;
            if(method.FullName==core.FullName)return useJit?jitCore:stubCore;
            if(method.Name=="BeginEvent"&&method.DeclaringType.FullName==stubEvent.FullName)return stubBegin;
            if(method.DeclaringType.FullName=="Godot.GD"&&method.Name=="Print")return test.ImportReference(typeof(Console).GetMethod("WriteLine",new[]{typeof(string)})!);
            if(method.DeclaringType.FullName=="HarmonyLib.AccessTools"&&method.Name=="Constructor")return findCtor;
            return test.ImportReference(method);
        }
        void Copy(MethodDefinition source,MethodDefinition dest,bool useJit=false) {
            dest.Body.InitLocals=source.Body.InitLocals;
            foreach(var v in source.Body.Variables)dest.Body.Variables.Add(new VariableDefinition(MapType(v.VariableType)));
            var instructions=new Dictionary<Instruction,Instruction>();
            foreach(var i in source.Body.Instructions){var n=Instruction.Create(OpCodes.Nop);n.OpCode=i.OpCode;dest.Body.Instructions.Add(n);instructions.Add(i,n);}
            foreach(var i in source.Body.Instructions)instructions[i].Operand=i.Operand switch {
                MethodReference m=>MapMethod(m,useJit),TypeReference t=>MapType(t),
                Instruction b=>instructions[b],Instruction[] bs=>bs.Select(b=>instructions[b]).ToArray(),
                VariableDefinition v=>dest.Body.Variables[v.Index],ParameterDefinition p=>dest.Parameters[p.Index],_=>i.Operand
            };
            foreach(var e in source.Body.ExceptionHandlers)dest.Body.ExceptionHandlers.Add(new ExceptionHandler(e.HandlerType){
                CatchType=e.CatchType==null?null:MapType(e.CatchType),TryStart=instructions[e.TryStart],TryEnd=instructions[e.TryEnd],HandlerStart=instructions[e.HandlerStart],HandlerEnd=instructions[e.HandlerEnd]
            });
        }
        Copy(helper,copiedHelper);Copy(guard,copiedGuard);Copy(guard,copiedJitGuard,true);Copy(target,copiedTarget);
        var bridge=new MethodDefinition("Bridge",MethodAttributes.Public|MethodAttributes.Static,test.ImportReference(typeof(Task)));
        foreach(var p in new TypeReference[]{stubEvent,stubPlayer,test.TypeSystem.Boolean,stubSync})bridge.Parameters.Add(new ParameterDefinition(p));
        harness.Methods.Add(bridge);var bridgeIl=bridge.Body.GetILProcessor();
        // Reuse the six actual instructions feeding the patched BeginEvent call,
        // mapping the original locals/arguments to the harness parameters.
        var slice=new List<Instruction>();for(var i=begin;i!=null&&slice.Count<6;i=i.Previous)slice.Insert(0,i);
        Require(slice.Count==6&&slice[0].OpCode==OpCodes.Ldloc_S&&slice[1].OpCode==OpCodes.Ldarg_1,"Unexpected patched call sequence.");
        foreach(var i in slice) {
            if(i==slice[0])bridgeIl.Emit(OpCodes.Ldarg_0);
            else if(i==slice[1])bridgeIl.Emit(OpCodes.Ldarg_1);
            else if(i.OpCode==OpCodes.Ldarg_0)bridgeIl.Emit(OpCodes.Ldarg_3);
            else if(i.OpCode==OpCodes.Ldloc_S)bridgeIl.Emit(OpCodes.Ldarg_2);
            else bridgeIl.Emit(i.OpCode,MapMethod((MethodReference)i.Operand));
        }
        bridgeIl.Emit(OpCodes.Ret);
        using var bytes=new MemoryStream();test.Write(bytes);
        var loaded=System.Reflection.Assembly.Load(bytes.ToArray());
        var api=loaded.GetType("Tests.Harness")!;var syncType=loaded.GetType(stubSync.FullName)!;
        var syncObject=Activator.CreateInstance(syncType,new object?[]{null,null,null,null,7UL,42UL})!;
        var playerObject=Activator.CreateInstance(loaded.GetType(stubPlayer.FullName)!)!;
        var eventObject=Activator.CreateInstance(loaded.GetType(stubEvent.FullName)!)!;
        var combatObject=syncType.GetField("_combatSynchronizer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(syncObject);
        var task=(Task)api.GetMethod("Bridge")!.Invoke(null,new[]{eventObject,playerObject,true,syncObject})!;task.GetAwaiter().GetResult();
        Require(ReferenceEquals(eventObject.GetType().GetField("SeenCombat")!.GetValue(eventObject),combatObject),"Wrong combat synchronizer passed.");
        Require(ReferenceEquals(eventObject.GetType().GetField("SeenPlayer")!.GetValue(eventObject),playerObject),"Wrong player passed.");
        Require((bool)eventObject.GetType().GetField("SeenPreFinished")!.GetValue(eventObject)!,"Pre-finished flag lost.");
        var selected=(System.Reflection.ConstructorInfo)api.GetMethod("FindTarget")!.Invoke(null,null)!;
        Require(selected.DeclaringType==syncType&&selected.GetParameters().Length==6&&selected.GetParameters()[5].ParameterType==typeof(ulong),"Wrong constructor resolved.");
        Require(syncType.GetConstructor(new[]{loaded.GetType(gameCtor.Parameters[0].ParameterType.FullName)!,loaded.GetType(gameCtor.Parameters[1].ParameterType.FullName)!,loaded.GetType(gameCtor.Parameters[2].ParameterType.FullName)!,typeof(ulong),typeof(uint)})==null,"Fixture unexpectedly supports obsolete constructor.");
        var args=new object?[]{syncObject,playerObject,null,null};
        api.GetField("ReplacementResult")!.SetValue(null,true);
        Require((bool)api.GetMethod("Guard")!.Invoke(null,args)!,"Guard failed to preserve success.");
        api.GetField("ReplacementResult")!.SetValue(null,false);
        Require(!(bool)api.GetMethod("Guard")!.Invoke(null,args)!,"Guard failed to preserve failure.");
        api.GetField("ShouldThrow")!.SetValue(null,true);
        Require(!(bool)api.GetMethod("Guard")!.Invoke(null,args)!,"Guard failed to catch runtime exception.");
        Require(!(bool)api.GetMethod("JitGuard")!.Invoke(null,args)!,"Guard failed to catch JIT MissingMethodException.");
        Console.WriteLine("PASS: patched IL passes the same combat synchronizer, player and pre-finished flag to the new BeginEvent API.");
        Console.WriteLine("PASS: patched constructor selector resolves the six-parameter API; the old five-parameter signature is absent.");
        Console.WriteLine("PASS: replacement guard preserves true/false results and catches both runtime exceptions and actual JIT MissingMethodException.");
    }
}
