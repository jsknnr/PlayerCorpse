using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    /// <summary>
    /// Patched onto the player entity (see patches/player-corpse-revive.json). The game calls
    /// <see cref="OnEntityRevive"/> from Entity.Revive(), which covers both a vanilla respawn and
    /// being revived by another player; <see cref="DeathContentManager"/> tells the two apart.
    /// </summary>
    public class EntityBehaviorCorpseRevive : EntityBehavior
    {
        public const string Code = "playercorpserevive";

        public EntityBehaviorCorpseRevive(Entity entity) : base(entity)
        {
        }

        public override void OnEntityRevive()
        {
            base.OnEntityRevive();

            if (entity is EntityPlayer entityPlayer && entity.Api is ICoreServerAPI sapi)
            {
                sapi.ModLoader.GetModSystem<DeathContentManager>()?.HandleRevive(entityPlayer);
            }
        }

        public override string PropertyName() => Code;
    }
}
