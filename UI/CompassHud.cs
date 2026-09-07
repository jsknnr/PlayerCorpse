using Vintagestory.API.Client;

namespace PlayerCorpse.UI
{
    /// <summary>One line of text under the top of the screen with distance and bearing to the targeted corpse.</summary>
    public class CompassHud : HudElement
    {
        private const string TextKey = "text";

        public CompassHud(ICoreClientAPI capi) : base(capi)
        {
            ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterTop, 0, 90, 700, 30);
            ElementBounds textBounds = ElementBounds.Fixed(0, 0, 700, 30);

            SingleComposer = capi.Gui
                .CreateCompo(Constants.ModId + "-compasshud", dialogBounds)
                .AddDynamicText("", CairoFont.WhiteSmallishText().WithOrientation(EnumTextOrientation.Center), textBounds, TextKey)
                .Compose();
        }

        public override bool Focusable => false;
        public override double DrawOrder => 0.2;

        public void SetText(string text)
        {
            SingleComposer.GetDynamicText(TextKey).SetNewText(text);
        }
    }
}
