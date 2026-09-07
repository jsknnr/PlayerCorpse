using PlayerCorpse.Entities;
using PlayerCorpse.Systems;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse.Items
{
    /// <summary>
    /// Points at the holder's corpses. Right-click targets the nearest one, shift right-click cycles through
    /// them, and in the off-hand it keeps refreshing. Targets come from the server's corpse registry, so
    /// distance and chunk loading do not matter. The needle in the model turns toward the target when held.
    /// </summary>
    public class ItemCorpseCompass : Item
    {
        public static long SearchCooldownMs => 5000;
        public static long CycleCooldownMs => 500;
        public static long OffHandRefreshMs => 10000;

        // Model needle. The arrow sits in the "C Pillar" element group; verified in-game, at rest its tip
        // points along +Z (south when the model's -Z faces forward). Adjust these two if the needle is off.
        private const string NeedleElementName = "C Pillar";
        private const float NeedleRestBearingDeg = 180f;
        private const int NeedleStepDeg = 10;

        private CorpseCompassSystem? _compass;
        private ICoreClientAPI? _capi;
        private Vintagestory.API.Common.Shape? _needleShape;
        private readonly Dictionary<int, MultiTextureMeshRef> _needleMeshes = new();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            _compass = api.ModLoader.GetModSystem<CorpseCompassSystem>();
            _capi = api as ICoreClientAPI;
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            foreach (var mesh in _needleMeshes.Values)
            {
                mesh.Dispose();
            }
            _needleMeshes.Clear();
            base.OnUnloaded(api);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);

            if (handling != EnumHandHandling.NotHandled || slot.Itemstack == null)
            {
                return;
            }

            // Clicking a corpse with the compass in hand must collect the corpse, not search.
            if (entitySel?.Entity is EntityPlayerCorpse)
            {
                return;
            }

            handling = EnumHandHandling.PreventDefault;

            // One search per click, decided by the server.
            if (!firstEvent || api.Side != EnumAppSide.Server || byEntity is not EntityPlayer { Player: IServerPlayer serverPlayer })
            {
                return;
            }

            bool cycle = byEntity.Controls.Sneak;
            long cooldown = cycle ? CycleCooldownMs : SearchCooldownMs;
            long now = api.World.ElapsedMilliseconds;
            long last = slot.Itemstack.TempAttributes.GetLong("lastCorpseSearch", 0);
            if (now - last < cooldown)
            {
                return;
            }
            slot.Itemstack.TempAttributes.SetLong("lastCorpseSearch", now);

            _compass?.Search(serverPlayer, slot, cycle ? CompassSearchMode.Cycle : CompassSearchMode.Nearest, explicitRequest: true);
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);

            // Off-hand: silently keep the target fresh. The client shows the HUD and particles on its own.
            if (api.Side != EnumAppSide.Server || slot.Itemstack == null || byEntity.LeftHandItemSlot != slot ||
                byEntity is not EntityPlayer { Player: IServerPlayer serverPlayer })
            {
                return;
            }

            long now = api.World.ElapsedMilliseconds;
            long last = slot.Itemstack.TempAttributes.GetLong("lastCorpseSearch", 0);
            if (now - last < OffHandRefreshMs)
            {
                return;
            }
            slot.Itemstack.TempAttributes.SetLong("lastCorpseSearch", now);

            _compass?.Search(serverPlayer, slot, CompassSearchMode.Refresh, explicitRequest: false);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get($"{Constants.ModId}:corpsecompass-usage"));
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            if (target is not (EnumItemRenderTarget.HandTp or EnumItemRenderTarget.HandTpOff))
            {
                return;
            }

            if (_compass?.Target == null || !_compass.HasTargetInCurrentDimension())
            {
                return;
            }

            EntityPlayer player = capi.World.Player.Entity;
            double bearing = CorpseCompassSystem.BearingDeg(player.Pos, _compass.Target);
            // Player facing bearing is -yaw (yaw 0 faces -Z, positive yaw turns toward -X).
            double relative = bearing + player.Pos.Yaw * GameMath.RAD2DEG;

            MultiTextureMeshRef? mesh = GetNeedleMesh(relative);
            if (mesh != null)
            {
                renderinfo.ModelRef = mesh;
            }
        }

        /// <summary>Cached mesh with the needle turned toward the given bearing relative to the holder.</summary>
        private MultiTextureMeshRef? GetNeedleMesh(double relativeBearingDeg)
        {
            if (_capi == null) return null;

            int step = ((int)Math.Round(relativeBearingDeg / NeedleStepDeg) * NeedleStepDeg % 360 + 360) % 360;
            if (_needleMeshes.TryGetValue(step, out var cached))
            {
                return cached;
            }

            _needleShape ??= Vintagestory.API.Common.Shape.TryGet(_capi, Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
            if (_needleShape == null)
            {
                return null;
            }

            Vintagestory.API.Common.Shape shape = _needleShape.Clone();
            ShapeElement? needle = shape.GetElementByName(NeedleElementName);
            if (needle == null)
            {
                _capi.Logger.Warning("Corpse compass shape has no '{0}' element, needle will not turn", NeedleElementName);
                return null;
            }

            // Shape rotation is counter-clockwise from above, bearings are clockwise.
            needle.RotationOrigin = [0, 0, 0];
            needle.RotationY = NeedleRestBearingDeg - step;

            _capi.Tesselator.TesselateShape(this, shape, out MeshData meshData);
            MultiTextureMeshRef mesh = _capi.Render.UploadMultiTextureMesh(meshData);
            _needleMeshes[step] = mesh;
            return mesh;
        }
    }
}
