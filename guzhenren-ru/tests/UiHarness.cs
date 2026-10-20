using Godot;
using GuZhenRenRussian;
using MegaCrit.Sts2.Core.Localization;
using GuZhenRen.Systems;
using MegaCrit.Sts2.Core.Nodes.Cards;

public partial class UiHarness : Control
{
    private int _checks;
    private Label _modLabel = null!;
    private Label _baseLabel = null!;
    private readonly LocString _card = new("cards", "GU_ZHEN_REN_CARD_YUE_GUANG_GU.title");
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
    private async Task Tap() { await Key(true); await Key(false); }
    private void RefreshText() { _modLabel.Text = _card.GetRawText(); _baseLabel.Text = _baseCard.GetRawText(); }
    public override async void _Ready()
    {
        try
        {
            ProcessMode = ProcessModeEnum.Always;
            string saved = ProjectSettings.GlobalizePath("user://guzhenren-russian-language.txt");
            string? probe = System.Environment.GetEnvironmentVariable("GU_RU_PROBE");
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
            var visibleCard = new NCard { Model = new GuZhenRen.Cards.TestCard(_card) };
            AddChild(visibleCard); visibleCard.UpdateVisuals(Pile.Hand, Preview.Upgrade);
            string russian = _modLabel.Text, original = _baseLabel.Text;
            Check(!TranslationCatalog.English && russian != "Moonlight Gu" && russian.Any(c => c is >= 'А' and <= 'я'), "Russian is the default for this mod");
            Check(_card.GetRawText() == russian, "real Harmony patch overrides the original LocString method");
            Check(_baseLabel.Text == "Original: cards/STRIKE.title", "base game keys retain the original method");
            Check(new LocString("cards", "OTHER_MOD.title").GetRawText() == "Original: cards/OTHER_MOD.title", "other mods keep their own localization");
            Check(new LocString("wrong_table", "GU_ZHEN_REN_CARD_YUE_GUANG_GU.title").GetRawText().StartsWith("Original:"), "lookup respects the localization table");
            Check(GuZhenRenSettingsPage.GetPageTitle() == "Гу Чжэнь Жэнь" && GuZhenRenSettingsPage.GetToggleLabel() == "Режим личинок", "original literal settings strings are translated");
            var hydrology = new GuZhenRen.Relics.ShuiWenGu();
            var card = new MegaCrit.Sts2.Core.Models.CardModel(_card);
            string originalTitle = "Original: cards/GU_ZHEN_REN_CARD_YUE_GUANG_GU.title";
            string ruCountText = await hydrology.AfterCardPlayed(card);
            Check(ruCountText == originalTitle + "|" + originalTitle, "title-length gameplay uses original text before and after await in Russian mode");
            Check(_card.GetRawText() == russian, "original-text scope does not leak into UI between async continuations");
            hydrology.Fail = true;
            try { await hydrology.AfterCardPlayed(card); throw new Exception("Expected failure"); }
            catch (InvalidOperationException) { }
            hydrology.Fail = false;
            Check(_card.GetRawText() == russian, "gameplay exceptions restore the original-text scope");
            Entry.Initialize(); await Key(true);
            Check(TranslationCatalog.English && _modLabel.Text == "Moonlight Gu", "P changes already-visible text to exact original English");
            Check(visibleCard.ShownTitle == "Moonlight Gu" && visibleCard.DisplayingPreview == Preview.Upgrade && visibleCard.DisplayingPile == Pile.Hand, "visible cards refresh without resetting upgrade previews or their pile");
            Check(_baseLabel.Text == original && LocManager.Instance.Language == "rus", "P keeps the game language and base text unchanged");
            Check(GuZhenRenSettingsPage.GetPageTitle() == "Gu Zhen Ren", "settings follow English mode");
            Check(await hydrology.AfterCardPlayed(card) == ruCountText, "different Russian/English choices produce the same relic gameplay input");
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
            GD.Print($"{_checks} GU RUSSIAN NATIVE CHECKS PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }
}
