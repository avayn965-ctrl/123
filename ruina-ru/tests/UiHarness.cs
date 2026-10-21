using Godot;
using RuinaRussian;
using MegaCrit.Sts2.Core.Localization;
using GuZhenRen.Systems;
using MegaCrit.Sts2.Core.Nodes.Cards;

public partial class UiHarness : Control
{
    private int _checks;
    private Label _modLabel = null!;
    private Label _baseLabel = null!;
    private readonly LocString _card = new("cards", "RUINA2-WISDOM.title");
    private readonly LocString _baseCard = new("cards", "STRIKE.title");
    private void Check(bool value, string text)
    {
        if (!value) throw new Exception(text);
        _checks++; GD.Print("PASS: " + text);
    }
    private async Task Frames(int count = 2)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Key(bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.P, PhysicalKeycode = Godot.Key.P, Pressed = pressed });
        Input.FlushBufferedEvents();
        await Frames();
    }
    private async Task Shift(bool pressed)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Shift, PhysicalKeycode = Godot.Key.Shift, Pressed = pressed });
        Input.FlushBufferedEvents(); await Frames();
    }
    private async Task Tap() { await Key(true); await Key(false); }
    private void RefreshText() { _modLabel.Text = _card.GetRawText(); _baseLabel.Text = _baseCard.GetRawText(); }
    public override async void _Ready()
    {
        try
        {
            ProcessMode = ProcessModeEnum.Always;
            string saved = ProjectSettings.GlobalizePath("user://ruina-russian-language.txt");
            string? probe = System.Environment.GetEnvironmentVariable("RUINA_RU_PROBE");
            if (probe == null && File.Exists(saved)) File.Delete(saved);
            Entry.Initialize();
            if (probe != null)
            {
                if (probe == "save")
                {
                    await Tap(); Check(TranslationCatalog.English && File.ReadAllText(saved) == "eng", "English saved to local settings");
                }
                else if (probe == "peer") Check(!TranslationCatalog.English, "another player's process keeps Russian independently");
                else Check(TranslationCatalog.English, "English restored in a new Godot process");
                GD.Print("LANGUAGE PROBE " + probe + " PASSED"); GetTree().Quit(); return;
            }
            _modLabel = new Label(); _baseLabel = new Label(); AddChild(_modLabel); AddChild(_baseLabel);
            LocManager.Instance.LocaleChanged += RefreshText;
            RefreshText();
            var visibleCard = new NCard { Model = new Ruina2.Ruina2Code.Cards.TestCard(_card) };
            AddChild(visibleCard); visibleCard.UpdateVisuals(Pile.Hand, Preview.Upgrade);
            string russian = _modLabel.Text, original = _baseLabel.Text;
            Check(!TranslationCatalog.English && russian != "Wisdom" && russian.Any(c => c is >= 'А' and <= 'я'), "Russian is the default for this mod");
            Check(_card.GetRawText() == russian, "real Harmony patch overrides the original LocString method");
            Check(_baseLabel.Text == "Original: cards/STRIKE.title", "base game keys retain the original method");
            Check(new LocString("cards", "OTHER_MOD.title").GetRawText() == "Original: cards/OTHER_MOD.title", "other mods keep their own localization");
            Check(new LocString("wrong_table", "RUINA2-WISDOM.title").GetRawText().StartsWith("Original:"), "lookup respects the localization table");
            var originalRelic = new GuZhenRen.Relics.ShuiWenGu();
            string unpatchedInput = await originalRelic.AfterCardPlayed(new MegaCrit.Sts2.Core.Models.CardModel(_card));
            Check(unpatchedInput == "Original: cards/RUINA2-WISDOM.title|Original: cards/RUINA2-WISDOM.title",
                "title-counting gameplay keeps original text even before GuZhenRenRussian initializes");
            Check(_card.GetRawText() == russian, "fallback gameplay guard does not change normal display text");
            Entry.Initialize(); await Key(true);
            Check(TranslationCatalog.English && _modLabel.Text == "Wisdom", "P changes already-visible text to exact original English");
            LocManager.Instance.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
            string plural = LocManager.Instance.SmartFormat(new LocString("powers", "RUINA2-OBLIVION.smartDescription"), new() { ["Amount"] = 3 });
            Check(plural == "Draw [blue]3[/blue] less cards each turn.", "English plural text formats correctly even with Chinese game culture");
            Check(LocManager.Instance.CultureInfo.Name == "zh-CN"
                && FormattingCulture.ForText(LocManager.Instance, _baseCard).Name == "zh-CN",
                "mod formatter never changes base game culture or formatting");
            LocManager.Instance.CultureInfo = System.Globalization.CultureInfo.GetCultureInfo("ru");
            Check(visibleCard.ShownTitle == "Wisdom" && visibleCard.DisplayingPreview == Preview.Upgrade && visibleCard.DisplayingPile == Pile.Hand, "visible cards refresh without resetting upgrade previews or their pile");
            Check(_baseLabel.Text == original && LocManager.Instance.Language == "rus", "P keeps the game language and base text unchanged");
            await Frames(4); Check(TranslationCatalog.English, "held P toggles once; initialization is idempotent");
            await Key(false); await Tap();
            Check(!TranslationCatalog.English && _modLabel.Text == russian, "a second P restores Russian immediately");
            var edit = new LineEdit(); AddChild(edit); edit.GrabFocus(); await Frames();
            await Tap(); Check(!TranslationCatalog.English, "P typed in chat/text fields does not switch language");
            edit.ReleaseFocus(); edit.QueueFree(); await Frames();
            GetTree().Paused = true; await Tap();
            Check(TranslationCatalog.English, "P continues to work while gameplay is paused");
            GetTree().Paused = false; await Tap();
            Check(!TranslationCatalog.English && File.ReadAllText(saved) == "rus", "Russian selection is saved only in local user storage");
            Check(LocManager.Instance.Notifications == 4, "one locale notification per accepted key press");
            // Load the real prior addon after Ruina to test late registration.
            GuZhenRenRussian.Entry.Initialize(); await Frames();
            var gu = new LocString("cards", "GU_ZHEN_REN_CARD_YUE_GUANG_GU.title");
            string guRussian = gu.GetRawText();
            await Tap();
            Check(TranslationCatalog.English && !GuZhenRenRussian.TranslationCatalog.English
                && gu.GetRawText() == guRussian, "P affects Ruina only with the GuZhenRen addon present");
            await Shift(true); await Tap();
            Check(TranslationCatalog.English && GuZhenRenRussian.TranslationCatalog.English
                && gu.GetRawText() == "Moonlight Gu", "Shift+P independently switches the prior GuZhenRen addon");
            await Shift(false); await Tap();
            Check(!TranslationCatalog.English && GuZhenRenRussian.TranslationCatalog.English,
                "Ruina can use Russian while GuZhenRen uses English");
            var hydrology = new GuZhenRen.Relics.ShuiWenGu();
            var relicCard = new MegaCrit.Sts2.Core.Models.CardModel(_card);
            string ruInput = await hydrology.AfterCardPlayed(relicCard);
            await Tap();
            string enInput = await hydrology.AfterCardPlayed(relicCard);
            Check(ruInput == enInput && ruInput.StartsWith("Original:"),
                "GuZhenRen's title-counting relic sees identical unmodified Ruina titles in both languages");
            Check(_card.GetRawText() == "Wisdom", "original text scope never leaks into Ruina display");
            await Tap();
            await Shift(true); await Key(true); await Shift(false); await Frames();
            Check(!TranslationCatalog.English, "releasing Shift during held P does not trigger Ruina");
            await Key(false);
            var otherEdit = new TextEdit(); AddChild(otherEdit); otherEdit.GrabFocus(); await Frames();
            bool guBeforeTyping = GuZhenRenRussian.TranslationCatalog.English;
            await Shift(true); await Tap(); await Shift(false);
            Check(!TranslationCatalog.English && GuZhenRenRussian.TranslationCatalog.English == guBeforeTyping,
                "multiline chat input suppresses both hotkeys");
            otherEdit.ReleaseFocus(); otherEdit.QueueFree();
            Check(new LocString("gameplay_ui", "MELODY.COMPLETE").GetRawText() != "Original: gameplay_ui/MELODY.COMPLETE",
                "Ruina's non-prefixed Melody key is translated");
            Check(new LocString("wrong_table", "MELODY.COMPLETE").GetRawText().StartsWith("Original:"),
                "Melody exception never affects another table");
            GD.Print($"{_checks} RUINA RUSSIAN NATIVE CHECKS PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }
}
