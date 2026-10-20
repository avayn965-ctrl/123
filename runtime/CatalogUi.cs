using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RefreshAncient.Compat2;

// Reflection keeps the shipped helper dependency-free. Native Godot UI tests
// exercise these exact calls against GodotSharp 4.5.1 before packaging.
public static class CatalogUi
{
    private sealed class View
    {
        public object Layout = null!;
        public object OptionsButton = null!;
        public object AncientsButton = null!;
        public object? Dialog;
        public object? Text;
    }
    private static readonly ConditionalWeakTable<object, View> Views = new();
    private static WeakReference<object>? _openDialog;
    private static ulong _suppressCancelUntil;

    private static Assembly? _godotSharp;
    private static Type GodotType(string name)
    {
        _godotSharp ??= UniqueOptions.LoadedType("Godot.GodotObject", "GodotSharp")?.Assembly
            ?? throw new InvalidOperationException("GodotSharp is not loaded.");
        return _godotSharp.GetType("Godot." + name, true)!;
    }
    private static object New(string name) => Activator.CreateInstance(GodotType(name))!;
    private static object Vector(float x, float y) => Activator.CreateInstance(GodotType("Vector2"), x, y)!;
    private static object VectorI(int x, int y) => Activator.CreateInstance(GodotType("Vector2I"), x, y)!;
    private static void Name(object node, string name)
    {
        Type type = node.GetType().GetProperty("Name")!.PropertyType;
        UniqueOptions.Set(node, "Name", Activator.CreateInstance(type, name)!);
    }
    private static void Add(object parent, object child)
    {
        foreach (MethodInfo method in parent.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            ParameterInfo[] p = method.GetParameters();
            if (method.Name == "AddChild" && p.Length == 3 && p[1].ParameterType == typeof(bool) && p[2].ParameterType.IsEnum)
            {
                method.Invoke(parent, new[] { child, false, Enum.ToObject(p[2].ParameterType, 0) });
                return;
            }
        }
        throw new MissingMethodException(parent.GetType().FullName, "AddChild");
    }
    private static void On(object node, string signal, Action action)
        => (node.GetType().GetEvent(signal) ?? throw new MissingMemberException(node.GetType().FullName, signal)).AddEventHandler(node, action);

    private static void Font(object control, string key, int size)
    {
        object name = Activator.CreateInstance(GodotType("StringName"), key)!;
        UniqueOptions.Call(control, "AddThemeFontSizeOverride", name, size);
    }
    private static float Coordinate(object node, string property, string coordinate)
        => Convert.ToSingle(UniqueOptions.Get(UniqueOptions.Get(node, property), coordinate));

    public static bool IsOpen()
    {
        try { return _openDialog != null && _openDialog.TryGetTarget(out object? dialog) && UniqueOptions.Get(dialog, "Visible") is true; }
        catch { _openDialog = null; return false; }
    }
    private static ulong Frame()
    {
        Type? engine = UniqueOptions.LoadedType("Godot.Engine", "GodotSharp");
        return engine == null ? 0UL : Convert.ToUInt64(engine.GetMethod("GetProcessFrames", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null));
    }
    public static bool ShouldSuppressCancel() => IsOpen() || (_suppressCancelUntil != 0 && Frame() <= _suppressCancelUntil);

    public static string LocalizedText(object? text, string fallback)
    {
        if (text == null) return fallback;
        if (text is string plain) return StripMarkup(plain);
        try { return StripMarkup(Convert.ToString(UniqueOptions.Call(text, "GetFormattedText")) ?? fallback); }
        catch
        {
            try { return StripMarkup(Convert.ToString(UniqueOptions.Call(text, "GetRawText")) ?? fallback); }
            catch { return fallback; }
        }
    }
    internal static string ModelText(object model, string property, string fallback)
    {
        try { return LocalizedText(UniqueOptions.Get(model, property), fallback); }
        catch { return fallback; }
    }
    private static string StripMarkup(string text) => Regex.Replace(text, @"\[/?[a-zA-Z][^\]]*\]", string.Empty);
    private static string Text(string russian, string english, string chinese)
        => UniqueOptions.Translate(UniqueOptions.Language(), russian, english, chinese);

