namespace Fixtures
{
    public class Option
    {
        public string TextKey { get; set; } = "";
        public bool IsLocked { get; set; }
        public bool IsProceed { get; set; }
        public Func<Task>? OnChosen { get; set; } = () => Task.CompletedTask;
        public object? Relic { get; set; }
        public string Title { get; set; } = "Название награды";
        public string Description { get; set; } = "Описание награды";
    }
    public sealed class Rng
    {
        private uint _seed = 1234567;
        public int Draws { get; private set; }
        public int NextInt(int max) { Draws++; _seed = unchecked(_seed * 1664525 + 1013904223); return (int)(_seed % (uint)max); }
    }
    public class Ancient
    {
        private List<Option> _currentOptions = new();
        public IReadOnlyList<Option> CurrentOptions => _currentOptions;
        public List<Option> Pool { get; } = new();
        public Rng Rng { get; } = new();
        public Owner Owner { get; } = new();
        public ModelId Id { get; set; } = new() { Entry = "ANCIENT" };
        public string Title => "Древний " + Id.Entry;
        public string InitialDescription => "Описание Древнего " + Id.Entry;
        public object? RunHistoryIcon { get; set; }
        public object? RunHistoryIconOutline { get; set; }
        public object? MapIcon { get; set; }
        public int Enumerations { get; private set; }
        public IEnumerable<Option> AllPossibleOptions { get { Enumerations++; return Pool; } }
        public void Apply(object value) => _currentOptions = (List<Option>)value;
        protected Option RelicOption(object relic, string page, string? done)
            => new() { TextKey = Pool.Single(o => ReferenceEquals(o.Relic, relic)).TextKey, Relic = relic };
        public void Fill(int count)
        {
            for (int i = 0; i < count; i++) Pool.Add(new Option { TextKey = "OPTION_" + i });
            Apply(Pool.Take(Math.Min(3, count)).ToList());
        }
    }
    public sealed class Owner
    {
        public Deck Deck { get; } = new();
        public RunState RunState { get; set; } = new();
        public ulong NetId { get; set; } = 1;
        public UnlockState UnlockState { get; } = new();
        public Character Character { get; } = new() { Id = "IRONCLAD" };
        public bool HasPet { get; set; }
        public bool HasEventPet() => HasPet;
    }
    public sealed class Deck { public List<Card> Cards { get; } = new(); }
    public sealed class Card
    {
        public bool IsRemovable { get; set; }
        public bool Enchantable { get; set; }
        public string Rarity { get; set; } = "Basic";
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
    public sealed class RunState
    {
        public List<object> Modifiers { get; } = new();
        public string RunLocation { get; set; } = "act1-room1";
    }
    public sealed class ModelId { public string Entry { get; set; } = ""; }
    public sealed class UnlockState { public List<Character> Characters { get; } = new(); }
    public sealed class Character { public string Id { get; set; } = ""; }
    public sealed class Button
    {
        public bool Visible { get; set; } = true;
        public bool Disabled { get; set; }
        public string Text { get; set; } = "original button";
        public string TooltipText { get; set; } = "original tooltip";
    }
    public sealed class Layout
    {
        public Button Button { get; } = new();
        public object? FindChild(string name, bool recursive, bool owned) => name == "RefreshAncient_RefreshOptionsButton" ? Button : null;
    }
}
namespace MegaCrit.Sts2.Core.Localization
{
    public sealed class LocManager
    {
        public static LocManager Instance { get; } = new();
        public string Language { get; set; } = "rus";
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    public sealed class NEventRoom
    {
        public static NEventRoom Instance { get; } = new();
        private Fixtures.Ancient? _event;
        public void SetEvent(Fixtures.Ancient ancient) => _event = ancient;
    }
}
namespace MegaCrit.Sts2.Core.Models.Events
{
    public sealed class Nonupeipe : Fixtures.Ancient { }
    public sealed class Tanx : Fixtures.Ancient { }
    public sealed class Pael : Fixtures.Ancient { }
    public sealed class Tezcatara : Fixtures.Ancient { }
    public sealed class Neow : Fixtures.Ancient { }
    public sealed class Orobas : Fixtures.Ancient { }
    public sealed class Darv : Fixtures.Ancient
    {
        private sealed class Set
        {
            public Func<Fixtures.Owner, bool> filter = _ => true;
            public object[] relics = Array.Empty<object>();
        }
        private static readonly List<Set> _validRelicSets = new()
        {
            new Set { relics = new object[] { new MegaCrit.Sts2.Core.Models.Relics.AllowedRelic() } },
            new Set { filter = _ => false, relics = new object[] { new MegaCrit.Sts2.Core.Models.Relics.ForbiddenRelic() } }
        };
    }
}
namespace MegaCrit.Sts2.Core.Models
{
    public static class ModelDb { public static T Enchantment<T>() where T : new() => new T(); }
}
namespace MegaCrit.Sts2.Core.Models.Enchantments
{
    public class Swift { public bool CanEnchant(Fixtures.Card card) => card.Enchantable; }
    public class Goopy : Swift { }
    public class Instinct : Swift { }
}
namespace MegaCrit.Sts2.Core.Models.Relics
{
    public class Relic
    {
        public virtual string Id => GetType().Name;
        public object? Icon { get; set; }
        public object? IconOutline { get; set; }
    }
    public sealed class BeautifulBracelet : Relic { }
    public sealed class TriBoomerang : Relic { }
    public sealed class PaelsClaw : Relic { }
    public sealed class PaelsTooth : Relic { }
    public sealed class PaelsLegion : Relic { }
    public sealed class NutritiousSoup : Relic { }
    public sealed class AllowedRelic : Relic { public bool IsAllowedAtNeow(Fixtures.Owner owner) => true; }
    public sealed class ForbiddenRelic : Relic { public bool IsAllowedAtNeow(Fixtures.Owner owner) => false; }
    public sealed class SeaGlass : Relic { public string CharacterId { get; set; } = ""; }
    public sealed class DustyTome : Relic
    {
        public int Setups { get; private set; }
        public void SetupForPlayer(Fixtures.Owner owner) => Setups++;
    }
}
