using Mono.Cecil;
using Mono.Cecil.Cil;

static IEnumerable<TypeDefinition> Expand(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Expand));
static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Expand);
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
if (args.Length != 3) throw new ArgumentException("Audit ADDON.dll GAME_sts2.dll ORIGINAL_GuZhenRen.dll");
using var addon = ModuleDefinition.ReadModule(args[0]);
using var game = ModuleDefinition.ReadModule(args[1]);
using var original = ModuleDefinition.ReadModule(args[2]);
var gameTypes = Types(game).ToDictionary(t => t.FullName);
var methods = Types(addon).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
var calls = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToArray();
var gameCalls = calls.Where(m => m.DeclaringType.Scope.Name == "sts2").DistinctBy(m => m.FullName).ToArray();
foreach (var call in gameCalls)
    Require(gameTypes.TryGetValue(call.DeclaringType.FullName, out var type)
        && type.Methods.Any(m => m.Name == call.Name && m.ReturnType.FullName == call.ReturnType.FullName
            && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(call.Parameters.Select(p => p.ParameterType.FullName))),
        "Missing game API: " + call.FullName);
Require(gameTypes["MegaCrit.Sts2.Core.Localization.LocManager"].Methods.Any(m => m.Name == "TriggerLocaleChange" && m.Parameters.Count == 0), "Locale refresh API unavailable.");
var card = gameTypes["MegaCrit.Sts2.Core.Nodes.Cards.NCard"];
Require(card.Properties.Any(p => p.Name == "Model") && card.Properties.Any(p => p.Name == "DisplayingPile")
    && card.Methods.Any(m => m.Name == "UpdateVisuals" && m.Parameters.Count == 2), "Card display API unavailable.");
Require(addon.Resources.Count == 26, "Expected all 26 embedded localization tables.");
Require(!addon.GetTypeReferences().Any(t => t.FullName.Contains(".Multiplayer.") || t.FullName.Contains(".Random.") || t.FullName.StartsWith("System.Net.")),
    "Display addon must not depend on network or RNG APIs.");
var relic = Types(original).Single(t => t.FullName == "GuZhenRen.Relics.ShuiWenGu");
var afterCard = relic.Methods.Single(m => m.Name == "AfterCardPlayed");
var attribute = afterCard.CustomAttributes.Single(a => a.AttributeType.Name == "AsyncStateMachineAttribute");
var machineName = ((TypeReference)attribute.ConstructorArguments[0].Value).FullName;
var tick = Types(original).Single(t => t.FullName == machineName).Methods.Single(m => m.Name == "MoveNext");
Require(tick.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == "CardModel" && m.Name == "get_Title") == 1,
    "ShuiWenGu title-counting hook no longer matches the original mod.");
var safeTitle = methods.Single(m => m.Name == "OriginalCardTitle");
Require(safeTitle.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally), "Original-title getter must always restore the display override.");
Console.WriteLine($"PASS: {gameCalls.Length} direct game API calls resolve; locale refresh and card display reflection API exist.");
Console.WriteLine("PASS: original ShuiWenGu async title getter matches the narrow gameplay-preservation hook; cleanup uses finally.");
Console.WriteLine("PASS: addon contains both complete language sets and has no networking or RNG API references.");
