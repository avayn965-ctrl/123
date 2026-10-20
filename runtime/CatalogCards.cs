namespace RefreshAncient.Compat2;

// Only visual nodes are copied, with Duplicate(0): no scripts, signals or
// event-option callbacks. The real gameplay buttons are never reparented.
internal static class CatalogCards
{
    internal static void EnumProperty(object node, string property, int value)
        => UniqueOptions.Set(node, property, Enum.ToObject(node.GetType().GetProperty(property)!.PropertyType, value));
    internal static void Constant(object node, string name, int value)
        => UniqueOptions.Call(node, "AddThemeConstantOverride", Activator.CreateInstance(CatalogUi.GodotType("StringName"), name), value);
    private static object Color(float r, float g, float b, float a)
        => Activator.CreateInstance(CatalogUi.GodotType("Color"), r, g, b, a)!;
    private static void Style(object node, object style)
        => UniqueOptions.Call(node, "AddThemeStyleboxOverride", Activator.CreateInstance(CatalogUi.GodotType("StringName"), "panel"), style);
    private static void Passive(object node)
    {
        if (node.GetType().GetProperty("MouseFilter") != null) EnumProperty(node, "MouseFilter", 2);
        if (node.GetType().GetProperty("FocusMode") != null) EnumProperty(node, "FocusMode", 0);
        foreach (object child in UniqueOptions.Items(UniqueOptions.Call(node, "GetChildren", false))) Passive(child);
    }
    private static object? Find(object node, string name) => UniqueOptions.Call(node, "FindChild", name, true, false);
    private static object? Template(object layout, string name)
    {
        foreach (object button in UniqueOptions.Items(UniqueOptions.Get(layout, "OptionButtons")))
        {
            object? visual = Find(button, name);
            if (visual != null) return visual;
        }
        return null;
    }
    private static object Copy(object source)
    {
        object copy = UniqueOptions.Call(source, "Duplicate", 0)!;
        UniqueOptions.Set(copy, "Visible", true);
        UniqueOptions.Set(copy, "Scale", CatalogUi.Vector(1, 1));
        UniqueOptions.Set(copy, "Rotation", 0f);
        Passive(copy);
        return copy;
    }
    private static void CopyFont(object source, object target, string name)
    {
        object key = Activator.CreateInstance(CatalogUi.GodotType("StringName"), name)!;
        object empty = Activator.CreateInstance(CatalogUi.GodotType("StringName"), string.Empty)!;
        object font = UniqueOptions.Call(source, "GetThemeFont", key, empty)!;
        UniqueOptions.Call(target, "AddThemeFontOverride", key, font);
    }
    private static void CopyTextTheme(object source, object target)
    {
        object empty = Activator.CreateInstance(CatalogUi.GodotType("StringName"), string.Empty)!;
        foreach (string name in new[] { "default_color", "font_outline_color", "font_shadow_color" })
        {
            object key = Activator.CreateInstance(CatalogUi.GodotType("StringName"), name)!;
            UniqueOptions.Call(target, "AddThemeColorOverride", key, UniqueOptions.Call(source, "GetThemeColor", key, empty));
        }
        foreach (string name in new[] { "outline_size", "shadow_offset_x", "shadow_offset_y" })
        {
            object key = Activator.CreateInstance(CatalogUi.GodotType("StringName"), name)!;
            Constant(target, name, Convert.ToInt32(UniqueOptions.Call(source, "GetThemeConstant", key, empty)));
        }
    }
    private static object Label(object? template, string text)
    {
        object label = template == null ? CatalogUi.New("RichTextLabel") : Copy(template);
        CatalogUi.Name(label, "Text");
        if (template != null)
        {
            CopyFont(template, label, "normal_font");
            CopyFont(template, label, "bold_font");
            CopyFont(template, label, "italics_font");
            CopyTextTheme(template, label);
        }
        UniqueOptions.Set(label, "Modulate", Color(1, 1, 1, 1));
        UniqueOptions.Set(label, "CustomMinimumSize", CatalogUi.Vector(0, 0));
        UniqueOptions.Set(label, "BbcodeEnabled", true);
        UniqueOptions.Set(label, "FitContent", true);
        UniqueOptions.Set(label, "ScrollActive", false);
        UniqueOptions.Set(label, "VisibleCharacters", -1);
        EnumProperty(label, "AutowrapMode", 3);
        EnumProperty(label, "SizeFlagsHorizontal", 3);
        CatalogUi.Font(label, "normal_font_size", 24);
        CatalogUi.Font(label, "bold_font_size", 24);
        CatalogUi.Font(label, "italics_font_size", 24);
        UniqueOptions.Set(label, "Text", text);
        Passive(label);
        return label;
    }
    private static object? Texture(object model, string property)
    {
        try { return UniqueOptions.Get(model, property); }
        catch (Exception error) { UniqueOptions.Log(error); return null; }
    }
    private static void Icon(object parent, object? model, bool ancient)
    {
        if (model == null) return;
        // These are the exact textures NTopBarRoomIcon and NEventOptionButton
        // use, including the outline. MapIcon is a different Ancient portrait.
        object? texture = Texture(model, ancient ? "RunHistoryIcon" : "Icon");
        if (texture == null) return;
        object frame = CatalogUi.New("Control");
        CatalogUi.Name(frame, "IconFrame");
        int size = ancient ? 56 : 80;
        UniqueOptions.Set(frame, "CustomMinimumSize", CatalogUi.Vector(size, size));
        EnumProperty(frame, "SizeFlagsVertical", 4);
        object? outline = Texture(model, ancient ? "RunHistoryIconOutline" : "IconOutline");
        if (outline != null) Image(frame, "Outline", outline, size);
        Image(frame, "Icon", texture, size);
        CatalogUi.Add(parent, frame);
    }
    private static void Image(object parent, string name, object texture, int size)
    {
        object image = CatalogUi.New("TextureRect");
        CatalogUi.Name(image, name);
        UniqueOptions.Set(image, "Texture", texture);
        EnumProperty(image, "ExpandMode", 1);
        EnumProperty(image, "StretchMode", 5);
        UniqueOptions.Set(image, "Size", CatalogUi.Vector(size, size));
        Passive(image);
        CatalogUi.Add(parent, image);
    }
    public static int StatusRank(string status) => status == "seen" ? 0 : status == "current" ? 1 : status == "available" ? 2 : 3;
    private static void Border(object card, string status)
    {
        object panel = CatalogUi.New("Panel");
        CatalogUi.Name(panel, "StatusBorder");
        object style = CatalogUi.New("StyleBoxFlat");
        UniqueOptions.Set(style, "DrawCenter", false);
        UniqueOptions.Set(style, "BorderWidthLeft", 3);
        UniqueOptions.Set(style, "BorderWidthRight", 3);
        UniqueOptions.Set(style, "BorderWidthTop", 3);
        UniqueOptions.Set(style, "BorderWidthBottom", 3);
        UniqueOptions.Set(style, "CornerRadiusTopLeft", 3);
        UniqueOptions.Set(style, "CornerRadiusTopRight", 3);
        UniqueOptions.Set(style, "CornerRadiusBottomLeft", 3);
        UniqueOptions.Set(style, "CornerRadiusBottomRight", 3);
        UniqueOptions.Set(style, "BorderColor", status == "seen" ? Color(1, 0.05f, 0.05f, 1)
            : status == "current" ? Color(0.12f, 0.95f, 0.2f, 1)
            : status == "available" ? Color(1, 0.87f, 0, 1) : Color(0.5f, 0.5f, 0.5f, 1));
        Style(panel, style);
        CatalogUi.Add(card, panel);
    }
    internal static void Fill(object layout, object parent, string[][] rows, object[] models, bool ancients)
    {
        CatalogHover.Clear();
        foreach (object child in UniqueOptions.Items(UniqueOptions.Call(parent, "GetChildren", false)))
        {
            UniqueOptions.Call(parent, "RemoveChild", child);
            UniqueOptions.Call(child, "QueueFree");
        }
        object? imageTemplate = Template(layout, "Image");
        object? textTemplate = Template(layout, "Text");
        IEnumerable<int> order = Enumerable.Range(0, rows.Length);
        if (!ancients) order = order.OrderBy(i => StatusRank(rows[i][2]));
        foreach (int i in order)
        {
            object card = CatalogUi.New("PanelContainer");
            CatalogUi.Name(card, "CatalogCard_" + i);
            EnumProperty(card, "SizeFlagsHorizontal", 3);
            UniqueOptions.Set(card, "CustomMinimumSize", CatalogUi.Vector(0, ancients ? 100 : 124));
            Style(card, CatalogUi.New("StyleBoxEmpty"));
            if (imageTemplate != null)
            {
                object background = Copy(imageTemplate);
                CatalogUi.Name(background, "Image");
                CatalogUi.Add(card, background);
            }
            else
            {
                object style = CatalogUi.New("StyleBoxFlat");
                UniqueOptions.Set(style, "BgColor", Color(0.08f, 0.12f, 0.22f, 1));
                Style(card, style);
            }
            object margin = CatalogUi.New("MarginContainer");
            Constant(margin, "margin_left", 16); Constant(margin, "margin_right", 18);
            Constant(margin, "margin_top", 12); Constant(margin, "margin_bottom", 12);
            object body = CatalogUi.New("HBoxContainer");
            Constant(body, "separation", 18);
            object? model = i < models.Length ? models[i] : null;
            Icon(body, ancients ? model : UniqueOptions.Get(model, "Relic"), ancients);
            object texts = CatalogUi.New("VBoxContainer");
            EnumProperty(texts, "SizeFlagsHorizontal", 3);
            EnumProperty(texts, "SizeFlagsVertical", 4);
            string text = "[color=#efc851][b]" + rows[i][0] + "[/b][/color]";
            if (!string.IsNullOrWhiteSpace(rows[i][1])) text += "\n" + rows[i][1];
            CatalogUi.Add(texts, Label(textTemplate, text));
            object status = CatalogUi.New("Label");
            CatalogUi.Name(status, "Status");
            UniqueOptions.Set(status, "Text", CatalogUi.Status(rows[i][2], ancients));
            CatalogUi.Font(status, "font_size", 15);
            UniqueOptions.Set(status, "Modulate", Color(0.75f, 0.79f, 0.87f, 1));
            CatalogUi.Add(texts, status);
            CatalogUi.Add(body, texts); CatalogUi.Add(margin, body); CatalogUi.Add(card, margin);
            if (!ancients) Border(card, rows[i][2]);
            Passive(card);
            if (!ancients && model != null) CatalogHover.Bind(card, model);
            CatalogUi.Add(parent, card);
        }
        object? scroll = UniqueOptions.Call(parent, "GetParent");
        EnumProperty(parent, "MouseFilter", 2);
        if (scroll != null) UniqueOptions.Set(scroll, "ScrollVertical", 0);
    }
}
