using Godot;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Localization;

namespace Ruina2.Ruina2Code.Cards
{
    public class TestCard(LocString title) : MegaCrit.Sts2.Core.Models.CardModel(title) { }
}

namespace MegaCrit.Sts2.Core.Nodes.Cards
{
    public enum Preview { Normal, Upgrade }
    public enum Pile { Draw, Hand }
    public partial class NCard : Control
    {
        public MegaCrit.Sts2.Core.Models.CardModel? Model { get; set; }
        public Pile DisplayingPile { get; private set; }
        public Preview DisplayingPreview;
        public string ShownTitle = "";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void UpdateVisuals(Pile pile, Preview mode)
        {
            DisplayingPile = pile; DisplayingPreview = mode;
            ShownTitle = Model?.Title ?? "";
        }
    }
}
