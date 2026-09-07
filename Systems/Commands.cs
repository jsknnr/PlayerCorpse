using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    public class Commands : ModSystem
    {
        private ICoreServerAPI _sapi = null!;
        private DeathContentManager _deathContentManager = null!;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            _deathContentManager = api.ModLoader.GetModSystem<DeathContentManager>();

            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands
                .Create("returnthings")
                .RequiresPrivilege(Core.Config.NeedPrivilegeForReturnThings)
                .WithDescription("Returns things lost at the last death")
                .BeginSubCommand("list")
                    .WithArgs(parsers.Word("player"))
                    .HandleWith(ShowDeathList)
                .EndSubCommand()
                .BeginSubCommand("get")
                    .WithArgs(
                        parsers.Word("player"),
                        parsers.OnlinePlayer("give to player"),
                        parsers.OptionalInt("id", 0))
                    .HandleWith(ReturnThings)
                .EndSubCommand();
        }

        /// <summary>
        /// Resolves a player name to a UID. Death data is stored by UID, so the player does not
        /// have to be online, only known to the server.
        /// </summary>
        private string? ResolvePlayerUid(string playerName)
        {
            IPlayer? online = _sapi.World.AllOnlinePlayers.FirstOrDefault(p => p.PlayerName == playerName);
            if (online != null)
            {
                return online.PlayerUID;
            }

            return _sapi.PlayerData.GetPlayerDataByLastKnownName(playerName)?.PlayerUID;
        }

        private TextCommandResult ShowDeathList(TextCommandCallingArgs args)
        {
            string playerName = (string)args[0];
            string? playerUid = ResolvePlayerUid(playerName);
            if (playerUid == null)
            {
                return TextCommandResult.Error(Lang.Get("No such player"));
            }

            string[] files = _deathContentManager.GetDeathDataFiles(playerUid);
            if (files.Length == 0)
            {
                return TextCommandResult.Error(Lang.Get("No data saved"));
            }

            var sb = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                sb.AppendLine($"{i}. {Path.GetFileName(files[i])}");
            }
            return TextCommandResult.Success(sb.ToString());
        }

        private TextCommandResult ReturnThings(TextCommandCallingArgs args)
        {
            string playerName = (string)args[0];
            IPlayer giveToPlayer = (IPlayer)args[1];
            int id = (int)args[2];

            string? playerUid = ResolvePlayerUid(playerName);
            if (playerUid == null)
            {
                return TextCommandResult.Error(Lang.Get("No such player"));
            }

            if (giveToPlayer.Entity == null)
            {
                return TextCommandResult.Error(Lang.Get(
                    "Player {0} is offline or not fully loaded.",
                    giveToPlayer.PlayerName));
            }

            string[] files = _deathContentManager.GetDeathDataFiles(playerUid);
            if (id < 0 || files.Length <= id)
            {
                return TextCommandResult.Error(Lang.Get("Index {0} not found", id));
            }

            InventoryGeneric inventory = _deathContentManager.LoadDeathContent(playerUid, files[id]);
            Vec3d dropPos = giveToPlayer.Entity.Pos.XYZ.AddCopy(0, 1, 0);

            foreach (var slot in inventory)
            {
                if (slot.Empty)
                {
                    continue;
                }

                // TryGiveItemstack reports success even when only part of the stack fit,
                // so always check what is left over and drop that on the ground.
                ItemStack stack = slot.Itemstack;
                giveToPlayer.InventoryManager.TryGiveItemstack(stack);
                if (stack.StackSize > 0)
                {
                    _sapi.World.SpawnItemEntity(stack, dropPos);
                }
                slot.Itemstack = null;
                slot.MarkDirty();
            }

            // Remove the file once items returned to prevent duping.
            try { File.Delete(files[id]); } catch { /* already-gone is fine */ }

            return TextCommandResult.Success(Lang.Get(
                "Returned things from {0} to {1} with index {2}",
                playerName, giveToPlayer.PlayerName, id));
        }
    }
}
