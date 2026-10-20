using Godot;

// A fixture for the game's custom [purple]-style effects. Duplicate(0) must
// keep these resources even though the label's gameplay script is omitted.
public partial class PreviewColorEffect : RichTextEffect
{
    public string bbcode = "purple";
    public override bool _ProcessCustomFX(CharFXTransform transform)
    {
        transform.Color = new Color(1, 0.48f, 1, 1);
        return true;
    }
}
