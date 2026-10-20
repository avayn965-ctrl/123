using System.Reflection;
using Fixtures;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

internal static class CatalogFeatureTests
{
    public static int Run(Type api)
    {
        Type ancientApi = api.Assembly.GetType("RefreshAncient.Compat2.AncientHistory", true)!;
        Type catalogApi = api.Assembly.GetType("RefreshAncient.Compat2.CatalogUi", true)!;
        object? Call(string name, params object?[] values) => api.GetMethod(name)!.Invoke(null, values);
        object? Ancients(string name, params object?[] values) => ancientApi.GetMethod(name)!.Invoke(null, values);
        object? Catalog(string name, params object?[] values) => catalogApi.GetMethod(name)!.Invoke(null, values);
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
        Ancient Event(string id, RunState run, ulong player = 1)
        {
            var value = new Ancient(); value.Fill(9); value.Id.Entry = id; value.Owner.RunState = run; value.Owner.NetId = player; return value;
        }
        List<Ancient> Filter(RunState run, Ancient current, List<Ancient> all)
            => (List<Ancient>)Ancients("Filter", run, current, all.Where(a => a.Id.Entry != current.Id.Entry).ToList())!;

        Test("Ancient rerolls exclude the initial and every successful replacement until exhaustion", () =>
        {
            var run = new RunState(); var all = new List<Ancient>();
            foreach (string id in new[] { "A", "B", "C", "D", "E" }) all.Add(Event(id, run));
            Ancient current = all[0]; var seen = new HashSet<string> { "A" };
            for (int i = 0; i < 4; i++)
            {
                var pool = Filter(run, current, all);
                Check(pool.Count == 4 - i && pool.All(a => !seen.Contains(a.Id.Entry)), "previous Ancient returned to pool");
                var next = pool[0]; Check(Ancients("CanReplace", current, next) is true, "new Ancient rejected");
                Check(Ancients("CommitIfSuccessful", true, current, next) is true, "replacement not committed");
                seen.Add(next.Id.Entry); current = next;
            }
            Check(Filter(run, current, all).Count == 0 && (int)Ancients("Remaining", current)! == 0, "Ancient pool did not exhaust");
            Check(Ancients("CanReplace", current, all[0]) is false, "old Ancient accepted through result fallback");
        });
        Test("failed Ancient replacements and inspection do not consume candidates; unknown/duplicate IDs are rejected", () =>
        {
            var run = new RunState(); var a = Event("A", run); var b = Event("B", run); var same = Event("b", run);
            var pool = (List<Ancient>)Ancients("Filter", run, a, new List<Ancient> { b, same, a })!;
            Check(pool.Count == 1, "duplicate/current model ID remained in pool");
            Check(Ancients("CommitIfSuccessful", false, a, b) is false && Ancients("CanReplace", a, b) is true, "failed replacement consumed a candidate");
            Check(Ancients("CanReplace", a, Event("UNKNOWN", run)) is false, "unknown Ancient accepted");
            for (int i = 0; i < 10; i++) Ancients("Catalog", a);
            Check((int)Ancients("Remaining", a)! == 1 && a.Rng.Draws == 0, "catalog changed selection/history");
        });
        Test("Ancient history is per player and room and resets in a new run", () =>
        {
            var run = new RunState(); var a = Event("A", run); var b = Event("B", run);
            Filter(run, a, new() { a, b }); Ancients("CommitIfSuccessful", true, a, b);
            var other = Event("A", run, 2); var otherB = Event("B", run, 2);
            Check(Filter(run, other, new() { other, otherB }).Count == 1, "history leaked to another player");
            Check(Filter(run, b, new() { a, b }).Count == 0, "history was lost on model replacement");
            run.RunLocation = "act1-room2";
            Check(Filter(run, b, new() { a, b }).Count == 1, "history leaked to a new room");
            var nextRun = new RunState(); var freshA = Event("A", nextRun); var freshB = Event("B", nextRun);
            Check(Filter(nextRun, freshA, new() { freshA, freshB }).Count == 1, "history leaked to a new run");
        });
        Test("host/client Ancient histories produce equal pools and reject replayed replacements", () =>
        {
            var hostRun = new RunState(); var clientRun = new RunState();
            var host = new List<Ancient>(); var client = new List<Ancient>();
            foreach (string id in new[] { "A", "B", "C", "D" }) { host.Add(Event(id, hostRun)); client.Add(Event(id, clientRun)); }
            Ancient h = host[0], c = client[0];
            for (int i = 0; i < 3; i++)
            {
                var hp = Filter(hostRun, h, host); var cp = Filter(clientRun, c, client);
                Check(hp.Select(a => a.Id.Entry).SequenceEqual(cp.Select(a => a.Id.Entry)), "Ancient pool desync");
                var hn = hp[0]; var cn = cp[0];
                Check(Ancients("CanReplace", h, hn) is true && Ancients("CanReplace", c, cn) is true, "network result rejected");
                Ancients("CommitIfSuccessful", true, h, hn); Ancients("CommitIfSuccessful", true, c, cn);
                Check(Ancients("CanReplace", c, cn) is false, "replayed Ancient result accepted");
                h = hn; c = cn;
            }
        });
        Test("option catalog shows current, previous, remaining and ineligible entries without spending RNG", () =>
        {
            var ancient = Event("OPTIONS", new RunState()); ancient.Pool.Add(new Option { TextKey = "LOCKED", IsLocked = true, Title = "Закрытая награда" });
            var first = (string[][])Call("OptionCatalog", ancient)!;
            Check(first.Length == 10 && first.Count(r => r[2] == "current") == 3 && first.Count(r => r[2] == "available") == 6 && first.Count(r => r[2] == "unavailable") == 1, "wrong initial catalog statuses");
            Check(ancient.Rng.Draws == 0 && (int)Call("Remaining", ancient)! == 6, "opening catalog changed RNG or consumed choices");
            object next = Call("Generate", ancient)!; ancient.Apply(next); Call("Commit", ancient, next);
            int draws = ancient.Rng.Draws; var later = (string[][])Call("OptionCatalog", ancient)!;
            Check(later.Count(r => r[2] == "seen") == 3 && later.Count(r => r[2] == "current") == 3 && later.Count(r => r[2] == "available") == 3, "wrong catalog after refresh");
            Check(ancient.Rng.Draws == draws, "catalog advanced RNG");
        });
        Test("Ancient catalog includes localized titles, descriptions and status of replaced models", () =>
        {
            var run = new RunState(); var a = Event("A", run); var b = Event("B", run); var c = Event("C", run);
            Filter(run, a, new() { a, b, c }); Ancients("CommitIfSuccessful", true, a, b);
            var rows = (string[][])Ancients("Catalog", b)!;
            Check(rows.Length == 3 && rows.Single(r => r[0] == "Древний A")[2] == "seen" && rows.Single(r => r[0] == "Древний B")[2] == "current" && rows.Single(r => r[0] == "Древний C")[2] == "available", "wrong Ancient catalog statuses");
            string content = (string)Catalog("FormatCatalog", rows, true)!;
            Check(content.Contains("Только просмотр") && content.Contains("Уже показан") && content.Contains("Описание Древнего") && content.Contains("Ещё доступны: 1"), "missing Russian catalog content");
        });
        Test("catalog preview does not roll DustyTome random card and strips display markup", () =>
        {
            var darv = new Darv(); darv.Fill(3); var tome = new DustyTome();
            darv.Pool.Add(new Option { TextKey = "TOME", Relic = tome, Title = "[color=red]Книга[/color]" });
            var rows = (string[][])Call("OptionCatalog", darv)!;
            Check(rows.Single(r => r[0] == "Книга")[1].Contains("определится") && tome.Setups == 0 && darv.Rng.Draws == 0, "catalog randomized an undisplayed reward");
        });
        return passed;
    }
}
