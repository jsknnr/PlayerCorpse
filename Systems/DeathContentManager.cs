using CommonLib.Extensions;
using CommonLib.Utils;
using HarmonyLib;
using PlayerCorpse.Entities;
using System;
using System.Collections.Generic;
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
        private ICoreServerAPI _sapi = null!;
        private readonly Dictionary<string, EntityPlayerCorpse> _pendingCorpses = new();
        private Harmony? _harmony;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Event.OnEntityDeath += OnEntityDeath;
            api.Event.PlayerRespawn += OnPlayerRespawn;

            _harmony = new Harmony(Constants.ModId);
            _harmony.PatchAll(typeof(EntityRevivePatch).Assembly);
        }

        public override void Dispose()
        {
            _harmony?.UnpatchAll(Constants.ModId);
            base.Dispose();
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (entity is EntityPlayer entityPlayer)
            {
                OnPlayerDeath((IServerPlayer)entityPlayer.Player);
            }
        }

        private void OnPlayerDeath(IServerPlayer byPlayer)
        {
            bool isKeepContent = byPlayer.Entity?.Properties?.Server?.Attributes?.GetBool("keepContents") ?? false;
            if (isKeepContent)
            {
                return;
            }

            EntityPlayerCorpse? corpseEntity = null;
            try
            {
                corpseEntity = CreateCorpseEntity(byPlayer);
                if (corpseEntity.Inventory != null && !corpseEntity.Inventory.Empty)
                {
                    // Save content for /returnthings (disk fallback if the server crashes
                    // before the player respawns or is revived)
                    if (Core.Config.MaxDeathContentSavedPerPlayer > 0)
                    {
                        SaveDeathContent(corpseEntity.Inventory, byPlayer);
                    }

                    // Hold the corpse until the player either respawns (spawn it)
                    // or is revived (return items). See OnPlayerRespawn / HandleRevive.
                    _pendingCorpses[byPlayer.PlayerUID] = corpseEntity;
                }
                else
                {
                    string message = $"Inventory is empty, {corpseEntity.OwnerName}'s corpse not created";
                    Mod.Logger.Notification(message);
                    if (Core.Config.DebugMode)
                    {
                        _sapi.BroadcastMessage(message);
                    }
                }
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
            if (!_pendingCorpses.Remove(byPlayer.PlayerUID, out var corpseEntity)) return;

            try
            {
                if (Core.Config.CreateCorpse)
                {
                    _sapi.World.SpawnEntity(corpseEntity);

                    string message = string.Format(
                        "Created {0} at {1}, id {2}",
                        corpseEntity.GetName(),
                        corpseEntity.Pos.XYZ.RelativePos(_sapi),
                        corpseEntity.EntityId);

                    Mod.Logger.Notification(message);
                    if (Core.Config.DebugMode)
                    {
                        _sapi.BroadcastMessage(message);
                    }
                }
                else
                {
                    corpseEntity.Inventory?.DropAll(corpseEntity.Pos.XYZ);
                }
            }
            catch (Exception ex)
            {
                Mod.Logger.Error("Deferred corpse spawn failed for {0}: {1}", byPlayer.PlayerName, ex);
                try { corpseEntity.Inventory?.DropAll(corpseEntity.Pos.XYZ); } catch { }
            }
        }

        public void HandleRevive(EntityPlayer entityPlayer)
        {
            if (entityPlayer.Player is not IServerPlayer byPlayer) return;
            if (!_pendingCorpses.ContainsKey(byPlayer.PlayerUID)) return;

            // Respawn also invokes Entity.Revive(); PlayerRespawn fires immediately after.
            // Defer so the respawn path (if any) can consume the pending entry first.
            // If it's a true revive, the entry will still be there when the callback runs.
            string playerUid = byPlayer.PlayerUID;
            _sapi.World.RegisterCallback((_) => DoHandleRevive(playerUid), 50);
        }

        private void DoHandleRevive(string playerUid)
        {
            if (!_pendingCorpses.Remove(playerUid, out var corpseEntity)) return;
            if (corpseEntity.Inventory == null) return;

            var byPlayer = _sapi.World.PlayerByUid(playerUid) as IServerPlayer;
            if (byPlayer?.Entity == null)
            {
                // Player disconnected between revive and the deferred handler — drop at corpse pos as fallback.
                try { corpseEntity.Inventory.DropAll(corpseEntity.Pos.XYZ); } catch { }
                return;
            }

            Vec3d dropPos = byPlayer.Entity.Pos.XYZ;
            try
            {
                foreach (var slot in corpseEntity.Inventory)
                {
                    if (slot.Empty) continue;

                    var dummy = new DummySlot(slot.Itemstack);
                    var op = new ItemStackMoveOperation(
                        byPlayer.Entity.World,
                        EnumMouseButton.Left,
                        0,
                        EnumMergePriority.AutoMerge,
                        slot.StackSize);

                    byPlayer.InventoryManager.TryTransferAway(dummy, ref op, onlyPlayerInventory: true, slotNotifyEffect: false);

                    if (dummy.StackSize > 0)
                    {
                        byPlayer.Entity.World.SpawnItemEntity(dummy.Itemstack, dropPos);
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Logger.Error("Revive item-return failed for {0}: {1}", byPlayer.PlayerName, ex);
                try { corpseEntity.Inventory.DropAll(dropPos); } catch { }
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

            corpse.Pos.SetPos(pos);
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

        public string GetDeathDataPath(IPlayer player)
        {
            ICoreAPI api = player.Entity.Api;
            string uidFixed = Regex.Replace(player.PlayerUID, "[^0-9a-zA-Z]", "");
            string localPath = Path.Combine("ModData", api.GetWorldId(), Mod.Info.ModID, uidFixed);
            return api.GetOrCreateDataPath(localPath);
        }

        public string[] GetDeathDataFiles(IPlayer player)
        {
            string path = GetDeathDataPath(player);
            return Directory
                .GetFiles(path)
                .OrderByDescending(f => new FileInfo(f).Name)
                .ToArray();
        }

        public void SaveDeathContent(InventoryGeneric inventory, IPlayer player)
        {
            string path = GetDeathDataPath(player);
            string[] files = GetDeathDataFiles(player);

            for (int i = files.Length - 1; i > Core.Config.MaxDeathContentSavedPerPlayer - 2; i--)
            {
                File.Delete(files[i]);
            }

            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);

            string name = $"inventory-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.dat";
            File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
        }

        public InventoryGeneric LoadLastDeathContent(IPlayer player, int offset = 0)
        {
            if (Core.Config.MaxDeathContentSavedPerPlayer <= offset)
            {
                throw new IndexOutOfRangeException("offset is too large or save data disabled");
            }

            string file = GetDeathDataFiles(player).ElementAt(offset);

            var tree = new TreeAttribute();
            tree.FromBytes(File.ReadAllBytes(file));

            var inv = new InventoryGeneric(tree.GetInt("qslots"), $"{Constants.ModId}-{player.PlayerUID}", player.Entity.Api);
            inv.FromTreeAttributes(tree);
            return inv;
        }
    }
}
