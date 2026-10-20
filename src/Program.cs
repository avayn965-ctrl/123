using Mono.Cecil;
using Mono.Cecil.Cil;

static IEnumerable<TypeDefinition> Types(ModuleDefinition m) => m.Types.SelectMany(Expand);
static IEnumerable<TypeDefinition> Expand(TypeDefinition t) => new[] {t}.Concat(t.NestedTypes.SelectMany(Expand));
static string TypeKey(TypeReference t) => t switch {
    GenericParameter p => (p.Type == GenericParameterType.Method ? "!!" : "!") + p.Position,
    GenericInstanceType g => TypeKey(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(TypeKey)) + ">",
    ByReferenceType r => TypeKey(r.ElementType) + "&",
    ArrayType a => TypeKey(a.ElementType) + "[" + new string(',', a.Rank-1) + "]",
    PointerType p => TypeKey(p.ElementType) + "*",
    _ => t.FullName
};
static string MethodKey(MethodReference m) {
    if (m is GenericInstanceMethod g) m = g.ElementMethod;
    return m.Name + "`" + m.GenericParameters.Count + "(" + string.Join(",", m.Parameters.Select(p=>TypeKey(p.ParameterType))) + "):" + TypeKey(m.ReturnType) + ":" + m.HasThis;
}
static bool IsGame(TypeReference t) => t.GetElementType().Scope?.Name == "sts2";
static IEnumerable<MemberReference> Members(ModuleDefinition m) => Types(m).SelectMany(t=>t.Methods).Where(x=>x.HasBody)
    .SelectMany(x=>x.Body.Instructions).Select(x=>x.Operand).OfType<MemberReference>()
    .Where(x=>x.DeclaringType != null && IsGame(x.DeclaringType)).DistinctBy(x=>x.FullName);
static List<string> Audit(ModuleDefinition mod, ModuleDefinition game) {
    var types=Types(game).ToDictionary(t=>t.FullName);
    var problems=new List<string>();
    foreach(var member in Members(mod)) {
        if (!types.TryGetValue(member.DeclaringType.GetElementType().FullName,out var t)) {
            problems.Add("Missing type: "+member.FullName); continue;
        }
        var found=false;
        var seen=new HashSet<string>();
        while(t != null && seen.Add(t.FullName)) {
            found = member switch {
                MethodReference mr => t.Methods.Any(x=>MethodKey(x)==MethodKey(mr)),
                FieldReference fr => t.Fields.Any(x=>x.Name==fr.Name && TypeKey(x.FieldType)==TypeKey(fr.FieldType)),
                _ => true
            };
            if(found || t.BaseType == null)break;
            types.TryGetValue(t.BaseType.GetElementType().FullName, out t);
        }
        if(!found)problems.Add("Missing member: "+member.FullName);
    }
    return problems;
}

static void WidenBranches(MethodDefinition method) {
    foreach(var i in method.Body.Instructions) i.OpCode=i.OpCode.Code switch {
        Code.Br_S=>OpCodes.Br, Code.Brfalse_S=>OpCodes.Brfalse, Code.Brtrue_S=>OpCodes.Brtrue,
        Code.Beq_S=>OpCodes.Beq, Code.Bge_S=>OpCodes.Bge, Code.Bge_Un_S=>OpCodes.Bge_Un,
        Code.Bgt_S=>OpCodes.Bgt, Code.Bgt_Un_S=>OpCodes.Bgt_Un, Code.Ble_S=>OpCodes.Ble,
        Code.Ble_Un_S=>OpCodes.Ble_Un, Code.Blt_S=>OpCodes.Blt, Code.Blt_Un_S=>OpCodes.Blt_Un,
        Code.Bne_Un_S=>OpCodes.Bne_Un, Code.Leave_S=>OpCodes.Leave, _=>i.OpCode
    };
}

