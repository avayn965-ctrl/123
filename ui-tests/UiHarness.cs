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
    public List<Control> OptionButtons { get; } = new();
    private static ImageTexture Texture(Color color)
    {
        using var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }
    private static IEnumerable<Node> Tree(Node node) => new[] { node }.Concat(node.GetChildren().SelectMany(Tree));
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
            string? probe = System.Environment.GetEnvironmentVariable("REFRESH_PLACEMENT_PROBE");
            if (!string.IsNullOrEmpty(probe))
            {
                PlacementChecks.Probe(this, assembly, probe); GetTree().Quit(); return;
            }
            Type placement = assembly.GetType("RefreshAncient.Compat2.ButtonPlacement", true)!;
            string positionFile = (string)placement.GetMethod("SettingsPath")!.Invoke(null, null)!;
            if (File.Exists(positionFile)) File.Delete(positionFile);
            LocManager.Instance.Language = "rus";
            var current = new Ancient(); current.Fill(40); current.Id.Entry = "A";
            foreach (var option in current.Pool) option.Description = new string('Ж', 120) + "\nОписание награды и её эффектов.";
            var rewardTexture = Texture(Colors.Cyan);
            var outlineTexture = Texture(Colors.Black);
            current.Pool[0].Title = "Формованное Стекло";
            current.Pool[0].Description = "Зачаровывает [color=#ff79ff]Замыслом[/color] и отнимает [b]половину ОЗ[/b]. [purple]Ключевое слово[/purple]";
            current.Pool[0].Relic = new MegaCrit.Sts2.Core.Models.Relics.Relic { Icon = rewardTexture, IconOutline = outlineTexture };
            current.Pool[0].HoverTips.Add(new PreviewTip());
            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Host = this;
            current.RunHistoryIcon = Texture(Colors.Gold); current.RunHistoryIconOutline = outlineTexture;
            var next = new Ancient(); next.Id.Entry = "B"; next.Owner.RunState = current.Owner.RunState;
            next.RunHistoryIcon = Texture(Colors.Green); next.RunHistoryIconOutline = outlineTexture;
            next.MapIcon = Texture(Colors.Red);
            var last = new Ancient(); last.Id.Entry = "C"; last.Owner.RunState = current.Owner.RunState;
            Call(_ancients, "Filter", current.Owner.RunState, current, new List<Ancient> { next, last });
            NEventRoom.Instance.SetEvent(current);
            var options = new Godot.Button { Name = "RefreshAncient_RefreshOptionsButton", Text = "Обновить выбор", Position = new Vector2(650, 260), Size = new Vector2(250, 54) };
            var ancients = new Godot.Button { Name = "RefreshAncient_RefreshAncientButton", Text = "Сменить Древнего", Position = new Vector2(650, 324), Size = new Vector2(250, 54) };
            AddChild(options); AddChild(ancients);
            var template = new Godot.Button { Name = "OriginalEventOption", Position = new Vector2(20, 500), Size = new Vector2(500, 124) };
            var originalImage = new NinePatchRect { Name = "Image", Texture = Texture(Colors.MidnightBlue) };
            var originalText = new RichTextLabel { Name = "Text", Text = "Оригинальная награда", BbcodeEnabled = true, FitContent = true };
            var colorEffect = new PreviewColorEffect();
            originalText.InstallEffect(colorEffect);
            int originalInput = 0;
            originalImage.GuiInput += _ => originalInput++;
            template.AddChild(originalImage); template.AddChild(originalText);
            AddChild(template); OptionButtons.Add(template);
            await Frames();
            Call(_catalog, "Update", this);
            var allOptions = GetNode<Godot.Button>("RefreshAncient_AllOptionsButton");
            var allAncients = GetNode<Godot.Button>("RefreshAncient_AllAncientsButton");
            Check(allOptions.Visible && allAncients.Visible && allOptions.Text == "Все варианты" && allAncients.Text == "Все Древние", "both localized browse buttons are created");
            Check(!options.GetRect().Intersects(allOptions.GetRect()) && allOptions.Position.X > options.Position.X, "wide layout puts catalog buttons beside paid buttons without overlap");
            Call(_catalog, "Update", this);
            Check(GetChildren().OfType<Godot.Button>().Count() == 5, "UI updates reuse buttons instead of duplicating them");
            allOptions.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(5);
            var dialog = GetNode<AcceptDialog>("RefreshAncient_CatalogDialog");
            var content = (RichTextLabel)dialog.FindChild("RefreshAncient_CatalogText", true, false);
            var scroll = (ScrollContainer)dialog.FindChild("RefreshAncient_CatalogScroll", true, false);
            var rows = (VBoxContainer)dialog.FindChild("RefreshAncient_CatalogRows", true, false);
            Check(dialog.Visible && dialog.Exclusive && dialog.Title == "Все варианты" && content.Text.Contains("Всего: 40"), "options button opens a real exclusive catalog window");
            Check(dialog.Size.Y <= GetWindow().ContentScaleSize.Y, "the first popup stays within screen height after its initial container layout");
            Check(scroll.Size.X > 0 && scroll.Size.Y > 0 && scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page, "long visual catalog has native scrollable visible content");
            dialog.NotifyMouseEntered();
            Vector2 wheelPoint = scroll.GetGlobalRect().GetCenter();
            dialog.PushInput(new InputEventMouseMotion { Position = wheelPoint, GlobalPosition = wheelPoint }, true);
            dialog.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1, Position = wheelPoint, GlobalPosition = wheelPoint }, true);
            await Frames();
            Check(scroll.ScrollVertical > 0, "mouse wheel scrolls cards on the first opening without reopening");
            scroll.ScrollVertical = 0;
            var first = (PanelContainer)rows.GetChild(0);
            var label = (RichTextLabel)first.FindChild("Text", true, false);
            Check(rows.GetChildCount() == 40 && ((NinePatchRect)first.FindChild("Image", true, false)).Texture == originalImage.Texture, "every reward is a card using the original event background");
            Check(label.BbcodeEnabled && label.Text.Contains("[b]Формованное Стекло[/b]") && label.Text.Contains("[color=#ff79ff]Замыслом[/color]") && !label.GetParsedText().Contains("[color"), "reward cards preserve the gold title, colored keywords and rich description");
            Check(label.CustomEffects.Contains(colorEffect) && !label.GetParsedText().Contains("[purple]") && label.GetParsedText().Contains("Ключевое слово"), "game-style custom color effects survive copying without label scripts");
            Check(((TextureRect)first.FindChild("Icon", true, false)).Texture == rewardTexture && ((TextureRect)first.FindChild("Outline", true, false)).Texture == outlineTexture, "reward cards show the exact relic icon and outline");
            Check(Tree(rows).OfType<Control>().All(c => (c.MouseFilter == MouseFilterEnum.Ignore || c is PanelContainer && c.MouseFilter == MouseFilterEnum.Pass) && c.FocusMode == FocusModeEnum.None) && !Tree(rows).OfType<BaseButton>().Any(), "hoverable catalog cards have no clickable or focusable gameplay controls");
            first.FindChild("Image", true, false).EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            Check(originalInput == 0 && originalText.Text == "Оригинальная награда" && template.GetParent() == this, "visual copies have no original signals and leave live options unchanged");
            Check(Tree(rows).OfType<RichTextLabel>().All(c => c.GetContentHeight() <= c.Size.Y + 1 && c.Size.X > 0), "long descriptions wrap and fit their cards without clipping");
            Check(GetLastExclusiveWindow() == dialog && Call(_unique, "CanPress", this) is false && Call(_ancients, "CanPress", this) is false, "modal window blocks both refresh actions");
            Check(current.Rng.Draws == 0 && (int)Call(_unique, "Remaining", current)! == 37 && (int)Call(_ancients, "Remaining", current)! == 2, "opening catalog spends no RNG and consumes no options or Ancients");
            dialog.PushInput(new InputEventKey { Keycode = Key.Pagedown, PhysicalKeycode = Key.Pagedown, Pressed = true }, true);
            await Frames();
            Check(scroll.ScrollVertical > 0, "keyboard PageDown scrolls on the first opening");
            dialog.PushInput(new InputEventKey { Keycode = Key.End, Pressed = true }, true);
            Check(scroll.ScrollVertical >= scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page - 1, "End reaches the last options on the first opening");
            dialog.PushInput(new InputEventKey { Keycode = Key.Home, Pressed = true }, true);
            Check(scroll.ScrollVertical == 0, "Home returns to the first options");
            var barRect = scroll.GetVScrollBar().GetGlobalRect();
            Check(scroll.GetVScrollBar().Visible && scroll.GetVScrollBar().MouseFilter == MouseFilterEnum.Stop && barRect.Position.Y >= 0 && barRect.End.Y <= dialog.Size.Y, "scrollbar is visible and fully inside the first popup");
            scroll.ScrollVertical = 0; await Frames();
            Vector2 hoverPoint = first.GlobalPosition + new Vector2(40, 30);
            first.EmitSignal(Control.SignalName.MouseExited);
            first.EmitSignal(Control.SignalName.MouseEntered);
            await Frames();
            Check(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Active.ContainsKey(first) && ReferenceEquals(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.LastTips, current.Pool[0].HoverTips), "mouse hover passes the option's original extra information to the game tip factory");
            var tip = MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Active[first];
            var tipText = tip.GetNode<Control>("textHoverTipContainer");
            Check(tip.GetWindow() == dialog && tip.GetParent() is CanvasLayer && tipText.GlobalPosition.X >= 0 && tipText.GlobalPosition.X + tipText.Size.X <= dialog.Size.X, "game tips appear inside the exclusive catalog window and stay within its bounds");
            first.EmitSignal(Control.SignalName.MouseExited);
            Check(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Active.Count == 0, "moving away removes the game tip");
            first.EmitSignal(Control.SignalName.MouseEntered);
            dialog.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1, Position = hoverPoint, GlobalPosition = hoverPoint }, true);
            await Frames();
            Check(scroll.ScrollVertical > 0 && MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Active.Count == 0, "scrolling over a hovered card keeps working and clears its stale tip");
            scroll.ScrollVertical = 0; await Frames();
            first.EmitSignal(Control.SignalName.MouseExited);
            first.EmitSignal(Control.SignalName.MouseEntered);
            dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
            Check(!dialog.Visible && Call(_catalog, "ShouldSuppressCancel") is true, "Close button hides the window and shields the pause latch");
            Check(MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Active.Count == 0 && current.Rng.Draws == 0, "closing the catalog clears hover tips without consuming RNG");
            await Frames(4);
            Check(Call(_catalog, "ShouldSuppressCancel") is false, "pause suppression expires after closing");
            allAncients.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            Check(dialog.Visible && dialog.Title == "Все Древние" && rows.GetChildCount() == 3 && Tree(rows).OfType<RichTextLabel>().Any(c => c.Text.Contains("Древний B")) && content.Text.Contains("Ещё доступны: 2"), "Ancient catalog reuses the window with correct content");
            var ancientRow = rows.GetChild(1);
            var ancientIcon = (TextureRect)ancientRow.FindChild("Icon", true, false);
            Check(ancientIcon.Texture == next.RunHistoryIcon && ancientIcon.Texture != next.MapIcon && ancientIcon.Size == new Vector2(56, 56), "Ancient names use the small top-bar icon rather than the map portrait");
            Check(((TextureRect)ancientRow.FindChild("Outline", true, false)).Texture == outlineTexture && Tree(ancientRow).OfType<Label>().Any(c => c.Text == "Ещё доступен"), "Ancient miniature retains its outline and availability status");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
            await Frames();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
            Check(!dialog.Visible, "Escape closes the native catalog window");
            await Frames(4);
            allOptions.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            Check(rows.GetChildCount() == 40 && ((TextureRect)rows.GetChild(0).FindChild("Icon", true, false)).Texture == rewardTexture && scroll.ScrollVertical == 0, "switching catalogs replaces old cards and resets scroll position");
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
            void UpdateLikeGame()
            {
                Input.FlushBufferedEvents();
                options.Visible = true; ancients.Visible = true;
                Call(_catalog, "Update", this);
            }
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true });
            Input.FlushBufferedEvents();
            UpdateLikeGame();
            Check(!options.Visible && !ancients.Visible && !allOptions.Visible && !allAncients.Visible && Call(_unique, "CanPress", this) is false && Call(_ancients, "CanPress", this) is false, "H hides all four mod buttons and blocks refresh preflight");
            for (int i = 0; i < 3; i++) UpdateLikeGame();
            Check(!options.Visible && !allOptions.Visible, "holding H does not repeatedly toggle visibility during game UI updates");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false }); UpdateLikeGame();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true }); UpdateLikeGame();
            Check(options.Visible && ancients.Visible && allOptions.Visible && allAncients.Visible && ancients.Disabled, "pressing H again restores all four buttons and preserves exhaustion restrictions");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false }); UpdateLikeGame();
            var editor = new LineEdit(); AddChild(editor); editor.GrabFocus();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = true }); UpdateLikeGame();
            Check(options.Visible && allOptions.Visible, "typing H into a text field does not toggle mod controls");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, PhysicalKeycode = Key.H, Pressed = false }); editor.ReleaseFocus(); editor.QueueFree();
            options.Visible = false; ancients.Visible = false; Call(_catalog, "Update", this);
            Check(!allOptions.Visible && !allAncients.Visible, "catalog buttons follow original visibility rules");
            PlacementChecks.Run(this, assembly, Check);
            await Frames(2);
            await CatalogChecks.Run(this, assembly, Check);
            GD.Print($"{_checks} native Godot 4.5.1 UI checks passed ({(string.IsNullOrEmpty(path) ? "runtime source" : "merged mod DLL")}).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
