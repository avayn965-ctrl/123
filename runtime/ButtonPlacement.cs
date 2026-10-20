using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RefreshAncient.Compat2;

public static class ButtonPlacement
{
    private sealed class State
    {
        public object Layout = null!;
        public object?[] Buttons = new object?[4];
        public object?[] Cursors = new object?[4];
        public bool Editing;
        public bool SuppressPress;
        public int Dragging = -1;
        public float OffsetX, OffsetY;
    }
    private static readonly ConditionalWeakTable<object, State> States = new();
    private static readonly Dictionary<int, float[]> Positions = new();
    private static bool _loaded;
    private static State? _cursorOwner;

    public static string SettingsPath()
    {
        Type project = CatalogUi.GodotType("ProjectSettings");
        string directory = (string)project.GetMethod("GlobalizePath", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { "user://" })!;
        return Path.Combine(directory, "refreshancient-button-positions.cfg");
    }
    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string path = SettingsPath();
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] pair = line.Split('=');
                if (pair.Length != 2 || !int.TryParse(pair[0], out int id) || id is < 0 or > 3) continue;
                string[] point = pair[1].Split(',');
                if (point.Length != 2 || !float.TryParse(point[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(point[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.IsFinite(x) || !float.IsFinite(y)) continue;
                Positions[id] = new[] { Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1) };
            }
        }
        catch (Exception error) { UniqueOptions.Log(error); }
    }
    private static void Save()
    {
        try
        {
            string path = SettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (Positions.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            List<string> lines = new() { "# RefreshAncient button positions v1 (normalized to the available screen area)" };
            foreach (var point in Positions.OrderBy(p => p.Key))
                lines.Add(point.Key.ToString(CultureInfo.InvariantCulture) + "=" + point.Value[0].ToString("R", CultureInfo.InvariantCulture) + "," + point.Value[1].ToString("R", CultureInfo.InvariantCulture));
            File.WriteAllLines(path + ".tmp", lines);
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) { UniqueOptions.Log(error); }
    }
    private static float Coordinate(object node, string property, string axis)
        => Convert.ToSingle(UniqueOptions.Get(UniqueOptions.Get(node, property), axis));
    private static float Span(State state, object button, string axis)
        => Math.Max(0, Coordinate(state.Layout, "Size", axis) - Coordinate(button, "Size", axis));
    private static bool LeftDown()
    {
        MethodInfo pressed = CatalogUi.GodotType("Input").GetMethod("IsMouseButtonPressed", BindingFlags.Public | BindingFlags.Static)!;
        return pressed.Invoke(null, new[] { Enum.Parse(pressed.GetParameters()[0].ParameterType, "Left") }) is true;
    }
    public static bool BlocksClick(object layout)
        => States.TryGetValue(layout, out State? state) && (state.Editing || state.SuppressPress);
    public static bool IsEditing(object layout) => States.TryGetValue(layout, out State? state) && state.Editing;
    private static State Get(object layout)
    {
        if (States.TryGetValue(layout, out State? state)) return state;
        state = new State { Layout = layout };
        States.Add(layout, state);
        State value = state;
        CatalogUi.On(layout, "TreeExiting", () =>
        {
            FinishDrag(value);
            value.Editing = false;
            Cursor(value, false);
        });
        return state;
    }
    public static void Toggle(object layout)
    {
        State state = Get(layout);
        if (state.Editing) FinishDrag(state);
        state.Editing = !state.Editing;
        Cursor(state, state.Editing);
    }
    public static void Reset(object layout)
    {
        State state = Get(layout);
        // Do not let a pending release recreate the position just cleared.
        state.Dragging = -1;
        Positions.Clear(); _loaded = true;
        Save();
    }
    private static void Cursor(State state, bool active)
    {
        if (active) _cursorOwner = state;
        if (!active && !ReferenceEquals(_cursorOwner, state)) return;
        Type input = CatalogUi.GodotType("Input");
        MethodInfo set = input.GetMethod("SetDefaultCursorShape", BindingFlags.Public | BindingFlags.Static)!;
        set.Invoke(null, new[] { Enum.Parse(set.GetParameters()[0].ParameterType, active ? "Move" : "Arrow") });
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            object? button = state.Buttons[i];
            if (button == null) continue;
            object previous = state.Cursors[i]!;
            UniqueOptions.Set(button, "MouseDefaultCursorShape", active ? Enum.Parse(previous.GetType(), "Move") : previous);
        }
        if (!active) _cursorOwner = null;
    }
    internal static void Apply(object layout, object? options, object? ancients, object allOptions, object allAncients)
    {
        Load();
        State state = Get(layout);
        object?[] buttons = { options, ancients, allOptions, allAncients };
        for (int i = 0; i < buttons.Length; i++)
        {
            object? button = buttons[i];
            if (button == null) continue;
            if (!ReferenceEquals(state.Buttons[i], button))
            {
                state.Buttons[i] = button;
                state.Cursors[i] = UniqueOptions.Get(button, "MouseDefaultCursorShape");
                int id = i;
                CatalogUi.OnGuiInput(button, value => Input(state, id, value));
            }
            if (Positions.TryGetValue(i, out float[]? point))
                UniqueOptions.Set(button, "Position", CatalogUi.Vector(Span(state, button, "X") * point[0], Span(state, button, "Y") * point[1]));
        }
        bool shown = false;
        foreach (object? button in buttons) if (button != null && UniqueOptions.Get(button, "Visible") is true) shown = true;
        if (state.SuppressPress && !LeftDown() && state.Dragging < 0) state.SuppressPress = false;
        Cursor(state, state.Editing && shown && !CatalogUi.IsOpen());
    }
    private static void Remember(State state, int id)
    {
        object button = state.Buttons[id]!;
        float spanX = Span(state, button, "X"), spanY = Span(state, button, "Y");
        Positions[id] = new[] { spanX > 0 ? Coordinate(button, "Position", "X") / spanX : 0, spanY > 0 ? Coordinate(button, "Position", "Y") / spanY : 0 };
    }
    private static void FinishDrag(State state)
    {
        if (state.Dragging < 0) return;
        state.Dragging = -1;
        Save();
    }
    private static object LocalMouse(State state, object value)
    {
        object transform = UniqueOptions.Call(state.Layout, "GetGlobalTransformWithCanvas")!;
        object inverse = UniqueOptions.Call(transform, "AffineInverse")!;
        object global = UniqueOptions.Get(value, "GlobalPosition")!;
        MethodInfo multiply = transform.GetType().GetMethod("op_Multiply", BindingFlags.Public | BindingFlags.Static,
            null, new[] { transform.GetType(), global.GetType() }, null)!;
        return multiply.Invoke(null, new[] { inverse, global })!;
    }
    private static void Input(State state, int id, object value)
    {
        try
        {
            if (!state.Editing || CatalogUi.IsOpen()) return;
            object button = state.Buttons[id]!;
            object? index = UniqueOptions.Get(value, "ButtonIndex");
            if (index?.ToString() == "Left")
            {
                if (UniqueOptions.Get(value, "Pressed") is true)
                {
                    state.Dragging = id; state.SuppressPress = true;
                    Remember(state, id);
                    object point = UniqueOptions.Get(value, "Position")!;
                    state.OffsetX = Convert.ToSingle(UniqueOptions.Get(point, "X"));
                    state.OffsetY = Convert.ToSingle(UniqueOptions.Get(point, "Y"));
                }
                else if (state.Dragging == id) FinishDrag(state);
                UniqueOptions.Call(button, "AcceptEvent");
            }
            else if (state.Dragging == id && value.GetType().Name == "InputEventMouseMotion")
            {
                object point = LocalMouse(state, value);
                float x = Math.Clamp(Convert.ToSingle(UniqueOptions.Get(point, "X")) - state.OffsetX, 0, Span(state, button, "X"));
                float y = Math.Clamp(Convert.ToSingle(UniqueOptions.Get(point, "Y")) - state.OffsetY, 0, Span(state, button, "Y"));
                UniqueOptions.Set(button, "Position", CatalogUi.Vector(x, y));
                Remember(state, id);
                UniqueOptions.Call(button, "AcceptEvent");
            }
        }
        catch (Exception error) { UniqueOptions.Log(error); }
    }
}
