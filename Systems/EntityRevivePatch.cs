using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    [HarmonyPatch(typeof(Entity), nameof(Entity.Revive))]
    public static class EntityRevivePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Entity __instance)
        {
            if (__instance is EntityPlayer ep && __instance.Api is ICoreServerAPI sapi)
            {
                sapi.ModLoader.GetModSystem<DeathContentManager>()?.HandleRevive(ep);
            }
        }
    }
}
