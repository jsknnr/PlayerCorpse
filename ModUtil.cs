using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse
{
    internal static class ModUtil
    {
        /// <summary>Position relative to the world's default spawn, matching the coordinates players see in-game.</summary>
        public static Vec3d RelativeToSpawn(Vec3d pos, ICoreAPI api)
        {
            var spawn = api.World.DefaultSpawnPosition;
            return new Vec3d(pos.X - spawn.X, pos.Y, pos.Z - spawn.Z);
        }

        public static void SendNotification(this IServerPlayer player, string message)
        {
            player.SendMessage(GlobalConstants.CurrentChatGroup, message, EnumChatType.Notification);
        }

        /// <summary>Logs a corpse event and, when DebugMode is on, also broadcasts it in chat (server only).</summary>
        public static void LogCorpseEvent(ICoreAPI api, ILogger logger, string message)
        {
            logger.Notification(message);
            if (Core.Config.DebugMode && api is ICoreServerAPI sapi)
            {
                sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
            }
        }
    }
}
