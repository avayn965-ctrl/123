using System.Reflection;

namespace RefreshAncient.Compat2;

internal static class CatalogHover
{
    private static object? _owner;
    private static object? _overlay;
    private static Type? _api;

    internal static void Bind(object card, object option)
    {
        CatalogCards.EnumProperty(card, "MouseFilter", 1); // Hover + pass wheel to ScrollContainer.
        UniqueOptions.Set(card, "MouseForcePassScrollEvents", true);
        CatalogUi.On(card, "MouseEntered", () => Show(card, option));
        CatalogUi.On(card, "MouseExited", () => Remove(card));
        CatalogUi.On(card, "TreeExiting", () => Remove(card));
    }
    private static void Remove(object card)
    {
        if (ReferenceEquals(_owner, card)) Clear();
    }
    internal static void Clear()
    {
        try
        {
            if (_owner != null) _api?.GetMethod("Remove", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[] { _owner });
            if (_overlay != null) UniqueOptions.Call(_overlay, "QueueFree");
        }
        catch (Exception error) { UniqueOptions.Log(error); }
        finally { _owner = null; _overlay = null; }
    }
    private static void Show(object card, object option)
    {
        Clear();
        try
        {
            if (!CatalogUi.IsOpen()) return;
            object? tips = UniqueOptions.Get(option, "HoverTips");
            if (tips == null) return;
            bool any = false;
            foreach (object ignored in UniqueOptions.Items(tips)) { any = true; break; }
            if (!any) return;
            _api ??= UniqueOptions.LoadedType("MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet", "sts2")
                ?? throw new InvalidOperationException("Game hover-tip API was not found.");
            MethodInfo create = _api.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "CreateAndShow"
                && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.IsGenericType
                && m.GetParameters()[1].ParameterType.IsInstanceOfType(tips));
            _owner = card;
            object? set = create.Invoke(null, new[] { card, tips, Enum.Parse(create.GetParameters()[2].ParameterType, "Left") });
            if (set == null) { _owner = null; return; }
            // Game tips normally live in the main viewport. An exclusive
            // catalog is a separate Window, so give tips a local overlay.
            object window = UniqueOptions.Call(card, "GetWindow")!;
            _overlay = CatalogUi.New("CanvasLayer");
            CatalogUi.Name(_overlay, "RefreshAncient_CatalogHover");
            UniqueOptions.Set(_overlay, "Layer", 100);
            CatalogUi.Add(window, _overlay);
            UniqueOptions.Call(set, "Reparent", _overlay, false);
            UniqueOptions.Call(set, "SetAlignment", card, Enum.Parse(create.GetParameters()[2].ParameterType, "Left"));
            UniqueOptions.Set(set, "MouseFilter", Enum.ToObject(set.GetType().GetProperty("MouseFilter")!.PropertyType, 2));
            foreach (object child in UniqueOptions.Items(UniqueOptions.Call(set, "GetChildren", false))) Clamp(child, window);
        }
        catch (Exception error) { UniqueOptions.Log(error); Clear(); }
    }
    private static float Coordinate(object node, string property, string axis)
        => Convert.ToSingle(UniqueOptions.Get(UniqueOptions.Get(node, property), axis));
    private static void Clamp(object child, object window)
    {
        if (child.GetType().GetProperty("GlobalPosition") == null) return;
        float width = Coordinate(window, "Size", "X"), height = Coordinate(window, "Size", "Y");
        float x = Coordinate(child, "GlobalPosition", "X"), y = Coordinate(child, "GlobalPosition", "Y");
        float w = Coordinate(child, "Size", "X"), h = Coordinate(child, "Size", "Y");
        UniqueOptions.Set(child, "GlobalPosition", CatalogUi.Vector(Math.Clamp(x, 8, Math.Max(8, width - w - 8)), Math.Clamp(y, 8, Math.Max(8, height - h - 8))));
    }
}
