using Godot;
using Fixtures;

namespace MegaCrit.Sts2.Core.HoverTips
{
    public enum HoverTipAlignment { None, Left, Right, Center }
}

namespace MegaCrit.Sts2.Core.Nodes.HoverTips
{
    // A native Godot stand-in for the audited game factory. Tests check the
    // original tip objects reach the factory and survive the Window boundary.
    public partial class NHoverTipSet : Control
    {
        public static Node Host = null!;
        public static readonly Dictionary<Control, NHoverTipSet> Active = new();
        public static int Created;
        public static IEnumerable<PreviewTip>? LastTips;
        public static NHoverTipSet? CreateAndShow(Control owner, IEnumerable<PreviewTip> tips, Core.HoverTips.HoverTipAlignment alignment)
        {
            Created++; LastTips = tips;
            var set = new NHoverTipSet();
            Host.AddChild(set); Active.Add(owner, set);
            var text = new VBoxContainer { Name = "textHoverTipContainer", CustomMinimumSize = new Vector2(360, 90), MouseFilter = MouseFilterEnum.Ignore };
            foreach (var tip in tips) text.AddChild(new RichTextLabel { Text = tip.Title + "\n" + tip.Description, FitContent = true, MouseFilter = MouseFilterEnum.Ignore });
            set.AddChild(text);
            set.AddChild(new Control { Name = "cardHoverTipContainer", MouseFilter = MouseFilterEnum.Ignore });
            owner.TreeExiting += () => Remove(owner);
            set.SetAlignment(owner, alignment);
            return set;
        }
        public void SetAlignment(Control owner, Core.HoverTips.HoverTipAlignment alignment)
            => GetNode<Control>("textHoverTipContainer").GlobalPosition = owner.GlobalPosition - new Vector2(360, 0);
        public static void Remove(Control owner)
        {
            if (Active.Remove(owner, out var set)) set.QueueFree();
        }
    }
}