static void Patch(ModuleDefinition mod, ModuleDefinition game, string output) {
    var originalProblems=Audit(mod,game);
    if(originalProblems.Count!=1 || !originalProblems[0].Contains("EventModel::BeginEvent("))
        throw new InvalidOperationException("Unexpected source compatibility findings; refusing to patch.");
    var logic=Types(mod).Single(t=>t.Name=="AncientRefreshDualButtonsLogic");
    var core=logic.Methods.Single(m=>m.Name=="ReplaceAncientForSinglePlayer");
    var sync=Types(game).Single(t=>t.FullName=="MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer");
    var eventModel=Types(game).Single(t=>t.FullName=="MegaCrit.Sts2.Core.Models.EventModel");
    var combatField=sync.Fields.Single(f=>f.Name=="_combatSynchronizer");
    var begin=eventModel.Methods.Single(m=>m.Name=="BeginEvent" && m.Parameters.Count==3);
    if(begin.Parameters[1].ParameterType.FullName!=combatField.FieldType.FullName)
        throw new InvalidOperationException("Unexpected combat synchronizer type.");
    var getCombat=new MethodDefinition("GetCombatSynchronizerForRefresh",MethodAttributes.Private|MethodAttributes.Static|MethodAttributes.HideBySig,mod.ImportReference(combatField.FieldType));
    getCombat.Parameters.Add(new ParameterDefinition("sync",ParameterAttributes.None,mod.ImportReference(sync)));
    logic.Methods.Add(getCombat);
    var il=getCombat.Body.GetILProcessor();
    il.Emit(OpCodes.Ldtoken,mod.ImportReference(sync));
    il.Emit(OpCodes.Call,mod.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle")!));
    il.Emit(OpCodes.Ldstr,"_combatSynchronizer");
    il.Emit(OpCodes.Ldc_I4,(int)(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic));
    il.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(Type).GetMethod("GetField",new[]{typeof(string),typeof(System.Reflection.BindingFlags)})!));
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(System.Reflection.FieldInfo).GetMethod("GetValue")!));
    il.Emit(OpCodes.Castclass,mod.ImportReference(combatField.FieldType));
    il.Emit(OpCodes.Ret);
    var beginCall=core.Body.Instructions.Single(i=>i.Operand is MethodReference mr && mr.DeclaringType.FullName==eventModel.FullName && mr.Name=="BeginEvent");
    if(beginCall.Previous.OpCode!=OpCodes.Ldloc_S)throw new InvalidOperationException("Unexpected argument-loading sequence.");
    var edit=core.Body.GetILProcessor();
    edit.InsertBefore(beginCall.Previous,Instruction.Create(OpCodes.Ldarg_0));
    edit.InsertBefore(beginCall.Previous,Instruction.Create(OpCodes.Call,getCombat));
    beginCall.Operand=mod.ImportReference(begin);
    WidenBranches(core);

    // Put the failure guard outside the implementation, so its catch also handles
    // JIT/member-resolution failures before the implementation body starts.
    var guard=new MethodDefinition("TryReplaceAncientSafely",MethodAttributes.Private|MethodAttributes.Static|MethodAttributes.HideBySig,mod.TypeSystem.Boolean);
    foreach(var p in core.Parameters)guard.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
    logic.Methods.Add(guard);
    guard.Body.InitLocals=true;
    var result=new VariableDefinition(mod.TypeSystem.Boolean);
    var error=new VariableDefinition(mod.ImportReference(typeof(Exception)));
    guard.Body.Variables.Add(result);guard.Body.Variables.Add(error);
    var print=(MethodReference)core.Body.Instructions.First(i=>i.Operand is MethodReference mr && mr.DeclaringType.FullName=="Godot.GD" && mr.Name=="Print").Operand;
    var g=guard.Body.GetILProcessor();
    var start=Instruction.Create(OpCodes.Ldarg_0);
    var handler=Instruction.Create(OpCodes.Stloc,error);
    var end=Instruction.Create(OpCodes.Ldloc,result);
    g.Append(start);g.Emit(OpCodes.Ldarg_1);g.Emit(OpCodes.Ldarg_2);g.Emit(OpCodes.Ldarg_3);
    g.Emit(OpCodes.Call,core);g.Emit(OpCodes.Stloc,result);g.Emit(OpCodes.Leave,end);
    g.Append(handler);
    g.Emit(OpCodes.Ldstr,"[RefreshAncient Compat 0.111.0] Replacement failed; caller will refund gold: ");
    g.Emit(OpCodes.Ldloc,error);
    g.Emit(OpCodes.Callvirt,mod.ImportReference(typeof(Exception).GetMethod("ToString",Type.EmptyTypes)!));
    g.Emit(OpCodes.Call,mod.ImportReference(typeof(string).GetMethod("Concat",new[]{typeof(string),typeof(string)})!));
    g.Emit(OpCodes.Call,print);
    g.Emit(OpCodes.Ldc_I4_0);g.Emit(OpCodes.Stloc,result);g.Emit(OpCodes.Leave,end);
    g.Append(end);g.Emit(OpCodes.Ret);
    guard.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch){
        CatchType=mod.ImportReference(typeof(Exception)),TryStart=start,TryEnd=handler,HandlerStart=handler,HandlerEnd=end
    });
    var guarded=0;
    foreach(var m in Types(mod).SelectMany(t=>t.Methods).Where(m=>m.HasBody && m!=guard))
        foreach(var i in m.Body.Instructions)
            if(i.Operand==core){i.Operand=guard;guarded++;}
    if(guarded!=2)throw new InvalidOperationException($"Expected 2 replacement callers; found {guarded}.");

    var target=Types(mod).Single(t=>t.Name=="IndependentRefreshMultiplayerSyncCtorPatch").Methods.Single(m=>m.Name=="TargetMethod");
    var accessCtor=(MethodReference)target.Body.Instructions.Single(i=>i.Operand is MethodReference mr && mr.DeclaringType.FullName=="HarmonyLib.AccessTools" && mr.Name=="Constructor").Operand;
    var typeFromHandle=(MethodReference)target.Body.Instructions.First(i=>i.Operand is MethodReference mr && mr.Name=="GetTypeFromHandle").Operand;
    var ctor=sync.Methods.Single(m=>m.IsConstructor && !m.IsStatic);
    if(ctor.Parameters.Count!=6 || ctor.Parameters[3].ParameterType.FullName!="MegaCrit.Sts2.Core.Runs.IRunState" || ctor.Parameters[5].ParameterType.MetadataType!=MetadataType.UInt64)
        throw new InvalidOperationException("Unexpected EventSynchronizer constructor.");
    target.Body=new Mono.Cecil.Cil.MethodBody(target);
    var t=target.Body.GetILProcessor();
    t.Emit(OpCodes.Ldtoken,mod.ImportReference(sync));t.Emit(OpCodes.Call,typeFromHandle);
    t.Emit(OpCodes.Ldc_I4,ctor.Parameters.Count);t.Emit(OpCodes.Newarr,typeFromHandle.ReturnType);
    for(int n=0;n<ctor.Parameters.Count;n++) {
        t.Emit(OpCodes.Dup);t.Emit(OpCodes.Ldc_I4,n);t.Emit(OpCodes.Ldtoken,mod.ImportReference(ctor.Parameters[n].ParameterType));
        t.Emit(OpCodes.Call,typeFromHandle);t.Emit(OpCodes.Stelem_Ref);
    }
    t.Emit(OpCodes.Ldc_I4_0);t.Emit(OpCodes.Call,accessCtor);t.Emit(OpCodes.Ret);

    var problems=Audit(mod,game);
    if(problems.Count!=0)throw new InvalidOperationException(string.Join("\n",problems));
    var info=mod.Assembly.CustomAttributes.Single(a=>a.AttributeType.FullName=="System.Reflection.AssemblyInformationalVersionAttribute");
    info.ConstructorArguments[0]=new CustomAttributeArgument(mod.TypeSystem.String,"1.4.3+unofficial.sts2.0.111.0.compat1");
    mod.Mvid=Guid.NewGuid();
    mod.Write(output);
    Console.WriteLine($"Patched {output}; {Members(mod).Count()} game references resolved; 2 replacement callers guarded.");
}

