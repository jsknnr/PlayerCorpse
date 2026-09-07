using PlayerCorpse.Items;
using PlayerCorpse.UI;
using ProtoBuf;
using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    [ProtoContract]
    public class CompassTargetPacket
    {
        [ProtoMember(1)] public bool Found { get; set; }
        [ProtoMember(2)] public double X { get; set; }
        [ProtoMember(3)] public double Y { get; set; }
        [ProtoMember(4)] public double Z { get; set; }
        [ProtoMember(5)] public int Dimension { get; set; }
        [ProtoMember(6)] public string OwnerName { get; set; } = "";
        [ProtoMember(7)] public int Index { get; set; }
        [ProtoMember(8)] public int Count { get; set; }
        [ProtoMember(9)] public long CorpseId { get; set; }
    }

    public enum CompassSearchMode
    {
        /// <summary>Snap to the nearest corpse.</summary>
        Nearest,
        /// <summary>Advance to the next corpse by distance.</summary>
        Cycle,
        /// <summary>Keep the current corpse if it still exists, else nearest. Used by the off-hand refresh.</summary>
        Refresh
    }

    /// <summary>Client-side view of the corpse the compass currently points at.</summary>
    public sealed class CompassTarget
    {
        public double X, Y, Z;
        public int Dimension;
        public string OwnerName = "";
        public int Index;
        public int Count;
        public long CorpseId;

        /// <summary>World position in the client's dimension-encoded Y convention (like Entity.Pos.XYZ).</summary>
        public Vec3d InternalPos => new(X, Y + Dimension * 32768.0, Z);
    }

    /// <summary>
    /// Corpse compass logic. Server side: resolves targets from the <see cref="CorpseRegistry"/> and sends
    /// them to the client. Client side: keeps the current target, drives the HUD line and the particles.
    /// </summary>
    public class CorpseCompassSystem : ModSystem
    {
        private const string ChannelName = Constants.ModId + ":compass";
        private const string TargetAttribute = "targetCorpseId";
        private const long OffHandParticleIntervalMs = 250;

        private IServerNetworkChannel? _serverChannel;
        private CorpseRegistry? _registry;

        private ICoreClientAPI? _capi;
        private CompassHud? _hud;
        private long _lastParticleMs;
        private readonly SimpleParticleProperties _particles = CreateParticleProperties();

        /// <summary>Client only. Null when the compass has nothing to point at.</summary>
        public CompassTarget? Target { get; private set; }

        public override void Start(ICoreAPI api)
        {
            api.Network.RegisterChannel(ChannelName).RegisterMessageType<CompassTargetPacket>();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            _serverChannel = api.Network.GetChannel(ChannelName);
            _registry = api.ModLoader.GetModSystem<CorpseRegistry>();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            _capi = api;
            api.Network.GetChannel(ChannelName).SetMessageHandler<CompassTargetPacket>(OnTargetReceived);
            _hud = new CompassHud(api);
            api.Event.RegisterGameTickListener(OnClientTick, 100);
        }

        public override void Dispose()
        {
            _hud?.Dispose();
            _hud = null;
            base.Dispose();
        }

        // ---------------------------------------------------------------- server

        public void Search(IServerPlayer player, ItemSlot slot, CompassSearchMode mode, bool explicitRequest)
        {
            if (_registry == null || _serverChannel == null || slot.Itemstack == null || player.Entity == null) return;

            bool creative = player.WorldData.CurrentGameMode == EnumGameMode.Creative;
            EntityPos playerPos = player.Entity.Pos;

            var candidates = _registry.GetCorpses(creative ? null : player.PlayerUID)
                .Where(r => r.Dimension == playerPos.Dimension)
                .OrderBy(r => r.SquareDistanceTo(playerPos))
                .ToList();

            if (candidates.Count == 0)
            {
                slot.Itemstack.Attributes.RemoveAttribute(TargetAttribute);
                slot.MarkDirty();
                _serverChannel.SendPacket(new CompassTargetPacket { Found = false }, player);

                if (explicitRequest)
                {
                    player.SendNotification(Lang.Get($"{Constants.ModId}:corpsecompass-corpses-not-found"));
                }
                return;
            }

            long currentId = slot.Itemstack.Attributes.GetLong(TargetAttribute, 0);
            int index = candidates.FindIndex(r => r.EntityId == currentId);

            switch (mode)
            {
                case CompassSearchMode.Nearest:
                    index = 0;
                    break;
                case CompassSearchMode.Cycle:
                    index = (index + 1) % candidates.Count;
                    break;
                case CompassSearchMode.Refresh:
                    if (index < 0) index = 0;
                    break;
            }

            CorpseRecord target = candidates[index];
            slot.Itemstack.Attributes.SetLong(TargetAttribute, target.EntityId);
            slot.MarkDirty();

            _serverChannel.SendPacket(new CompassTargetPacket
            {
                Found = true,
                X = target.X,
                Y = target.Y,
                Z = target.Z,
                Dimension = target.Dimension,
                OwnerName = target.OwnerName,
                Index = index,
                Count = candidates.Count,
                CorpseId = target.EntityId
            }, player);

            if (explicitRequest)
            {
                string text = string.Format("{0}'s corpse found at {1} for {2} (id {3})",
                    target.OwnerName,
                    ModUtil.RelativeToSpawn(new Vec3d(target.X, target.Y, target.Z), player.Entity.Api),
                    player.PlayerName,
                    target.EntityId);
                Mod.Logger.Notification(text);
                if (Core.Config.DebugMode)
                {
                    player.SendNotification(text);
                }
            }
        }

        // ---------------------------------------------------------------- client

        private void OnTargetReceived(CompassTargetPacket packet)
        {
            if (!packet.Found)
            {
                Target = null;
                _hud?.TryClose();
                return;
            }

            Target = new CompassTarget
            {
                X = packet.X,
                Y = packet.Y,
                Z = packet.Z,
                Dimension = packet.Dimension,
                OwnerName = packet.OwnerName,
                Index = packet.Index,
                Count = packet.Count,
                CorpseId = packet.CorpseId
            };

            // Fresh result: show it right away instead of waiting for the next tick.
            EmitParticles();
            UpdateHud(holdingCompass: true, inOffHand: false);
        }

        private void OnClientTick(float dt)
        {
            if (_capi?.World?.Player?.Entity == null) return;

            EntityPlayer player = _capi.World.Player.Entity;
            bool inMainHand = player.RightHandItemSlot?.Itemstack?.Collectible is ItemCorpseCompass;
            bool inOffHand = player.LeftHandItemSlot?.Itemstack?.Collectible is ItemCorpseCompass;

            UpdateHud(inMainHand || inOffHand, inOffHand);

            if (inOffHand && HasTargetInCurrentDimension() &&
                _capi.World.ElapsedMilliseconds - _lastParticleMs > OffHandParticleIntervalMs)
            {
                EmitParticles();
            }
        }

        private void UpdateHud(bool holdingCompass, bool inOffHand)
        {
            if (_hud == null || _capi == null) return;

            if (!holdingCompass || !HasTargetInCurrentDimension())
            {
                if (_hud.IsOpened()) _hud.TryClose();
                return;
            }

            _hud.SetText(BuildHudText(_capi.World.Player.Entity, Target!));
            if (!_hud.IsOpened()) _hud.TryOpen();
        }

        public bool HasTargetInCurrentDimension()
        {
            return Target != null && _capi?.World?.Player?.Entity != null &&
                   Target.Dimension == _capi.World.Player.Entity.Pos.Dimension;
        }

        /// <summary>Compass bearing from the player to the target in degrees, 0 = north (-Z), 90 = east (+X).</summary>
        public static double BearingDeg(EntityPos from, CompassTarget target)
        {
            double dx = target.X - from.X;
            double dz = target.Z - from.Z;
            return (Math.Atan2(dx, -dz) * GameMath.RAD2DEG + 360.0) % 360.0;
        }

        private static string BuildHudText(EntityPlayer player, CompassTarget target)
        {
            EntityPos pos = player.Pos;
            double dx = target.X - pos.X;
            double dy = target.Y - pos.Y;
            double dz = target.Z - pos.Z;
            int distance = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz));

            string[] dirs = ["n", "ne", "e", "se", "s", "sw", "w", "nw"];
            int dirIndex = (int)Math.Round(BearingDeg(pos, target) / 45.0) % 8;
            string direction = Lang.Get($"{Constants.ModId}:dir-{dirs[dirIndex]}");

            int vertical = (int)Math.Round(dy);
            string height = vertical > 1 ? Lang.Get($"{Constants.ModId}:compass-hud-above", vertical)
                : vertical < -1 ? Lang.Get($"{Constants.ModId}:compass-hud-below", -vertical)
                : Lang.Get($"{Constants.ModId}:compass-hud-level");

            string text = Lang.Get($"{Constants.ModId}:compass-hud", target.OwnerName, distance, direction, height);
            if (target.Count > 1)
            {
                text += " " + Lang.Get($"{Constants.ModId}:compass-hud-multi", target.Index + 1, target.Count);
            }
            return text;
        }

        private void EmitParticles()
        {
            if (_capi == null || Target == null) return;
            EntityPlayer player = _capi.World.Player.Entity;
            if (player == null) return;

            _lastParticleMs = _capi.World.ElapsedMilliseconds;

            Vec3d targetPos = Target.InternalPos.Add(0.5, 0, 0.5);
            Vec3d startPos = player.Pos.AheadCopy(1).XYZ.Add(0, player.LocalEyePos.Y, 0);
            Vec3d relativePos = targetPos - startPos;

            _particles.MinVelocity = relativePos.ToVec3f() / (_particles.LifeLength * 3);
            _particles.MinPos = startPos;
            _particles.AddPos = _particles.MinVelocity.ToVec3d() * 0.1;
            _particles.MinSize = GameMath.Clamp(_particles.MinVelocity.Length() * 0.01f, 0.05f, 3f);
            _particles.MaxSize = _particles.MinSize * 2;
            _particles.Color = GetRandomColor(_capi.World.Rand);

            _capi.World.SpawnParticles(_particles);
        }

        private static SimpleParticleProperties CreateParticleProperties() => new()
        {
            MinPos = Vec3d.Zero,
            AddPos = new Vec3d(.2, .2, .2),
            MinVelocity = Vec3f.Zero,
            AddVelocity = Vec3f.Zero,
            RandomVelocityChange = true,
            Bounciness = 0.1f,
            GravityEffect = 0,
            WindAffected = false,
            WithTerrainCollision = true,
            MinSize = 0.3f,
            MaxSize = 0.8f,
            MinQuantity = 1,
            AddQuantity = 5,
            LifeLength = 1f,
            VertexFlags = 100 & VertexFlags.GlowLevelBitMask,
            ParticleModel = EnumParticleModel.Quad
        };

        private static int GetRandomColor(Random rand)
        {
            return ColorUtil.ToRgba(255, rand.Next(200, 256), rand.Next(100, 156), rand.Next(0, 56));
        }
    }
}
