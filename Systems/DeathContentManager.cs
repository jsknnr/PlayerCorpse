using PlayerCorpse.Entities;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    public class DeathContentManager : ModSystem
    {
        // Entity id of the corpse created by the player's most recent death. Stored in the player
        // entity's server-side Attributes, which are saved with the entity, so it survives
        // disconnects and server restarts. Cleared on respawn or once a revive collected it.
        private const string LastCorpseIdKey = Constants.ModId + ":lastCorpseId";

        private ICoreServerAPI _sapi = null!;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Event.OnEntityDeath += OnEntityDeath;
            api.Event.PlayerRespawn += OnPlayerRespawn;
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            // Player can be null for a player entity whose owner already disconnected.
            if (entity is EntityPlayer { Player: IServerPlayer serverPlayer })
            {
                OnPlayerDeath(serverPlayer);
            }
        }

        private void OnPlayerDeath(IServerPlayer byPlayer)
        {
            EntityPlayer? playerEntity = byPlayer.Entity;
            if (playerEntity == null)
            {
                return;
            }

            bool isKeepContent = playerEntity.Properties?.Server?.Attributes?.GetBool("keepContents") ?? false;
            if (isKeepContent)
            {
                return;
            }

            EntityPlayerCorpse? corpseEntity = null;
            try
            {
                corpseEntity = CreateCorpseEntity(byPlayer);
                if (corpseEntity.Inventory == null || corpseEntity.Inventory.Empty)
                {
                    ModUtil.LogCorpseEvent(_sapi, Mod.Logger,
                        $"Inventory is empty, {corpseEntity.OwnerName}'s corpse not created");
                    return;
                }

                // Disk copy for /returnthings, in case the corpse is lost or looted.
                if (Core.Config.MaxDeathContentSavedPerPlayer > 0)
                {
                    SaveDeathContent(corpseEntity.Inventory, byPlayer);
                }

                if (!Core.Config.CreateCorpse)
                {
                    corpseEntity.Inventory.DropAll(corpseEntity.Pos.XYZ);
                    return;
                }

                // Spawn right away so nothing is held only in memory while the player is dead.
                _sapi.World.SpawnEntity(corpseEntity);
                playerEntity.Attributes.SetLong(LastCorpseIdKey, corpseEntity.EntityId);

                ModUtil.LogCorpseEvent(_sapi, Mod.Logger, string.Format(
                    "Created {0} at {1}, id {2}",
                    corpseEntity.GetName(),
                    ModUtil.RelativeToSpawn(corpseEntity.Pos.XYZ, _sapi),
                    corpseEntity.EntityId));
            }
            catch (Exception ex)
            {
                Mod.Logger.Error(
                    "Corpse creation failed for {0}, falling back to dropping collected items at death location. Exception: {1}",
                    byPlayer.PlayerName, ex);
                HandleCreationFailure(byPlayer, corpseEntity);
            }
        }

        private void HandleCreationFailure(IServerPlayer byPlayer, EntityPlayerCorpse? corpseEntity)
        {
            try
            {
                Vec3d? dropPos = byPlayer.Entity?.Pos?.XYZ;
                if (corpseEntity?.Inventory is { Empty: false } inv && dropPos != null)
                {
                    inv.DropAll(dropPos);
                }
            }
            catch (Exception dropEx)
            {
                Mod.Logger.Error("Fallback drop also failed for {0}: {1}", byPlayer.PlayerName, dropEx);
            }

            try
            {
                byPlayer.SendMessage(
                    GlobalConstants.InfoLogChatGroup,
                    Lang.Get($"{Constants.ModId}:corpse-creation-failed"),
                    EnumChatType.Notification);
            }
            catch
            {
                // chat-send failure must not cascade
            }
        }

        private void OnPlayerRespawn(IServerPlayer byPlayer)
        {
            // The player chose to respawn, so the corpse stays where it is for them to collect.
            byPlayer.Entity?.Attributes.RemoveAttribute(LastCorpseIdKey);
        }

        /// <summary>
        /// Called from <see cref="EntityBehaviorCorpseRevive"/> when a player entity is revived.
        /// </summary>
        public void HandleRevive(EntityPlayer entityPlayer)
        {
            if (entityPlayer.Player is not IServerPlayer byPlayer) return;

            long corpseId = entityPlayer.Attributes.GetLong(LastCorpseIdKey, 0);
            if (corpseId == 0) return;

            // The vanilla respawn path fires PlayerRespawn first and calls Entity.Revive() later, once the
            // player has been teleported. Defer anyway so a respawn that is still in flight can clear the
            // stored id first; a true revive (another player healing the body) still finds it here.
            string playerUid = byPlayer.PlayerUID;
            _sapi.World.RegisterCallback((_) => ReturnCorpseToRevivedPlayer(playerUid, corpseId), 50);
        }

        private void ReturnCorpseToRevivedPlayer(string playerUid, long corpseId)
        {
            var byPlayer = _sapi.World.PlayerByUid(playerUid) as IServerPlayer;
            if (byPlayer?.Entity == null) return;

            // A respawn in the meantime removed the id; leave the corpse in the world.
            if (byPlayer.Entity.Attributes.GetLong(LastCorpseIdKey, 0) != corpseId) return;
            byPlayer.Entity.Attributes.RemoveAttribute(LastCorpseIdKey);

            if (_sapi.World.GetEntityById(corpseId) is not EntityPlayerCorpse corpse ||
                !corpse.Alive ||
                corpse.OwnerUID != playerUid)
            {
                Mod.Logger.Notification("{0} was revived but corpse id {1} is gone or not theirs, nothing returned",
                    byPlayer.PlayerName, corpseId);
                return;
            }

            try
            {
                corpse.Collect(byPlayer);
            }
            catch (Exception ex)
            {
                Mod.Logger.Error("Returning corpse {0} to revived player {1} failed, corpse left in world: {2}",
                    corpseId, byPlayer.PlayerName, ex);
            }
        }

        private EntityPlayerCorpse CreateCorpseEntity(IServerPlayer byPlayer)
        {
            var entityType = _sapi.World.GetEntityType(new AssetLocation(Constants.ModId, "playercorpse"));

            if (_sapi.World.ClassRegistry.CreateEntity(entityType) is not EntityPlayerCorpse corpse)
            {
                throw new Exception("Unable to instantiate player corpse");
            }

            corpse.OwnerUID = byPlayer.PlayerUID;
            corpse.OwnerName = byPlayer.PlayerName;
            corpse.CreationTime = _sapi.World.Calendar.TotalHours;
            corpse.CreationRealDatetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            TakeContentFromPlayer(byPlayer, corpse);

            // Fix dancing corpse issue
            BlockPos floorPos = TryFindFloor(byPlayer.Entity.Pos.AsBlockPos);

            // Attempt to align the corpse to the center of the block so that it does not crawl higher
            Vec3d pos = floorPos.ToVec3d().Add(.5, 0, .5);

            // ToVec3d() encodes the dimension in Y, so the dimension-aware setter is required
            // for deaths outside the default dimension.
            corpse.Pos.SetPosWithDimension(pos);
            corpse.World = _sapi.World;

            return corpse;
        }

        /// <summary> Try to find the nearest block with collision below </summary>
        private BlockPos TryFindFloor(BlockPos pos)
        {
            var floorPos = new BlockPos(pos.dimension);
            for (int i = pos.Y; i > 0; i--)
            {
                floorPos.Set(pos.X, i, pos.Z);
                var block = _sapi.World.BlockAccessor.GetBlock(floorPos);
                if (block.BlockId != 0 && block.CollisionBoxes?.Length > 0)
                {
                    floorPos.Set(pos.X, i + 1, pos.Z);
                    return floorPos;
                }
            }
            return pos;
        }

        private void TakeContentFromPlayer(IServerPlayer byPlayer, EntityPlayerCorpse corpse)
        {
            corpse.Inventory = new InventoryGeneric(
                GetMaxCorpseSlots(byPlayer),
                $"{Constants.ModId}-{byPlayer.PlayerUID}",
                _sapi);

            int lastSlotId = 0;
            foreach (var invClassName in Core.Config.SaveInventoryTypes)
            {
                // Skip armor if it does not drop after death
                var isDropArmorVanilla = byPlayer.Entity.Properties.Server?.Attributes?.GetBool("dropArmorOnDeath") ?? false;
                var isDropArmor = isDropArmorVanilla || Core.Config.DropArmorOnDeath != Config.DropArmorMode.Vanilla;
                if (invClassName == GlobalConstants.characterInvClassName && !isDropArmor)
                {
                    continue;
                }

                // XSkills slots fix
                if (invClassName.Equals(GlobalConstants.backpackInvClassName) &&
                    byPlayer.InventoryManager.GetOwnInventory("xskillshotbar") != null)
                {
                    int i = 0;
                    var backpackInv = byPlayer.InventoryManager.GetOwnInventory(invClassName);
                    foreach (var slot in backpackInv)
                    {
                        if (i > backpackInv.Count - 4) // Extra backpack slots
                        {
                            break;
                        }
                        corpse.Inventory[lastSlotId++].Itemstack = TakeSlotContent(slot);
                    }
                    continue;
                }

                foreach (var slot in byPlayer.InventoryManager.GetOwnInventory(invClassName))
                {
                    corpse.Inventory[lastSlotId++].Itemstack = TakeSlotContent(slot);
                }
            }
        }

        private static int GetMaxCorpseSlots(IServerPlayer byPlayer)
        {
            int maxCorpseSlots = 0;
            foreach (var invClassName in Core.Config.SaveInventoryTypes)
            {
                maxCorpseSlots += byPlayer.InventoryManager.GetOwnInventory(invClassName)?.Count ?? 0;
            }
            return maxCorpseSlots;
        }

        private static ItemStack? TakeSlotContent(ItemSlot slot)
        {
            if (slot.Empty)
            {
                return null;
            }

            // Skip the player's clothing (not armor)
            if (slot.Inventory.ClassName == GlobalConstants.characterInvClassName)
            {
                bool isArmor = slot.Itemstack.ItemAttributes?["protectionModifiers"].Exists ?? false;
                if (!isArmor && Core.Config.DropArmorOnDeath != Config.DropArmorMode.ArmorAndCloth)
                {
                    return null;
                }
            }

            return slot.TakeOutWhole();
        }

        public string GetDeathDataPath(string playerUid)
        {
            string uidFixed = Regex.Replace(playerUid, "[^0-9a-zA-Z]", "");
            string localPath = Path.Combine("ModData", _sapi.World.SavegameIdentifier, Mod.Info.ModID, uidFixed);
            return _sapi.GetOrCreateDataPath(localPath);
        }

        /// <summary>Saved death inventories for the player, newest first.</summary>
        public string[] GetDeathDataFiles(string playerUid)
        {
            string path = GetDeathDataPath(playerUid);
            return Directory
                .GetFiles(path)
                .OrderByDescending(f => new FileInfo(f).Name)
                .ToArray();
        }

        public void SaveDeathContent(InventoryGeneric inventory, IPlayer player)
        {
            string path = GetDeathDataPath(player.PlayerUID);
            string[] files = GetDeathDataFiles(player.PlayerUID);

            for (int i = files.Length - 1; i > Core.Config.MaxDeathContentSavedPerPlayer - 2; i--)
            {
                File.Delete(files[i]);
            }

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);

            // Millisecond stamp plus a counter so rapid successive deaths never overwrite each other.
            string stamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff");
            string file = Path.Combine(path, $"inventory-{stamp}.dat");
            for (int n = 1; File.Exists(file); n++)
            {
                file = Path.Combine(path, $"inventory-{stamp}-{n}.dat");
            }

            File.WriteAllBytes(file, tree.ToBytes());
        }

        public InventoryGeneric LoadDeathContent(string playerUid, string file)
        {
            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(file));

            var inv = new InventoryGeneric(tree.GetInt("qslots"), $"{Constants.ModId}-{playerUid}", _sapi);
            inv.FromTreeAttributes(tree);
            return inv;
        }
    }
}
