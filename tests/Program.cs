using System.Collections;
using System.Reflection;
using Fixtures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;

Type api = args.Length == 0 ? typeof(RefreshAncient.Compat2.UniqueOptions)
    : Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("RefreshAncient.Compat2.UniqueOptions", true)!;
object? Invoke(string name, params object?[] values) => api.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, values);
int Remaining(Ancient ancient) => (int)Invoke("Remaining", ancient)!;
object? Generate(Ancient ancient) => Invoke("Generate", ancient);
void Commit(Ancient ancient, object options) { ancient.Apply(options); Invoke("Commit", ancient, options); }
List<string> Keys(object options) => ((IEnumerable)options).Cast<Option>().Select(o => o.TextKey).ToList();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
int passed = 0;
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
Ancient Make(int pool) { var ancient = new Ancient(); ancient.Fill(pool); return ancient; }

Test("third, fourth and fifth refresh never repeat initial or previously displayed choices", () =>
{
    var ancient = Make(18); var seen = new HashSet<string>(ancient.CurrentOptions.Select(o => o.TextKey));
    for (int i = 0; i < 5; i++)
    {
        object options = Generate(ancient)!; var keys = Keys(options);
        Check(keys.Count == 3 && keys.Distinct().Count() == 3 && !keys.Any(seen.Contains), "repeated/missing choice on refresh " + (i + 1));
        foreach (string key in keys) seen.Add(key);
        Commit(ancient, options);
    }
    Check(seen.Count == 18 && Remaining(ancient) == 0 && Generate(ancient) == null, "pool should be exhausted");
});
Test("last partial batch contains only the remaining choice; exhausted preview draws no RNG", () =>
{
    var ancient = Make(10); var sizes = new List<int>();
    while (Remaining(ancient) > 0) { object options = Generate(ancient)!; sizes.Add(Keys(options).Count); Commit(ancient, options); }
    int draws = ancient.Rng.Draws;
    Check(sizes.SequenceEqual(new[] { 3, 3, 1 }), "wrong final partial batch");
    Check(Generate(ancient) == null && ancient.Rng.Draws == draws, "exhausted refresh consumed RNG");
});
Test("unpaid/unapplied previews do not consume choices", () =>
{
    var ancient = Make(12); int remaining = Remaining(ancient);
    Check(Generate(ancient) != null && Generate(ancient) != null && Remaining(ancient) == remaining, "preview consumed history");
});
Test("duplicate text keys, locked/proceed/non-action options are excluded", () =>
{
    var ancient = Make(5);
    ancient.Pool.Add(new Option { TextKey = "OPTION_3" });
    ancient.Pool.Add(new Option { TextKey = "LOCKED", IsLocked = true });
    ancient.Pool.Add(new Option { TextKey = "PROCEED", IsProceed = true });
    ancient.Pool.Add(new Option { TextKey = "NO_ACTION", OnChosen = null });
    Check(Remaining(ancient) == 2 && Keys(Generate(ancient)!).Count == 2, "unavailable/duplicate choices included");
});
Test("UI polling materializes the pool once and never consumes RNG", () =>
{
    var ancient = Make(20);
    for (int i = 0; i < 100; i++) Check(Remaining(ancient) == 17, "changed availability");
    Check(ancient.Enumerations == 1 && ancient.Rng.Draws == 0, "UI changed random stream or rebuilt pool");
});
Test("history is isolated by player/event instance and resets for a replaced Ancient", () =>
{
    var first = Make(6); var second = Make(6); Commit(first, Generate(first)!);
    Check(Remaining(first) == 0 && Remaining(second) == 3 && Remaining(Make(6)) == 3, "history leaked between events");
});
Test("host/client selections and RNG remain equal for four refreshes; stale keys are rejected", () =>
{
    var host = Make(16); var client = Make(16);
    for (int i = 0; i < 4; i++)
    {
        object hostOptions = Generate(host)!; object clientPreview = Generate(client)!; var keys = Keys(hostOptions);
        Check(keys.SequenceEqual(Keys(clientPreview)), "deterministic selection mismatch");
        object rebuilt = Invoke("Rebuild", client, keys)!;
        Check(Keys(rebuilt).SequenceEqual(keys), "network reconstruction mismatch");
        Commit(host, hostOptions); Commit(client, rebuilt);
        Check(Remaining(host) == Remaining(client) && host.Rng.Draws == client.Rng.Draws, "host/client history or RNG mismatch");
        Check(Invoke("Rebuild", client, keys) == null, "replayed result was accepted");
    }
    Check(Invoke("Rebuild", client, new[] { "unknown" }) == null, "unknown key accepted");
    Check(Invoke("Rebuild", client, new[] { "OPTION_15", "OPTION_15" }) == null, "duplicate packet keys accepted");
});
Test("exhausted button visibly warns in Russian, stays disabled and rejects click preflight", () =>
{
    LocManager.Instance.Language = "rus"; var ancient = Make(6); Commit(ancient, Generate(ancient)!);
    NEventRoom.Instance.SetEvent(ancient); var layout = new Layout();
    Invoke("UpdateUi", layout);
    Check(layout.Button.Disabled && layout.Button.Text == "Новых вариантов нет" && layout.Button.TooltipText.Contains("Золото не списывается"), "missing visible/hover warning");
    Check(Invoke("CanPress", layout) is false, "exhausted click accepted");
    var available = Make(6); NEventRoom.Instance.SetEvent(available); var disabledForGold = new Layout(); disabledForGold.Button.Disabled = true;
    Invoke("UpdateUi", disabledForGold);
    Check(disabledForGold.Button.Disabled && Invoke("CanPress", disabledForGold) is true, "availability overrode base gold/UI disable state");
});
Test("Russian uses game language; English and Chinese button text remain available", () =>
{
    foreach (string language in new[] { "ru", "rus", "ru_RU" }) Check(((string)Invoke("OptionsTextFor", language, 75)!).Contains("Обновить выбор (75 з.)"), "Russian alias unsupported");
    Check((string)Invoke("OptionsTextFor", "eng", 75)! == "Refresh Options (75g)", "English text changed");
    Check(((string)Invoke("AncientTextFor", "zhs", 100)!).Contains("刷新先古之民"), "Chinese text missing");
    LocManager.Instance.Language = "rus";
    Check(((string)Invoke("AncientText", 100)!).Contains("Сменить Древнего"), "game language ignored");
    var dictionary = new Dictionary<string, string> { ["en"] = "General", ["zhs"] = "原文" };
    Invoke("AddRussianLabels", dictionary);
    Check(dictionary["rus"] == "Общие настройки" && dictionary["ru"] == dictionary["rus"] && dictionary["zhs"] == "原文", "settings translations lost");
});
Test("Neow modifiers and relic eligibility are respected", () =>
{
    var neow = new Neow(); neow.Fill(3);
    neow.Pool.Add(new Option { TextKey = "GOOD", Relic = new AllowedRelic() });
    neow.Pool.Add(new Option { TextKey = "BAD", Relic = new ForbiddenRelic() });
    Check(Remaining(neow) == 1, "Neow offered forbidden relic");
    var custom = new Neow(); custom.Fill(6); custom.Owner.RunState.Modifiers.Add(new object());
    Check(Remaining(custom) == 0, "Neow modifier event replaced with ordinary choices");
});
Test("enchantment/card/pet restrictions for Nonupeipe, Tanx, Pael and Tezcatara are respected", () =>
{
    var nonu = new Nonupeipe(); nonu.Fill(3); nonu.Pool.Add(new Option { TextKey = "BRACELET", Relic = new BeautifulBracelet() });
    Check(Remaining(nonu) == 0, "ineligible bracelet offered");
    var eligible = new Nonupeipe(); eligible.Fill(3); eligible.Pool.Add(new Option { TextKey = "BRACELET", Relic = new BeautifulBracelet() });
    for (int i = 0; i < 4; i++) eligible.Owner.Deck.Cards.Add(new Card { Enchantable = true });
    Check(Remaining(eligible) == 1, "eligible bracelet missing");
    var tanx = new Tanx(); tanx.Fill(3); tanx.Pool.Add(new Option { TextKey = "BOOM", Relic = new TriBoomerang() });
    Check(Remaining(tanx) == 0, "ineligible boomerang offered");
    var pael = new Pael(); pael.Fill(3); pael.Owner.HasPet = true;
    pael.Pool.Add(new Option { TextKey = "CLAW", Relic = new PaelsClaw() }); pael.Pool.Add(new Option { TextKey = "TOOTH", Relic = new PaelsTooth() }); pael.Pool.Add(new Option { TextKey = "PET", Relic = new PaelsLegion() });
    Check(Remaining(pael) == 0, "ineligible Pael choice offered");
    var tez = new Tezcatara(); tez.Fill(3); tez.Pool.Add(new Option { TextKey = "SOUP", Relic = new NutritiousSoup() });
    Check(Remaining(tez) == 0, "soup offered without basic strike");
});
Test("Darv filters are applied and DustyTome random setup is deferred until selection", () =>
{
    var darv = new Darv(); darv.Fill(3); var tome = new DustyTome();
    darv.Pool.Add(new Option { TextKey = "TOME", Relic = tome }); darv.Pool.Add(new Option { TextKey = "FORBIDDEN", Relic = new ForbiddenRelic() });
    Check(Remaining(darv) == 1 && tome.Setups == 0, "UI setup consumed reward RNG or ignored Darv filter");
    Check(Keys(Generate(darv)!).SequenceEqual(new[] { "TOME" }) && tome.Setups == 1, "selected tome not prepared");
    Generate(darv); Check(tome.Setups == 1, "unapplied preview repeated random setup");
});
Test("Orobas SeaGlass excludes locked/own characters and collapses identical text keys", () =>
{
    var orobas = new Orobas(); orobas.Fill(3);
    orobas.Owner.UnlockState.Characters.Add(new Character { Id = "IRONCLAD" }); orobas.Owner.UnlockState.Characters.Add(new Character { Id = "SILENT" });
    orobas.Pool.Add(new Option { TextKey = "SEA_GLASS", Relic = new SeaGlass { CharacterId = "LOCKED" } });
    orobas.Pool.Add(new Option { TextKey = "SEA_GLASS", Relic = new SeaGlass { CharacterId = "IRONCLAD" } });
    orobas.Pool.Add(new Option { TextKey = "SEA_GLASS", Relic = new SeaGlass { CharacterId = "SILENT" } });
    Check(Remaining(orobas) == 1 && ((List<Option>)Generate(orobas)!)[0].Relic is SeaGlass { CharacterId: "SILENT" }, "invalid SeaGlass variant offered");
});
passed += CatalogFeatureTests.Run(api);
passed += SettingsTests.Run(api.Assembly);
Console.WriteLine($"{passed} feature tests passed ({(args.Length == 0 ? "runtime source" : "merged mod DLL")}).");
