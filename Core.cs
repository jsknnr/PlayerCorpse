using PlayerCorpse.Entities;
using PlayerCorpse.Items;
using PlayerCorpse.Systems;
using Vintagestory.API.Common;

namespace PlayerCorpse
{
    public class Core : ModSystem
    {
        // The few values clients need are published through the world config, which the server sends to
        // every client on join. Clients never read the config file; they read these keys instead.
        private const string CompassEnabledKey = Constants.ModId + ":CorpseCompassEnabled";
        private const string CollectionTimeKey = Constants.ModId + ":CorpseCollectionTime";
        private const string FreeAfterTimeKey = Constants.ModId + ":FreeCorpseAfterTime";

        /// <summary>Server-side configuration. On the client this holds defaults; use the accessors below instead.</summary>
        public static Config Config { get; private set; } = new();

        public override void StartPre(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            Config = Config.Load(api, Mod.Logger);

            // CorpseCompassEnabled drives the json patches that disable the compass item and recipe.
            api.World.Config.SetBool(CompassEnabledKey, Config.CorpseCompassEnabled);
            api.World.Config.SetFloat(CollectionTimeKey, Config.CorpseCollectionTime);
            api.World.Config.SetInt(FreeAfterTimeKey, Config.FreeCorpseAfterTime);
        }

        public override void Start(ICoreAPI api)
        {
            api.RegisterEntity("EntityPlayerCorpse", typeof(EntityPlayerCorpse));
            api.RegisterItemClass("ItemCorpseCompass", typeof(ItemCorpseCompass));
            api.RegisterEntityBehaviorClass(EntityBehaviorCorpseRevive.Code, typeof(EntityBehaviorCorpseRevive));
        }

        /// <summary>Seconds of holding right-click needed to collect a corpse. Valid on both sides once in a world.</summary>
        public static float CorpseCollectionTime(ICoreAPI api)
        {
            return api.World.Config.GetFloat(CollectionTimeKey, Config.CorpseCollectionTime);
        }

        /// <summary>In-game hours after which a corpse becomes free for all. Valid on both sides once in a world.</summary>
        public static int FreeCorpseAfterTime(ICoreAPI api)
        {
            return api.World.Config.GetInt(FreeAfterTimeKey, Config.FreeCorpseAfterTime);
        }
    }
}
