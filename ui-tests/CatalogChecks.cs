using Godot;
using System.Reflection;
using Fixtures;
using MegaCrit.Sts2.Core.Nodes.Rooms;

internal static class CatalogChecks
{
    internal static async Task Run(Control parent, Assembly assembly, Action<bool, string> check)
    {
        Type unique = assembly.GetType("RefreshAncient.Compat2.UniqueOptions", true)!;
        Type catalog = assembly.GetType("RefreshAncient.Compat2.CatalogUi", true)!;
        object? Call(Type type, string method, params object?[] args) => type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        var current = new Ancient(); current.Fill(12);
        current.Pool[11].IsLocked = true;
        for (int i = 0; i < current.Pool.Count; i++)
        {
            using var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            image.Fill(new Color(i / 12f, 0.5f, 1 - i / 12f));
            current.Pool[i].Title = "Награда " + i;
            current.Pool[i].Relic = new MegaCrit.Sts2.Core.Models.Relics.Relic { Icon = ImageTexture.CreateFromImage(image) };
            current.Pool[i].HoverTips.Add(new PreviewTip { Title = "Подсказка " + i });
        }
        NEventRoom.Instance.SetEvent(current);
        Call(unique, "Remaining", current);
        object next = Call(unique, "Generate", current)!; current.Apply(next); Call(unique, "Commit", current, next);
        int draws = current.Rng.Draws;
        var layout = new Control { Name = "CatalogOrderChecks", Size = new Vector2(1280, 720) }; parent.AddChild(layout);
        layout.AddChild(new Godot.Button { Name = "RefreshAncient_RefreshOptionsButton", Position = new Vector2(650, 260), Size = new Vector2(250, 54) });
        Call(catalog, "Update", layout); Call(catalog, "Open", layout, false);
        for (int i = 0; i < 4; i++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        var dialog = layout.GetNode<AcceptDialog>("RefreshAncient_CatalogDialog");
        var rows = (VBoxContainer)dialog.FindChild("RefreshAncient_CatalogRows", true, false);
        var cards = rows.GetChildren().OfType<PanelContainer>().ToArray();
        string[] statuses = cards.Select(card => ((Godot.Label)card.FindChild("Status", true, false)).Text).ToArray();
        check(statuses.Take(3).All(s => s == "Уже показан") && statuses.Skip(3).Take(3).All(s => s == "На экране") && statuses.Skip(6).Take(5).All(s => s == "Ещё доступен"), "catalog sorts all previously shown choices before current and still available choices");
        check(statuses.Last() == "Недоступен" && cards.Length == 12, "ineligible rewards remain visible as a separate final group");
        Color Border(int index) => ((StyleBoxFlat)((Panel)cards[index].FindChild("StatusBorder", true, false)).GetThemeStylebox("panel")).BorderColor;
        check(Enumerable.Range(0, 3).All(i => Border(i).R > 0.9f && Border(i).G < 0.2f), "every already-shown choice has a red frame");
        check(Enumerable.Range(3, 3).All(i => Border(i).G > 0.9f && Border(i).R < 0.2f), "every current choice has a green frame");
        check(Enumerable.Range(6, 5).All(i => Border(i).R > 0.9f && Border(i).G > 0.8f && Border(i).B < 0.1f), "every still-available choice has a yellow frame");
        bool paired = true;
        foreach (var card in cards)
        {
            int id = int.Parse(card.Name.ToString().Replace("CatalogCard_", string.Empty));
            var text = (RichTextLabel)card.FindChild("Text", true, false);
            var icon = (TextureRect)card.FindChild("Icon", true, false);
            if (!text.Text.Contains("Награда " + id) || icon.Texture != current.Pool[id].Relic!.GetType().GetProperty("Icon")!.GetValue(current.Pool[id].Relic)) paired = false;
        }
        check(paired, "sorting preserves the pairing of each reward title, description and relic icon");
        var selected = cards[4];
        int selectedId = int.Parse(selected.Name.ToString().Replace("CatalogCard_", string.Empty));
        selected.EmitSignal(Control.SignalName.MouseEntered);
        check(ReferenceEquals(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.LastTips, current.Pool[selectedId].HoverTips) && current.Rng.Draws == draws, "sorted cards keep the correct game hover tips without consuming RNG");
        dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        layout.QueueFree();
    }
}