    public static string FormatCatalog(string[][] rows, bool ancients)
    {
        StringBuilder result = new();
        int available = 0;
        foreach (string[] row in rows) if (row[2] == "available") available++;
        result.Append(Text("Всего: ", "Total: ", "总计：")).Append(rows.Length)
            .Append(Text(" · Ещё доступны: ", " · Still available: ", " · 仍可获得：")).Append(available).AppendLine().AppendLine();
        result.AppendLine(ancients
            ? Text("Список для текущего события с учётом акта и разблокировок.", "The current event pool, respecting act and unlock restrictions.", "当前事件的先古之民列表，遵循章节与解锁限制。")
            : Text("Все варианты текущего Древнего. Недоступные награды отмечены отдельно.", "All options for this Ancient. Ineligible rewards are marked separately.", "当前先古之民的全部选项，不可用的奖励会单独标记。"));
        result.AppendLine(Text("Только просмотр — золото не расходуется.", "View only — no gold is spent.", "仅供查看，不会消耗金币。")).AppendLine();
        if (rows.Length == 0) result.AppendLine(Text("Вариантов нет.", "No entries.", "没有条目。"));
        int index = 0;
        foreach (string[] row in rows)
        {
            string status = row[2] == "current" ? Text(ancients ? "Сейчас здесь" : "На экране", "Current", "当前")
                : row[2] == "seen" ? Text("Уже показан", "Already shown", "已展示")
                : row[2] == "available" ? Text("Ещё доступен", "Available", "仍可获得")
                : Text("Недоступен", "Unavailable", "不可用");
            result.Append(++index).Append(". ").Append(row[0]).Append("  [").Append(status).AppendLine("]");
            if (!string.IsNullOrWhiteSpace(row[1])) result.AppendLine(row[1]);
            result.AppendLine();
        }
        return result.ToString();
    }

    private static View Ensure(object layout)
    {
        if (Views.TryGetValue(layout, out View? view)) return view;
        view = new View { Layout = layout, OptionsButton = New("Button"), AncientsButton = New("Button") };
        Name(view.OptionsButton, "RefreshAncient_AllOptionsButton");
        Name(view.AncientsButton, "RefreshAncient_AllAncientsButton");
        UniqueOptions.Set(view.OptionsButton, "CustomMinimumSize", Vector(210, 54));
        UniqueOptions.Set(view.AncientsButton, "CustomMinimumSize", Vector(210, 54));
        UniqueOptions.Set(view.OptionsButton, "Visible", false);
        UniqueOptions.Set(view.AncientsButton, "Visible", false);
        UniqueOptions.Set(view.OptionsButton, "ZIndex", 100);
        UniqueOptions.Set(view.AncientsButton, "ZIndex", 100);
        Font(view.OptionsButton, "font_size", 18);
        Font(view.AncientsButton, "font_size", 18);
        On(view.OptionsButton, "Pressed", () => Open(view.Layout, false));
        On(view.AncientsButton, "Pressed", () => Open(view.Layout, true));
        Add(layout, view.OptionsButton); Add(layout, view.AncientsButton);
        Views.Add(layout, view);
        return view;
    }

    public static void Update(object layout)
    {
        try
        {
            object? options = UniqueOptions.Call(layout, "FindChild", "RefreshAncient_RefreshOptionsButton", true, false);
            object? ancients = UniqueOptions.Call(layout, "FindChild", "RefreshAncient_RefreshAncientButton", true, false);
            if (options == null && ancients == null) return;
            View view = Ensure(layout);
            bool showOptions = options != null && UniqueOptions.Get(options, "Visible") is true;
            bool showAncients = ancients != null && UniqueOptions.Get(ancients, "Visible") is true;
            UniqueOptions.Set(view.OptionsButton, "Visible", showOptions);
            UniqueOptions.Set(view.AncientsButton, "Visible", showAncients);
            UniqueOptions.Set(view.OptionsButton, "Disabled", false);
            UniqueOptions.Set(view.AncientsButton, "Disabled", false);
            UniqueOptions.Set(view.OptionsButton, "Text", Text("Все варианты", "All Options", "全部选项"));
            UniqueOptions.Set(view.AncientsButton, "Text", Text("Все Древние", "All Ancients", "全部先古之民"));
            UniqueOptions.Set(view.OptionsButton, "TooltipText", Text("Посмотреть все награды и их доступность.", "View every reward and its availability.", "查看全部奖励及其可用状态。"));
            UniqueOptions.Set(view.AncientsButton, "TooltipText", Text("Посмотреть Древних текущего пула и уже встреченных.", "View the current Ancient pool and those already encountered.", "查看当前先古之民列表及已遇到的条目。"));
            object? current = UniqueOptions.CurrentAncient();
            if (showAncients && current != null && AncientHistory.Remaining(current) == 0)
            {
                UniqueOptions.Set(ancients!, "Disabled", true);
                UniqueOptions.Set(ancients!, "Text", Text("Новых Древних нет", "No new Ancients", "没有新先古之民"));
                UniqueOptions.Set(ancients!, "TooltipText", Text("Все доступные Древние в этой комнате уже были показаны. Золото не списывается.", "All available Ancients in this room have already been shown. No gold is charged.", "本房间所有可用的先古之民都已展示，不会扣除金币。"));
            }
            Place(layout, view, options, ancients, showOptions, showAncients);
        }
        catch (Exception error) { UniqueOptions.Log(error); }
    }

