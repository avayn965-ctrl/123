using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class RuntimeMerge
{
    public static TypeDefinition Merge(ModuleDefinition target, ModuleDefinition source)
    {
        var types = new Dictionary<string, TypeDefinition>();
        var methods = new Dictionary<string, MethodDefinition>();
        var fields = new Dictionary<string, FieldDefinition>();
        IEnumerable<TypeDefinition> Expand(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Expand));
        var all = source.Types.Where(t => t.Name != "<Module>").SelectMany(Expand).ToList();
        foreach (var old in all)
        {
            var copy = new TypeDefinition(old.Namespace, old.Name, old.Attributes);
            if (old.DeclaringType == null) target.Types.Add(copy);
            else types[old.DeclaringType.FullName].NestedTypes.Add(copy);
            types.Add(old.FullName, copy);
        }
        TypeReference Type(TypeReference type)
        {
            if (types.TryGetValue(type.FullName, out var local)) return local;
            if (type is GenericParameter) return type;
            if (type is GenericInstanceType generic)
            {
                var copy = new GenericInstanceType(Type(generic.ElementType));
                foreach (var argument in generic.GenericArguments) copy.GenericArguments.Add(Type(argument));
                return copy;
            }
            if (type is ByReferenceType reference) return new ByReferenceType(Type(reference.ElementType));
            if (type is ArrayType array) return new ArrayType(Type(array.ElementType), array.Rank);
            if (type is PointerType pointer) return new PointerType(Type(pointer.ElementType));
            if (type is RequiredModifierType required) return new RequiredModifierType(Type(required.ModifierType), Type(required.ElementType));
            if (type is OptionalModifierType optional) return new OptionalModifierType(Type(optional.ModifierType), Type(optional.ElementType));
            return target.ImportReference(type);
        }
        foreach (var old in all)
        {
            var copy = types[old.FullName];
            copy.BaseType = old.BaseType == null ? null : Type(old.BaseType);
            foreach (var iface in old.Interfaces) copy.Interfaces.Add(new InterfaceImplementation(Type(iface.InterfaceType)));
            foreach (var field in old.Fields)
            {
                var added = new FieldDefinition(field.Name, field.Attributes, Type(field.FieldType));
                if (field.HasConstant) added.Constant = field.Constant;
                if (field.InitialValue.Length > 0) added.InitialValue = field.InitialValue.ToArray();
                copy.Fields.Add(added); fields.Add(field.FullName, added);
            }
            foreach (var method in old.Methods)
            {
                if (method.HasGenericParameters) throw new NotSupportedException("Generic runtime methods must be mapped explicitly.");
                var added = new MethodDefinition(method.Name, method.Attributes, Type(method.ReturnType)) { ImplAttributes = method.ImplAttributes };
                foreach (var p in method.Parameters) added.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, Type(p.ParameterType)));
                copy.Methods.Add(added); methods.Add(method.FullName, added);
            }
        }
        MethodReference Method(MethodReference method)
        {
            if (method is GenericInstanceMethod generic)
            {
                var copy = new GenericInstanceMethod(Method(generic.ElementMethod));
                foreach (var argument in generic.GenericArguments) copy.GenericArguments.Add(Type(argument));
                return copy;
            }
            if (methods.TryGetValue(method.FullName, out var local)) return local;
            var added = new MethodReference(method.Name, Type(method.ReturnType), Type(method.DeclaringType))
            { HasThis = method.HasThis, ExplicitThis = method.ExplicitThis, CallingConvention = method.CallingConvention };
            foreach (var p in method.GenericParameters) added.GenericParameters.Add(new GenericParameter(p.Name, added));
            foreach (var p in method.Parameters) added.Parameters.Add(new ParameterDefinition(Type(p.ParameterType)));
            return added;
        }
        FieldReference Field(FieldReference field) => fields.TryGetValue(field.FullName, out var local) ? local
            : new FieldReference(field.Name, Type(field.FieldType), Type(field.DeclaringType));
        foreach (var oldType in all)
        foreach (var old in oldType.Methods)
        {
            var copy = methods[old.FullName];
            if (!old.HasBody) continue;
            copy.Body.InitLocals = old.Body.InitLocals;
            foreach (var variable in old.Body.Variables) copy.Body.Variables.Add(new VariableDefinition(Type(variable.VariableType)));
            var instructions = new Dictionary<Instruction, Instruction>();
            foreach (var oldInstruction in old.Body.Instructions)
            {
                var added = Instruction.Create(OpCodes.Nop); added.OpCode = oldInstruction.OpCode;
                copy.Body.Instructions.Add(added); instructions.Add(oldInstruction, added);
            }
            foreach (var oldInstruction in old.Body.Instructions)
                instructions[oldInstruction].Operand = oldInstruction.Operand switch
                {
                    Instruction branch => instructions[branch], Instruction[] branches => branches.Select(b => instructions[b]).ToArray(),
                    MethodReference method => Method(method), FieldReference field => Field(field), TypeReference type => Type(type),
                    VariableDefinition variable => copy.Body.Variables[variable.Index], ParameterDefinition parameter => copy.Parameters[parameter.Index],
                    _ => oldInstruction.Operand
                };
            foreach (var handler in old.Body.ExceptionHandlers)
                copy.Body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
                {
                    CatchType = handler.CatchType == null ? null : Type(handler.CatchType),
                    TryStart = instructions[handler.TryStart], TryEnd = handler.TryEnd == null ? null : instructions[handler.TryEnd],
                    HandlerStart = instructions[handler.HandlerStart], HandlerEnd = handler.HandlerEnd == null ? null : instructions[handler.HandlerEnd],
                    FilterStart = handler.FilterStart == null ? null : instructions[handler.FilterStart]
                });
        }
        return types["RefreshAncient.Compat2.UniqueOptions"];
    }
}