if(args.Length<3)throw new ArgumentException("audit|il MOD GAME [method-name]");
using var mod=ModuleDefinition.ReadModule(args[1]);
using var game=ModuleDefinition.ReadModule(args[2]);
if(args[0]=="audit") {
    var problems=Audit(mod,game);
    Console.WriteLine($"Game references: {Members(mod).Count()}, unresolved: {problems.Count}");
    foreach(var p in problems)Console.WriteLine(p);
    foreach(var name in new[]{"MegaCrit.Sts2.Core.Models.EventModel","MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer"}) {
        var t=Types(game).Single(t=>t.FullName==name);
        foreach(var m in t.Methods.Where(x=>x.Name is "BeginEvent" or ".ctor"))Console.WriteLine(m.FullName);
    }
} else if(args[0]=="il") {
    foreach(var m in Types(mod).SelectMany(t=>t.Methods).Where(m=>m.Name==args[3])) {
        Console.WriteLine(m.FullName);
        foreach(var v in m.Body.Variables)Console.WriteLine($"local {v.Index}: {v.VariableType}");
        foreach(var i in m.Body.Instructions)Console.WriteLine(i);
        foreach(var e in m.Body.ExceptionHandlers)Console.WriteLine($"{e.HandlerType} try {e.TryStart} - {e.TryEnd} handler {e.HandlerStart} - {e.HandlerEnd}");
    }
} else if(args[0]=="patch") {
    Patch(mod,game,args[3]);
} else if(args[0]=="verify") {
    using var original=ModuleDefinition.ReadModule(args[3]);
    Verification.Run(original,mod,game);
} else if(args[0]=="ancients") {
    foreach(var type in Types(game).Where(t=>t.BaseType?.Name=="AncientEventModel"))Console.WriteLine(type.FullName);
} else if(args[0]=="members") {
    foreach(var type in Types(game).Where(t=>t.Name==args[3])) {
        Console.WriteLine(type.FullName);
        foreach(var p in type.Properties)Console.WriteLine(p.FullName);
        foreach(var f in type.Fields)Console.WriteLine(f.FullName);
        foreach(var m in type.Methods.Where(m=>!m.IsGetter&&!m.IsSetter))Console.WriteLine(m.FullName);
    }
} else if(args[0]=="features") {
    if(Audit(mod,game).Count!=0)throw new InvalidOperationException("Input must have the v0.111.0 compatibility fix applied first.");
    FeaturesPatch.Apply(mod,args[4]);
    var problems=Audit(mod,game);
    if(problems.Count>0)throw new InvalidOperationException(string.Join("\n",problems));
    mod.Write(args[3]);
    Console.WriteLine("Wrote "+args[3]);
} else if(args[0]=="verify-features") {
    using var baseline=ModuleDefinition.ReadModule(args[3]);
    var problems=Audit(mod,game);
    if(problems.Count>0)throw new InvalidOperationException(string.Join("\n",problems));
    Console.WriteLine($"PASS: {Members(mod).Count()} direct game API references resolve.");
    FeaturesVerification.Run(baseline,mod,game);
} else throw new ArgumentException("Unknown command");
