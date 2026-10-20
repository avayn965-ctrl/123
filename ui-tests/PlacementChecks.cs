using Godot;
using System.Reflection;
using Fixtures;
using ModConfig;
using MegaCrit.Sts2.Core.Nodes.Rooms;

internal static class PlacementChecks
{
    private static readonly Vector2[] Targets = { new(110, 90), new(440, 130), new(80, 510), new(950, 560) };
    private sealed class Scene
    {
        internal readonly Control Root;
        internal readonly Godot.Button[] Buttons;
        internal readonly Type Catalog, Placement, Settings, Unique;
        internal readonly Ancient Current;
        internal int Actions;
        internal object? Call(Type api, string name, params object?[] args)
            => api.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        internal Scene(Control parent, Assembly assembly)
        {
            Catalog = assembly.GetType("RefreshAncient.Compat2.CatalogUi", true)!;
            Placement = assembly.GetType("RefreshAncient.Compat2.ButtonPlacement", true)!;
            Settings = assembly.GetType("RefreshAncient.Compat2.ModSettings", true)!;
            Unique = assembly.GetType("RefreshAncient.Compat2.UniqueOptions", true)!;
            ModConfigApi.Reset();
            ModConfigApi.Entries = (ConfigEntry[])Call(Settings, "ExtendEntries", (object)ModConfigApi.OriginalEntries())!;
            Current = new Ancient(); Current.Fill(12); NEventRoom.Instance.SetEvent(Current);
            assembly.GetType("RefreshAncient.Compat2.AncientHistory", true)!.GetMethod("Filter")!.Invoke(null, new object[] { Current.Owner.RunState, Current, new List<Ancient>() });
            Root = new Control { Name = "PlacementChecks", Size = new Vector2(1280, 720) };
            parent.AddChild(Root);
            var options = new Godot.Button { Name = "RefreshAncient_RefreshOptionsButton", Size = new Vector2(250, 54) };
            var ancients = new Godot.Button { Name = "RefreshAncient_RefreshAncientButton", Size = new Vector2(250, 54) };
            Root.AddChild(options); Root.AddChild(ancients);
            options.Pressed += () => { if (Call(Unique, "CanPress", Root) is true) Actions++; };
            Update();
            Buttons = new[] { options, ancients, Root.GetNode<Godot.Button>("RefreshAncient_AllOptionsButton"), Root.GetNode<Godot.Button>("RefreshAncient_AllAncientsButton") };
        }
        internal void Update()
        {
            Input.FlushBufferedEvents();
            var options = Root.GetNode<Godot.Button>("RefreshAncient_RefreshOptionsButton");
            var ancients = Root.GetNode<Godot.Button>("RefreshAncient_RefreshAncientButton");
            // These positions/visibility are refreshed by the real mod before
            // its postfix. Exercise the same repeated-update boundary here.
            options.Visible = true; ancients.Visible = true;
            options.Position = new Vector2(650, 260); ancients.Position = new Vector2(650, 324);
            Call(Catalog, "Update", Root);
            Call(Unique, "UpdateUi", Root);
        }
        internal void Key(Key key, bool pressed)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            Update();
        }
        internal void Tap(Key key) { Key(key, true); Key(key, false); }
        internal void Drag(int id, Vector2 target)
        {
            var offset = new Vector2(8, 9);
            Buttons[id].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = offset, GlobalPosition = Buttons[id].GlobalPosition + offset });
            var global = Root.GetGlobalTransformWithCanvas() * (target + offset);
            Buttons[id].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { GlobalPosition = global, ButtonMask = MouseButtonMask.Left });
            Buttons[id].EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, GlobalPosition = global });
        }
        internal void Dispose() => Root.QueueFree();
    }
    internal static void Run(Control parent, Assembly assembly, Action<bool, string> check)
    {
        var scene = new Scene(parent, assembly);
        scene.Call(scene.Placement, "Reset", scene.Root); scene.Update();
        Vector2[] defaults = scene.Buttons.Select(b => b.Position).ToArray();
        scene.Key(Key.J, true);
        check(scene.Call(scene.Placement, "IsEditing", scene.Root) is true && scene.Buttons.All(b => b.MouseDefaultCursorShape == Control.CursorShape.Move), "J enables move mode and the four-arrow cursor on all buttons");
        for (int i = 0; i < 4; i++) scene.Update();
        check(scene.Call(scene.Placement, "IsEditing", scene.Root) is true, "holding J does not repeatedly leave move mode");
        scene.Key(Key.J, false);
        for (int i = 0; i < 4; i++) scene.Drag(i, Targets[i]);
        check(scene.Buttons.Select((button, i) => button.Position.DistanceTo(Targets[i]) < 0.01f).All(value => value), "all four buttons can move independently, including a disabled paid button");
        scene.Buttons[0].EmitSignal(BaseButton.SignalName.Pressed);
        scene.Buttons[2].EmitSignal(BaseButton.SignalName.Pressed);
        check(scene.Actions == 0 && scene.Call(scene.Catalog, "IsOpen") is false, "drag mode blocks paid refreshes and catalog opening");
        for (int i = 0; i < 4; i++) scene.Update();
        check(scene.Buttons.Select((button, i) => button.Position.DistanceTo(Targets[i]) < 0.01f).All(value => value), "normal game placement updates cannot overwrite the dragged positions");
        string path = (string)scene.Call(scene.Placement, "SettingsPath")!;
        check(File.Exists(path) && File.ReadAllLines(path).Count(line => !line.StartsWith('#')) == 4, "all four positions are saved to the user settings file");
        scene.Tap(Key.J);
        check(scene.Call(scene.Placement, "IsEditing", scene.Root) is false && scene.Buttons.All(b => b.MouseDefaultCursorShape != Control.CursorShape.Move), "pressing J again ends dragging and restores button cursors");
        var before = scene.Buttons[0].Position; scene.Drag(0, new Vector2(5, 5));
        check(scene.Buttons[0].Position == before, "normal mode does not move a button on mouse drag");
        scene.Tap(Key.K);
        check(scene.Buttons.Select((button, i) => button.Position.DistanceTo(defaults[i]) < 0.01f).All(value => value) && !File.Exists(path), "K restores all original positions and removes the saved overrides");
        scene.Tap(Key.J); scene.Drag(0, new Vector2(-500, 10000)); scene.Update();
        check(scene.Buttons[0].Position.X == 0 && scene.Buttons[0].GetRect().End.Y <= scene.Root.Size.Y, "dragging clamps buttons to the visible screen");
        scene.Tap(Key.K); scene.Tap(Key.J);
        ModConfigApi.SetValue("refreshancient.mod", "customOptionsCost", "31");
        ModConfigApi.SetValue("refreshancient.mod", "customAncientCost", "62"); scene.Update();
        check(scene.Buttons[0].Text.Contains("31 з.") && scene.Buttons[1].Text == "Новых Древних нет", "live price labels refresh while exhaustion warnings keep priority");
        ModConfigApi.SetValue("refreshancient.mod", "hideButtonsKey", "F7");
        ModConfigApi.SetValue("refreshancient.mod", "moveButtonsKey", "F8");
        ModConfigApi.SetValue("refreshancient.mod", "resetButtonsKey", "F9");
        scene.Tap(Key.J);
        check(scene.Call(scene.Placement, "IsEditing", scene.Root) is false, "the previous movement key stops working after rebinding");
        scene.Tap(Key.F8); scene.Drag(2, new Vector2(210, 410)); scene.Update();
        check(scene.Call(scene.Placement, "IsEditing", scene.Root) is true && scene.Buttons[2].Position.DistanceTo(new Vector2(210, 410)) < 0.01f, "configured movement key enables the same drag behavior");
        scene.Tap(Key.F9);
        check(scene.Buttons[2].Position == defaults[2] && !File.Exists(path), "configured reset key restores original placement");
        scene.Tap(Key.F8); scene.Tap(Key.F7);
        check(scene.Buttons.All(b => !b.Visible), "configured hide key hides all four buttons");
        scene.Tap(Key.F7);
        check(scene.Buttons.All(b => b.Visible), "configured hide key restores all four buttons");
        scene.Call(scene.Placement, "Reset", scene.Root); scene.Update();
        ModConfigApi.Reset();
        scene.Call(scene.Settings, "ExtendEntries", (object)ModConfigApi.OriginalEntries());
        scene.Dispose();
    }
    internal static void Probe(Control parent, Assembly assembly, string mode)
    {
        var scene = new Scene(parent, assembly);
        if (mode == "save")
        {
            scene.Call(scene.Placement, "Reset", scene.Root); scene.Update(); scene.Tap(Key.J);
            for (int i = 0; i < 4; i++) scene.Drag(i, Targets[i]);
            scene.Tap(Key.J);
            GD.Print("PLACEMENT PROBE SAVED: 4 positions in user storage.");
        }
        else
        {
            for (int i = 0; i < 4; i++) if (scene.Buttons[i].Position.DistanceTo(Targets[i]) > 0.02f) throw new Exception("Position was not restored in a new process: " + i);
            scene.Root.Size = new Vector2(640, 360); scene.Update();
            if (scene.Buttons.Any(button => button.Position.X < 0 || button.Position.Y < 0 || button.GetRect().End.X > 640.01f || button.GetRect().End.Y > 360.01f)) throw new Exception("Saved positions went off-screen after resizing.");
            scene.Call(scene.Placement, "Reset", scene.Root);
            GD.Print("PLACEMENT PROBE RESTORED: 4 positions after process restart; all fit a resized viewport.");
        }
        scene.Dispose();
    }
}
