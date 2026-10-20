using Godot;
using System.Reflection;
using Fixtures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Rooms;

public partial class UiHarness : Control
{
    private Type _unique = null!;
    private Type _ancients = null!;
    private Type _catalog = null!;
    private int _checks;
    private object? Call(Type api, string name, params object?[] args) => api.GetMethod(name)!.Invoke(null, args);
    private void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++; GD.Print("PASS UI: " + name);
    }
    private async Task Frames(int count = 2)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    public override async void _Ready()
    {
        try
        {
            string? path = System.Environment.GetEnvironmentVariable("REFRESH_TEST_MOD");
            Assembly assembly = string.IsNullOrEmpty(path) ? typeof(RefreshAncient.Compat2.UniqueOptions).Assembly : Assembly.LoadFrom(Path.GetFullPath(path));
            _unique = assembly.GetType("RefreshAncient.Compat2.UniqueOptions", true)!;
            _ancients = assembly.GetType("RefreshAncient.Compat2.AncientHistory", true)!;
            _catalog = assembly.GetType("RefreshAncient.Compat2.CatalogUi", true)!;
            // The headless display server has a 64x64 physical dummy window.
            // Use a real 1280x720 logical viewport for layout/scroll tests.
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            GetWindow().ContentScaleSize = new Vector2I(1280, 720);
            LocManager.Instance.Language = "rus";
            var current = new Ancient(); current.Fill(40); current.Id.Entry = "A";
            foreach (var option in current.Pool) option.Description = new string('Ж', 120) + "\nОписание награды и её эффектов.";
            var next = new Ancient(); next.Id.Entry = "B"; next.Owner.RunState = current.Owner.RunState;
            var last = new Ancient(); last.Id.Entry = "C"; last.Owner.RunState = current.Owner.RunState;
            Call(_ancients, "Filter", current.Owner.RunState, current, new List<Ancient> { next, last });
            NEventRoom.Instance.SetEvent(current);
            var options = new Godot.Button { Name = "RefreshAncient_RefreshOptionsButton", Text = "Обновить выбор", Position = new Vector2(650, 260), Size = new Vector2(250, 54) };
            var ancients = new Godot.Button { Name = "RefreshAncient_RefreshAncientButton", Text = "Сменить Древнего", Position = new Vector2(650, 324), Size = new Vector2(250, 54) };
            AddChild(options); AddChild(ancients);
            await Frames();
            Call(_catalog, "Update", this);
            var allOptions = GetNode<Godot.Button>("RefreshAncient_AllOptionsButton");
            var allAncients = GetNode<Godot.Button>("RefreshAncient_AllAncientsButton");
            Check(allOptions.Visible && allAncients.Visible && allOptions.Text == "Все варианты" && allAncients.Text == "Все Древние", "both localized browse buttons are created");
            Check(!options.GetRect().Intersects(allOptions.GetRect()) && allOptions.Position.X > options.Position.X, "wide layout puts catalog buttons beside paid buttons without overlap");
            Call(_catalog, "Update", this);
            Check(GetChildren().OfType<Godot.Button>().Count() == 4, "UI updates reuse buttons instead of duplicating them");
            allOptions.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            var dialog = GetNode<AcceptDialog>("RefreshAncient_CatalogDialog");
            var content = dialog.GetNode<RichTextLabel>("RefreshAncient_CatalogText");
            Check(dialog.Visible && dialog.Exclusive && dialog.Title == "Все варианты" && content.Text.Contains("Всего: 40"), "options button opens a real exclusive catalog window");
            Check(content.Size.X > 0 && content.Size.Y > 0 && content.ScrollActive && content.GetVScrollBar().MaxValue > content.GetVScrollBar().Page, "long catalog has native scrollable visible content");
            Check(GetLastExclusiveWindow() == dialog && Call(_unique, "CanPress", this) is false && Call(_ancients, "CanPress", this) is false, "modal window blocks both refresh actions");
            Check(current.Rng.Draws == 0 && (int)Call(_unique, "Remaining", current)! == 37 && (int)Call(_ancients, "Remaining", current)! == 2, "opening catalog spends no RNG and consumes no options or Ancients");
            dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
            Check(!dialog.Visible && Call(_catalog, "ShouldSuppressCancel") is true, "Close button hides the window and shields the pause latch");
            await Frames(4);
            Check(Call(_catalog, "ShouldSuppressCancel") is false, "pause suppression expires after closing");
            allAncients.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            Check(dialog.Visible && dialog.Title == "Все Древние" && content.Text.Contains("Древний B") && content.Text.Contains("Ещё доступны: 2"), "Ancient catalog reuses the window with correct content");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
            await Frames();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
            Check(!dialog.Visible, "Escape closes the native catalog window");
            await Frames(4);
            allOptions.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            dialog.EmitSignal(Window.SignalName.CloseRequested);
            Check(!dialog.Visible && GetChildren().OfType<AcceptDialog>().Count() == 1, "window close request works and reopening does not duplicate dialogs");
            await Frames(4);
            Size = new Vector2(720, 720); options.Position = new Vector2(420, 200); ancients.Position = new Vector2(420, 264);
            Call(_catalog, "Update", this);
            Check(allOptions.Position.X == options.Position.X && allOptions.Position.Y > options.Position.Y && !allOptions.GetRect().Intersects(ancients.GetRect()), "narrow layout stacks buttons without overlap");
            Call(_ancients, "CommitIfSuccessful", true, current, next);
            Call(_ancients, "CommitIfSuccessful", true, current, last);
            Call(_ancients, "Filter", current.Owner.RunState, current, new List<Ancient> { next, last });
            Call(_catalog, "Update", this);
            Check(ancients.Disabled && ancients.Text == "Новых Древних нет" && allAncients.Visible && !allAncients.Disabled, "exhausted Ancient button is disabled while its catalog remains accessible");
            options.Visible = false; ancients.Visible = false; Call(_catalog, "Update", this);
            Check(!allOptions.Visible && !allAncients.Visible, "catalog buttons follow original visibility rules");
            GD.Print($"{_checks} native Godot 4.5.1 UI checks passed ({(string.IsNullOrEmpty(path) ? "runtime source" : "merged mod DLL")}).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
