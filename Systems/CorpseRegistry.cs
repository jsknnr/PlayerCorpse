using PlayerCorpse.Entities;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PlayerCorpse.Systems
{
    [ProtoContract]
    public class CorpseRecord
    {
        [ProtoMember(1)] public long EntityId { get; set; }
        [ProtoMember(2)] public string OwnerUid { get; set; } = "";
        [ProtoMember(3)] public string OwnerName { get; set; } = "";
        [ProtoMember(4)] public double X { get; set; }
        /// <summary>Dimension-local Y (not the encoded InternalY).</summary>
        [ProtoMember(5)] public double Y { get; set; }
        [ProtoMember(6)] public double Z { get; set; }
        [ProtoMember(7)] public int Dimension { get; set; }
        [ProtoMember(8)] public double CreatedTotalHours { get; set; }

        public double SquareDistanceTo(EntityPos pos)
        {
            double dx = X - pos.X, dy = Y - pos.Y, dz = Z - pos.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    [ProtoContract]
    public class CorpseRegistryData
    {
        [ProtoMember(1)] public List<CorpseRecord> Records { get; set; } = [];
    }

    /// <summary>
    /// Server-side list of every live corpse, persisted in the savegame. Lets the compass find corpses
    /// in unloaded chunks. Corpses register themselves on spawn/load and unregister when they die.
    /// </summary>
    public class CorpseRegistry : ModSystem
    {
        private const string SaveKey = Constants.ModId + ":corpses";

        private ICoreServerAPI _sapi = null!;
        private readonly Dictionary<long, CorpseRecord> _records = new();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _sapi = api;
            api.Event.SaveGameLoaded += OnSaveGameLoaded;
            api.Event.GameWorldSave += OnGameWorldSave;
        }

        private void OnSaveGameLoaded()
        {
            _records.Clear();
            var data = _sapi.WorldManager.SaveGame.GetData<CorpseRegistryData>(SaveKey);
            if (data?.Records == null) return;

            foreach (var record in data.Records)
            {
                _records[record.EntityId] = record;
            }
        }

        private void OnGameWorldSave()
        {
            _sapi.WorldManager.SaveGame.StoreData(SaveKey, new CorpseRegistryData { Records = _records.Values.ToList() });
        }

        public void Register(EntityPlayerCorpse corpse)
        {
            if (corpse.EntityId == 0 || string.IsNullOrEmpty(corpse.OwnerUID)) return;

            if (!_records.TryGetValue(corpse.EntityId, out var record))
            {
                record = new CorpseRecord
                {
                    EntityId = corpse.EntityId,
                    OwnerUid = corpse.OwnerUID,
                    OwnerName = corpse.OwnerName,
                    CreatedTotalHours = corpse.CreationTime
                };
                _records[corpse.EntityId] = record;
            }

            UpdatePosition(record, corpse);
        }

        public void Unregister(long entityId)
        {
            _records.Remove(entityId);
        }

        /// <summary>
        /// Live corpses, optionally filtered by owner. Positions of loaded corpses are refreshed and
        /// records whose corpse is gone from a loaded chunk are dropped.
        /// </summary>
        public List<CorpseRecord> GetCorpses(string? ownerUid)
        {
            var result = new List<CorpseRecord>();

            foreach (var record in _records.Values.ToList())
            {
                if (ownerUid != null && record.OwnerUid != ownerUid) continue;

                if (_sapi.World.GetEntityById(record.EntityId) is EntityPlayerCorpse corpse)
                {
                    if (!corpse.Alive)
                    {
                        _records.Remove(record.EntityId);
                        continue;
                    }
                    UpdatePosition(record, corpse);
                }
                else if (IsChunkLoaded(record))
                {
                    // Chunk is loaded but the corpse is not in it: it was removed without telling us.
                    _records.Remove(record.EntityId);
                    continue;
                }

                result.Add(record);
            }

            return result;
        }

        private bool IsChunkLoaded(CorpseRecord record)
        {
            var pos = new BlockPos((int)record.X, (int)record.Y, (int)record.Z, record.Dimension);
            return _sapi.World.BlockAccessor.GetChunkAtBlockPos(pos) != null;
        }

        private static void UpdatePosition(CorpseRecord record, EntityPlayerCorpse corpse)
        {
            record.X = corpse.Pos.X;
            record.Y = corpse.Pos.Y;
            record.Z = corpse.Pos.Z;
            record.Dimension = corpse.Pos.Dimension;
        }
    }
}
