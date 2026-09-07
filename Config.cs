using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace PlayerCorpse
{
    /// <summary>
    /// Server-side mod configuration, stored as flat JSON in ModConfig/playercorpseforked.json.
    /// Each option is documented in README.md.
    /// </summary>
    public class Config
    {
        public const string FileName = "playercorpseforked.json";

        /// <summary>Corpse burns in fire/lava.</summary>
        public bool CanFired { get; set; } = false;

        /// <summary>Corpse has 100 hp and can be broken by another player.</summary>
        public bool HasHealth { get; set; } = false;

        /// <summary>If false, items are dropped at the death location instead of creating a corpse.</summary>
        public bool CreateCorpse { get; set; } = true;

        /// <summary>Player inventory class names whose contents go into the corpse.</summary>
        public string[] SaveInventoryTypes { get; set; } = DefaultSaveInventoryTypes();

        /// <summary>Privilege required to use /returnthings.</summary>
        public string NeedPrivilegeForReturnThings { get; set; } = Privilege.gamemode;

        /// <summary>How many death inventories are kept on disk per player for /returnthings (0 disables).</summary>
        public int MaxDeathContentSavedPerPlayer { get; set; } = 10;

        /// <summary>Also broadcast corpse events to chat.</summary>
        public bool DebugMode { get; set; } = false;

        /// <summary>Makes corpses available to everyone after N in-game hours (0 - always, below zero - never).</summary>
        public int FreeCorpseAfterTime { get; set; } = 240;

        /// <summary>Seconds the owner must hold right-click to collect a corpse.</summary>
        public float CorpseCollectionTime { get; set; } = 1;

        /// <summary>If false, the compass item and recipe are disabled; existing compasses become unknown items.</summary>
        public bool CorpseCompassEnabled { get; set; } = true;

        /// <summary>Override the vanilla keep-inventory rules so armor (and optionally clothes) end up in the corpse.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public DropArmorMode DropArmorOnDeath { get; set; } = DropArmorMode.Vanilla;

        public enum DropArmorMode { Vanilla, Armor, ArmorAndCloth }

        private static string[] DefaultSaveInventoryTypes() =>
        [
            GlobalConstants.hotBarInvClassName,
            GlobalConstants.backpackInvClassName,
            GlobalConstants.craftingInvClassName,
            GlobalConstants.mousecursorInvClassName,
            GlobalConstants.characterInvClassName
        ];

        /// <summary>
        /// Loads the config from ModConfig, converting the old CommonLib layout if found, validates it and
        /// writes it back so new options show up in the file. A file that cannot be parsed is left untouched
        /// and defaults are used for this session.
        /// </summary>
        public static Config Load(ICoreAPI api, ILogger logger)
        {
            string path = Path.Combine(GamePaths.ModConfig, FileName);
            Config config;

            try
            {
                config = ReadFile(path, logger);
            }
            catch (Exception e)
            {
                logger.Error("Could not read config {0}, using defaults for this session. Fix or delete the file. Error: {1}", path, e);
                config = new Config();
                config.Validate(logger);
                return config;
            }

            config.Validate(logger);
            api.StoreModConfig(config, FileName);
            return config;
        }

        private static Config ReadFile(string path, ILogger logger)
        {
            if (!File.Exists(path))
            {
                logger.Notification("Config {0} not found, creating it with defaults", FileName);
                return new Config();
            }

            var root = JObject.Parse(File.ReadAllText(path));
            if (UnwrapLegacyFormat(root))
            {
                string backup = path + ".bak";
                File.Copy(path, backup, overwrite: true);
                logger.Notification("Config {0} used the old CommonLib layout and was converted. Backup written to {1}", FileName, backup);
            }

            return root.ToObject<Config>() ?? new Config();
        }

        /// <summary>
        /// CommonLib stored every option as {"Description": .., "Default": .., "Value": ..} plus a "Version" entry.
        /// Collapse such entries to their Value so the file can be read as a plain object.
        /// </summary>
        private static bool UnwrapLegacyFormat(JObject root)
        {
            bool legacy = false;
            foreach (JProperty prop in root.Properties().ToList())
            {
                if (prop.Value is JObject entry && entry.ContainsKey("Default") && entry.TryGetValue("Value", out JToken? value))
                {
                    prop.Value = value.DeepClone();
                    legacy = true;
                }
            }

            if (legacy)
            {
                root.Remove("Version");
            }

            return legacy;
        }

        private void Validate(ILogger logger)
        {
            if (SaveInventoryTypes == null || SaveInventoryTypes.Length == 0)
            {
                logger.Warning("Config: SaveInventoryTypes is empty, using defaults");
                SaveInventoryTypes = DefaultSaveInventoryTypes();
            }

            if (MaxDeathContentSavedPerPlayer < 0)
            {
                logger.Warning("Config: MaxDeathContentSavedPerPlayer {0} is below 0, set to 0", MaxDeathContentSavedPerPlayer);
                MaxDeathContentSavedPerPlayer = 0;
            }

            if (CorpseCollectionTime < 0)
            {
                logger.Warning("Config: CorpseCollectionTime {0} is below 0, set to 0", CorpseCollectionTime);
                CorpseCollectionTime = 0;
            }

            if (string.IsNullOrWhiteSpace(NeedPrivilegeForReturnThings))
            {
                logger.Warning("Config: NeedPrivilegeForReturnThings is empty, set to {0}", Privilege.gamemode);
                NeedPrivilegeForReturnThings = Privilege.gamemode;
            }
            else if (!Privilege.AllCodes().Contains(NeedPrivilegeForReturnThings))
            {
                // Mods can register custom privileges, so only warn.
                logger.Warning("Config: NeedPrivilegeForReturnThings '{0}' is not a built-in privilege ({1})",
                    NeedPrivilegeForReturnThings, string.Join(", ", Privilege.AllCodes()));
            }
        }
    }
}
