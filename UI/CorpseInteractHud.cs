using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PlayerCorpse.UI
{
    /// <summary>
    /// Owns the single client-side progress ring shown while collecting a corpse. Corpse entities
    /// share this one renderer instead of each registering (and leaking) their own.
    /// </summary>
    public class CorpseInteractHud : ModSystem
    {
        public HudCircleRenderer? Renderer { get; private set; }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            Renderer = new HudCircleRenderer(api, new HudCircleSettings
            {
                Color = 0xFF9500
            });
        }

        public override void Dispose()
        {
            Renderer?.Dispose();
            Renderer = null;
            base.Dispose();
        }
    }
}
