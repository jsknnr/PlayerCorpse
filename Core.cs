using CommonLib.Config;
using PlayerCorpse.Entities;
using PlayerCorpse.Items;
using Vintagestory.API.Common;

namespace PlayerCorpse
{
    public class Core : ModSystem
    {
        public static Config Config { get; private set; } = null!;

        public override void Start(ICoreAPI api)
        {
            var configs = api.ModLoader.GetModSystem<ConfigManager>();
            Config = configs.GetConfig<Config>();

            api.World.Config.SetBool($"{Mod.Info.ModID}:CorpseCompassEnabled", Config.CorpseCompassEnabled);

            api.RegisterEntity("EntityPlayerCorpse", typeof(EntityPlayerCorpse));
            api.RegisterItemClass("ItemCorpseCompass", typeof(ItemCorpseCompass));
        }

    }
}
