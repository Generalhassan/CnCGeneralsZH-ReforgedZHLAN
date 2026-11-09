// Zero Hour LAN Launcher - data files: pack.json (on the stick), settings.json and state.json (on this PC).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ZHLauncher
{
    [DataContract]
    public class PresetDef
    {
        [DataMember(Name = "name", Order = 1)] public string Name;
        [DataMember(Name = "description", Order = 2, EmitDefaultValue = false)] public string Description;
        // mod folder (or .big file) names under mods\, in load order: later wins
        [DataMember(Name = "mods", Order = 3)] public List<string> Mods = new List<string>();
    }

    [DataContract]
    public class PackDef
    {
        [DataMember(Name = "name", Order = 1)] public string Name = "LAN pack";
        [DataMember(Name = "version", Order = 2)] public string Version = "v3.6.0";
        [DataMember(Name = "build", Order = 3)] public string Build = "build/Reforged-v3.6.0.zip";
        // extra command line for everyone, e.g. "-win"
        [DataMember(Name = "extraArgs", Order = 4, EmitDefaultValue = false)] public string ExtraArgs;
        // mods to load with -modpatch even without a mod.json saying "patch": true
        [DataMember(Name = "patches", Order = 5)] public List<string> Patches = new List<string>();
        [DataMember(Name = "presets", Order = 6)] public List<PresetDef> Presets = new List<PresetDef>();

        [OnDeserialized]
        void Fix(StreamingContext c)
        {
            if (Patches == null) Patches = new List<string>();
            if (Presets == null) Presets = new List<PresetDef>();
            foreach (var p in Presets) if (p.Mods == null) p.Mods = new List<string>();
        }
    }

    [DataContract]
    public class ModJson
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "patch")] public bool Patch;
    }

    public class ModInfo
    {
        public string Name;       // folder or file name in mods\, what presets refer to
        public string Label;      // from mod.json when there is one
        public string PackPath;   // where it is in the pack
        public bool IsFile;       // a single .big
        public bool IsPatch;
        public override string ToString() { return Label + (IsPatch ? "  (patch)" : ""); }
    }

    [DataContract]
    public class Settings
    {
        [DataMember(Name = "zeroHourPath")] public string ZeroHourPath;
        [DataMember(Name = "generalsPath")] public string GeneralsPath;
        [DataMember(Name = "extraArgs")] public string ExtraArgs;
        // "classic" (the game's default) or "reforged": Reforged's mouse controls, -mouse
        [DataMember(Name = "mouse")] public string Mouse;
        // true: leave the pack's add-ons (addons\*.big, e.g. HD textures) off this PC
        [DataMember(Name = "noAddons")] public bool NoAddons;
        [DataMember(Name = "lastPreset")] public string LastPreset;
        [DataMember(Name = "customMods")] public List<string> CustomMods = new List<string>();
    }

    [DataContract]
    public class HashEntry
    {
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "time")] public long Time;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    [DataContract]
    public class State
    {
        [DataMember(Name = "baseFrom")] public string BaseFrom;
        [DataMember(Name = "buildSha256")] public string BuildSha256;
        [DataMember(Name = "buildVersion")] public string BuildVersion;
        // file path -> hash, so a mod is hashed once and not on every start
        [DataMember(Name = "hashes")] public Dictionary<string, HashEntry> Hashes = new Dictionary<string, HashEntry>();
        // add-on files the launcher put into the game folder, so it can take them out again
        [DataMember(Name = "addonsInGame")] public List<string> AddonsInGame = new List<string>();

        [OnDeserialized]
        void Fix(StreamingContext c)
        {
            if (Hashes == null) Hashes = new Dictionary<string, HashEntry>(StringComparer.OrdinalIgnoreCase);
            if (AddonsInGame == null) AddonsInGame = new List<string>();
        }
    }

    public static class Json
    {
        static DataContractJsonSerializerSettings Opts()
        {
            return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
        }

        public static T Load<T>(string path) where T : class, new()
        {
            if (!File.Exists(path)) return new T();
            var bytes = File.ReadAllBytes(path);
            // tolerate a BOM from Notepad
            int skip = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
            using (var ms = new MemoryStream(bytes, skip, bytes.Length - skip))
            {
                var s = new DataContractJsonSerializer(typeof(T), Opts());
                return (T)s.ReadObject(ms) ?? new T();
            }
        }

        public static void Save<T>(string path, T value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string text;
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, new UTF8Encoding(false), false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(T), Opts()).WriteObject(w, value);
                }
                text = new UTF8Encoding(false).GetString(ms.ToArray()).Replace("\\/", "/");
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