    private static void Place(object layout, View view, object? options, object? ancients, bool showOptions, bool showAncients)
    {
        object? anchor = showOptions ? options : showAncients ? ancients : null;
        if (anchor == null) return;
        float x = Coordinate(anchor, "Position", "X"), y = Coordinate(anchor, "Position", "Y");
        float width = Coordinate(anchor, "Size", "X"), height = Coordinate(anchor, "Size", "Y");
        if (width <= 0) width = 250; if (height <= 0) height = 54;
        float fullWidth = Coordinate(layout, "Size", "X");
        int count = (showOptions ? 1 : 0) + (showAncients ? 1 : 0);
        float center = y + (count * height + (count - 1) * 10) / 2;
        bool columns = x + width + 10 + 210 <= fullWidth - 12;
        float start = columns ? y : center - (count * 2 * height + (count * 2 - 1) * 8) / 2;
        int row = 0;
        void Pair(object source, object browse)
        {
            float sourceY = start + row * (height + (columns ? 10 : 8));
            UniqueOptions.Set(source, "Position", Vector(x, sourceY));
            UniqueOptions.Set(browse, "Size", Vector(columns ? 210 : width, height));
            UniqueOptions.Set(browse, "Position", Vector(columns ? x + width + 10 : x, columns ? sourceY : sourceY + height + 8));
            row += columns ? 1 : 2;
        }
        if (showOptions) Pair(options!, view.OptionsButton);
        if (showAncients) Pair(ancients!, view.AncientsButton);
    }

    public static void Open(object layout, bool ancients)
    {
        if (IsOpen()) return;
        try
        {
            object? current = UniqueOptions.CurrentAncient();
            if (current == null) return;
            View view = Ensure(layout);
            string[][] rows = ancients ? AncientHistory.Catalog(current) : UniqueOptions.OptionCatalog(current);
            if (view.Dialog == null)
            {
                view.Dialog = New("AcceptDialog");
                Name(view.Dialog, "RefreshAncient_CatalogDialog");
                UniqueOptions.Set(view.Dialog, "Transient", true);
                UniqueOptions.Set(view.Dialog, "Exclusive", true);
                UniqueOptions.Set(view.Dialog, "DialogHideOnOk", true);
                UniqueOptions.Set(view.Dialog, "DialogCloseOnEscape", true);
                UniqueOptions.Set(view.Dialog, "Visible", false);
                UniqueOptions.Set(view.Dialog, "DialogText", string.Empty);
                view.Text = New("RichTextLabel");
                Name(view.Text, "RefreshAncient_CatalogText");
                UniqueOptions.Set(view.Text, "BbcodeEnabled", false);
                UniqueOptions.Set(view.Text, "ScrollActive", true);
                UniqueOptions.Set(view.Text, "SelectionEnabled", true);
                UniqueOptions.Set(view.Text, "FitContent", false);
                UniqueOptions.Set(view.Text, "CustomMinimumSize", Vector(720, 380));
                Font(view.Text, "normal_font_size", 20);
                Add(view.Dialog, view.Text); Add(layout, view.Dialog);
                UniqueOptions.Set(UniqueOptions.Call(view.Dialog, "GetLabel")!, "Visible", false);
                On(view.Dialog, "Confirmed", () => Close(view));
                On(view.Dialog, "Canceled", () => Close(view));
                On(view.Dialog, "CloseRequested", () => Close(view));
            }
            UniqueOptions.Set(view.Dialog, "Title", ancients ? Text("Все Древние", "All Ancients", "全部先古之民") : Text("Все варианты", "All Options", "全部选项"));
            UniqueOptions.Set(view.Dialog, "OkButtonText", Text("Закрыть", "Close", "关闭"));
            UniqueOptions.Set(view.Text!, "Text", FormatCatalog(rows, ancients));
            _openDialog = new WeakReference<object>(view.Dialog);
            UniqueOptions.Call(view.Dialog, "PopupCenteredClamped", VectorI(860, 540), 0.9f);
            UniqueOptions.Call(UniqueOptions.Call(view.Dialog, "GetOkButton")!, "GrabFocus");
        }
        catch (Exception error) { _openDialog = null; UniqueOptions.Log(error); }
    }

    private static void Close(View view)
    {
        if (view.Dialog != null) UniqueOptions.Call(view.Dialog, "Hide");
        _openDialog = null;
        _suppressCancelUntil = Frame() + 2;
    }
}
