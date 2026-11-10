// Zero Hour LAN Launcher - everything that touches files: finding Zero Hour, the clean copy,
// installing the Reforged build, copying mods from the pack, the LAN code and starting the game.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZHLauncher
{
    public class Progress
    {
        public string Text;
        public double Fraction = -1;   // -1: no bar, just text
        public Progress(string text, double fraction) { Text = text; Fraction = fraction; }
    }

    public class Engine
    {
        public readonly string PackDir;
        public readonly string DataRoot;
        public PackDef Pack;
        public List<ModInfo> Mods = new List<ModInfo>();
        public Settings Settings;
        public State State;

        public Action<Progress> Report = p => { };

        public string BaseDir { get { return Path.Combine(DataRoot, "base"); } }
        public string GameDir { get { return Path.Combine(DataRoot, "game-" + SafeName(Pack.Version)); } }
        public string LocalModsDir { get { return Path.Combine(DataRoot, "mods"); } }
        public string LocalAddonsDir { get { return Path.Combine(DataRoot, "addons"); } }
        public string PackAddonsDir { get { return Path.Combine(PackDir, "addons"); } }
        public string PackFile { get { return Path.Combine(PackDir, "pack.json"); } }
        string SettingsFile { get { return Path.Combine(DataRoot, "settings.json"); } }
        string StateFile { get { return Path.Combine(DataRoot, "state.json"); } }
        string LogFile { get { return Path.Combine(DataRoot, "launcher.log"); } }

        public Engine(string packDir, string dataRoot)
        {
            PackDir = Path.GetFullPath(packDir);
            DataRoot = Path.GetFullPath(dataRoot);
            Directory.CreateDirectory(DataRoot);
            Settings = SafeLoad<Settings>(SettingsFile);
            if (Settings.CustomMods == null) Settings.CustomMods = new List<string>();
            State = SafeLoad<State>(StateFile);
            if (State.Hashes == null) State.Hashes = new Dictionary<string, HashEntry>(StringComparer.OrdinalIgnoreCase);
            else State.Hashes = new Dictionary<string, HashEntry>(State.Hashes, StringComparer.OrdinalIgnoreCase);
            ReloadPack();
        }

        T SafeLoad<T>(string path) where T : class, new()
        {
            try { return Json.Load<T>(path); }
            catch (Exception e) { Log("could not read " + path + ": " + e.Message); return new T(); }
        }

        public void SaveSettings() { Json.Save(SettingsFile, Settings); }
        public void SaveState() { Json.Save(StateFile, State); }
        public void SavePack() { Json.Save(PackFile, Pack); }

        public void Log(string line)
        {
            try { File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine); }
            catch { }
        }

        static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ------------------------------------------------------------------ where the launcher keeps its files

        public static string DefaultDataRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZHLauncher"); }
        }

        // The folder picked under Settings, Game files. It is written to a one-line file in the
        // default folder, because settings.json itself lives in the picked one.
        static string PointerFile { get { return Path.Combine(DefaultDataRoot, "datafolder.txt"); } }

        public static string ChosenDataRoot()
        {
            try
            {
                if (File.Exists(PointerFile))
                {
                    var p = File.ReadAllText(PointerFile).Trim();
                    if (p.Length > 0 && Directory.Exists(p)) return p;
                }
            }
            catch { }
            return DefaultDataRoot;
        }

        static string Norm(string dir) { return Path.GetFullPath(dir).TrimEnd('\\', '/'); }
        static bool SamePath(string a, string b) { return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase); }
        static bool Inside(string dir, string root) { return Norm(dir).StartsWith(Norm(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }

        static void SetChosenDataRoot(string dir)
        {
            Directory.CreateDirectory(DefaultDataRoot);
            if (SamePath(dir, DefaultDataRoot)) { if (File.Exists(PointerFile)) File.Delete(PointerFile); }
            else File.WriteAllText(PointerFile, Norm(dir));
        }

        static readonly string[] OwnFiles = { "settings.json", "state.json", "launcher.log" };
        static bool IsOwnDir(string name)
        {
            return name.Equals("base", StringComparison.OrdinalIgnoreCase) || name.Equals("mods", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("addons", StringComparison.OrdinalIgnoreCase) || name.StartsWith("game-", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsLauncherFolder(string dir)
        {
            return File.Exists(Path.Combine(dir, "settings.json")) || Directory.Exists(Path.Combine(dir, "base"));
        }

        // for the tests: behave as if the new folder were on another drive
        public static bool TestOtherDrive;

        static bool SameVolume(string a, string b)
        {
            if (TestOtherDrive) return false;
            return string.Equals(Path.GetPathRoot(Norm(a)), Path.GetPathRoot(Norm(b)), StringComparison.OrdinalIgnoreCase);
        }

        static long SizeOf(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { return 0; }
        }

        public enum MoveKind { Same, Move, UseExisting, Refused }

        public MoveKind CheckMoveTarget(string target, out string message)
        {
            if (SamePath(target, DataRoot)) { message = "That is already the launcher's folder."; return MoveKind.Same; }
            if (Inside(target, DataRoot)) { message = "Pick a folder outside the current one."; return MoveKind.Refused; }
            if (SamePath(target, PackDir) || Inside(target, PackDir))
            { message = "Pick a folder outside the LAN pack: the pack is what you copy to the other PCs."; return MoveKind.Refused; }
            if (Directory.Exists(target) && IsLauncherFolder(target))
            {
                message = "This folder already holds the launcher's files (from an earlier move or another copy). " +
                          "Switch to them? What is in " + DataRoot + " stays where it is.";
                return MoveKind.UseExisting;
            }
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            { message = "That folder has other files in it. Pick an empty folder, or make a new one with Make New Folder."; return MoveKind.Refused; }

            long bytes = SizeOf(BaseDir) + SizeOf(LocalModsDir) + SizeOf(LocalAddonsDir);
            string fs = null;
            try { fs = new DriveInfo(Path.GetPathRoot(Norm(target))).DriveFormat; } catch { }
            if (bytes == 0) message = "Nothing is copied on this PC yet, so the launcher simply uses the new folder from now on.";
            else if (SameVolume(DataRoot, target)) message = "Everything moves there (" + Gb(bytes) + ", the same drive, so it only takes a moment).";
            else message = Gb(bytes) + " are copied there and then removed here. The game folder is set up again from them on the next Play.";
            if (fs != null && !fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                message += "\n\nThat drive is " + fs + ", which has no file links: the game folder will take another copy of Zero Hour's size there.";
            return MoveKind.Move;
        }

        // Moves base, mods, add-ons, the game folder and the launcher's own files to target and makes it
        // the launcher's folder. The launcher has to start again afterwards.
        public void MoveData(string target)
        {
            if (Process.GetProcessesByName("generals").Length > 0) throw new Exception("Close Zero Hour first: its files are in use.");
            target = Norm(target);
            var here = Norm(DataRoot);
            SaveSettings();
            SaveState();
            Directory.CreateDirectory(target);
            var dirs = Directory.GetDirectories(here).Where(d => IsOwnDir(Path.GetFileName(d))).ToList();
            bool rebuildGame = false;

            if (SameVolume(here, target))
            {
                foreach (var d in dirs)
                {
                    Report(new Progress("Moving " + Path.GetFileName(d) + "...", -1));
                    Directory.Move(d, Path.Combine(target, Path.GetFileName(d)));   // a rename: links stay links
                }
                foreach (var f in OwnFiles) { var p = Path.Combine(here, f); if (File.Exists(p)) File.Move(p, Path.Combine(target, f)); }
            }
            else
            {
                // the game folder is mostly links into base: copied, it would take Zero Hour's size twice
                var jobs = dirs.Where(d => !Path.GetFileName(d).StartsWith("game-", StringComparison.OrdinalIgnoreCase))
                               .Select(d => new KeyValuePair<string, string>(d, Path.Combine(target, Path.GetFileName(d)))).ToList();
                try
                {
                    CopyTrees(jobs, "Moving the launcher's files");
                    foreach (var f in OwnFiles) { var p = Path.Combine(here, f); if (File.Exists(p)) File.Copy(p, Path.Combine(target, f), true); }
                }
                catch
                {
                    foreach (var j in jobs) if (Directory.Exists(j.Value)) ForceDelete(j.Value);
                    foreach (var f in OwnFiles) { var p = Path.Combine(target, f); if (File.Exists(p)) File.Delete(p); }
                    throw;
                }
                foreach (var d in dirs) { Report(new Progress("Removing the old copy...", -1)); ForceDelete(d); }
                foreach (var f in OwnFiles) { var p = Path.Combine(here, f); if (File.Exists(p)) File.Delete(p); }
                rebuildGame = true;
            }

            // remembered hashes are filed under full paths
            var hashes = new Dictionary<string, HashEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in State.Hashes)
            {
                var k = kv.Key;
                if (k.StartsWith(here + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) k = target + k.Substring(here.Length);
                hashes[k] = kv.Value;
            }
            State.Hashes = hashes;
            if (rebuildGame) { State.BuildSha256 = null; State.AddonsInGame = new List<string>(); }
            Json.Save(Path.Combine(target, "state.json"), State);
            SetChosenDataRoot(target);
            try { File.AppendAllText(Path.Combine(target, "launcher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  moved here from " + here + Environment.NewLine); } catch { }
        }

        public void UseExistingData(string target)
        {
            SaveSettings();
            SaveState();
            SetChosenDataRoot(target);
        }

        // ------------------------------------------------------------------ the pack

        public void ReloadPack()
        {
            Pack = File.Exists(PackFile) ? Json.Load<PackDef>(PackFile) : new PackDef();
            if (string.IsNullOrWhiteSpace(Pack.Version)) Pack.Version = "v3.6.0";
            if (string.IsNullOrWhiteSpace(Pack.Build) || !File.Exists(PackPath(Pack.Build)))
            {
                // no build named, or the name is wrong: take the zip in build\ if there is one
                var dir = Path.Combine(PackDir, "build");
                var zip = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.zip").OrderBy(f => f).LastOrDefault() : null;
                if (zip != null) Pack.Build = "build/" + Path.GetFileName(zip);
            }
            Mods = DiscoverMods();
        }

        public string PackPath(string rel)
        {
            return Path.Combine(PackDir, rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        }

        List<ModInfo> DiscoverMods()
        {
            var list = new List<ModInfo>();
            var dir = Path.Combine(PackDir, "mods");
            if (!Directory.Exists(dir)) return list;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var m = new ModInfo { Name = Path.GetFileName(d), PackPath = d, IsFile = false };
                m.Label = m.Name;
                var mj = Path.Combine(d, "mod.json");
                if (File.Exists(mj))
                {
                    try
                    {
                        var j = Json.Load<ModJson>(mj);
                        if (!string.IsNullOrWhiteSpace(j.Name)) m.Label = (j.Name + " " + (j.Version ?? "")).Trim();
                        m.IsPatch = j.Patch;
                        if (j.Exclude != null) m.Exclude = j.Exclude.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    }
                    catch (Exception e) { Log("mod.json in " + d + ": " + e.Message); }
                }
                list.Add(m);
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                // a single archive is a mod too, whatever it is called: .big, or .gib, .ctr and the like
                if (!f.EndsWith(".big", StringComparison.OrdinalIgnoreCase) && !IsBigArchive(f)) continue;
                var name = Path.GetFileName(f);
                list.Add(new ModInfo { Name = name, Label = Path.GetFileNameWithoutExtension(f), PackPath = f, IsFile = true });
            }
            foreach (var m in list)
                if (Pack.Patches.Any(p => string.Equals(p, m.Name, StringComparison.OrdinalIgnoreCase))) m.IsPatch = true;
            return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public ModInfo FindMod(string name)
        {
            return Mods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public List<ModInfo> Resolve(IEnumerable<string> names, out List<string> missing)
        {
            var found = new List<ModInfo>();
            missing = new List<string>();
            foreach (var n in names)
            {
                var m = FindMod(n);
                if (m == null) missing.Add(n); else found.Add(m);
            }
            return found;
        }

        // ------------------------------------------------------------------ finding Zero Hour

        static readonly string[] GeneralsSiblings = {
            "Command & Conquer Generals", "Command & Conquer(tm) Generals", "Command and Conquer Generals",
        };

        static string Reg(RegistryKey root, string path, params string[] values)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return null;
                    foreach (var v in values)
                    {
                        var s = k.GetValue(v) as string;
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim().Trim('"');
                    }
                }
            }
            catch { }
            return null;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            var a = Reg(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (a != null) roots.Add(a.Replace('/', '\\'));
            var b = Reg(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (b != null) roots.Add(b);
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            var libs = new List<string>();
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                libs.Add(r);
                var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (!File.Exists(vdf)) continue;
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
            }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static bool HoldsZeroHour(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "INIZH.big"));
        }

        public static bool HoldsGenerals(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Textures.big")) && !HoldsZeroHour(dir);
        }

        public static string FindZeroHour()
        {
            var candidates = new List<string>();
            foreach (var lib in SteamLibraries())
            {
                var common = Path.Combine(lib, "steamapps", "common");
                try { if (Directory.Exists(common)) candidates.AddRange(Directory.GetDirectories(common)); } catch { }
            }
            foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                                          @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                                          @"SOFTWARE\WOW6432Node\Electronic Arts\Command and Conquer Generals Zero Hour" })
            {
                var p = Reg(Registry.LocalMachine, hive, "InstallPath", "Install Dir");
                if (p != null) candidates.Add(p);
            }
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                foreach (var sub in new[] { "EA Games", "Origin Games", "Command & Conquer The First Decade",
                                            @"EA Games\Command & Conquer The First Decade" })
                {
                    var d = Path.Combine(pf, sub);
                    try { if (Directory.Exists(d)) { candidates.Add(d); candidates.AddRange(Directory.GetDirectories(d)); } } catch { }
                }
            }
            foreach (var c in candidates)
            {
                try
                {
                    var dir = File.Exists(c) ? Path.GetDirectoryName(c) : c;   // a path saved as ...\generals.exe
                    if (HoldsZeroHour(dir)) return dir;
                    if (Directory.Exists(dir))
                        foreach (var s in Directory.GetDirectories(dir)) if (HoldsZeroHour(s)) return s;
                }
                catch { }
            }
            return null;
        }

        public static string FindGenerals(string zh)
        {
            if (string.IsNullOrEmpty(zh)) return null;
            var inside = Path.Combine(zh, "ZH_Generals");
            if (File.Exists(Path.Combine(inside, "Textures.big"))) return inside;
            var parent = Path.GetDirectoryName(zh.TrimEnd('\\', '/'));
            if (parent != null)
            {
                foreach (var s in GeneralsSiblings) if (HoldsGenerals(Path.Combine(parent, s))) return Path.Combine(parent, s);
                try { foreach (var d in Directory.GetDirectories(parent)) if (HoldsGenerals(d)) return d; } catch { }
            }
            foreach (var hive in new[] { @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Generals",
                                          @"SOFTWARE\Electronic Arts\EA Games\Generals" })
            {
                var p = Reg(Registry.LocalMachine, hive, "InstallPath", "Install Dir");
                if (p == null) continue;
                if (HoldsGenerals(p)) return p;
                var td = Path.Combine(p, "Command & Conquer(tm) Generals");
                if (HoldsGenerals(td)) return td;
            }
            return null;
        }

        public static bool Is64BitExe(string exe)
        {
            try
            {
                using (var fs = File.OpenRead(exe))
                using (var br = new BinaryReader(fs))
                {
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int pe = br.ReadInt32();
                    fs.Seek(pe + 4, SeekOrigin.Begin);
                    ushort machine = br.ReadUInt16();
                    return machine == 0x8664 || machine == 0xAA64;
                }
            }
            catch { return false; }
        }

        // The official launcher installs Reforged into the game folder itself. Copied from there,
        // its newer files would sit under our v3.6.0 and differ from PC to PC.
        public static bool HasReforgedInside(string zh)
        {
            if (Is64BitExe(Path.Combine(zh, "generals.exe"))) return true;
            try { return Directory.GetFiles(zh, "avcodec*.dll").Length > 0; } catch { return false; }
        }

        // ------------------------------------------------------------------ the clean copy

        public bool BaseReady { get { return HoldsZeroHour(BaseDir) && File.Exists(Path.Combine(BaseDir, "ZH_Generals", "Textures.big")); } }
        public bool GameReady { get { return File.Exists(Path.Combine(GameDir, "generals.exe")) && State.BuildVersion == Pack.Version && State.BuildSha256 != null; } }

        public void SetupBase(string zh, string generals)
        {
            Log("setup base from " + zh + " / " + generals);
            if (Directory.Exists(BaseDir)) { Report(new Progress("Removing the old copy...", -1)); ForceDelete(BaseDir); }
            if (Directory.Exists(GameDir)) ForceDelete(GameDir);   // it was linked to the old copy
            State.BuildSha256 = null;
            SaveState();

            var jobs = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(zh, BaseDir) };
            bool generalsInside = generals != null &&
                Path.GetFullPath(generals).TrimEnd('\\', '/').StartsWith(Path.GetFullPath(zh).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (generals != null && !generalsInside)
                jobs.Add(new KeyValuePair<string, string>(generals, Path.Combine(BaseDir, "ZH_Generals")));
            CopyTrees(jobs, "Copying Zero Hour");

            if (!BaseReady) throw new Exception("The copy has no Generals files (Textures.big in ZH_Generals). Pick the Generals folder in Settings.");
            State.BaseFrom = zh;
            SaveState();
        }

        void CopyTrees(List<KeyValuePair<string, string>> jobs, string what)
        {
            var files = new List<Tuple<string, string, long>>();
            foreach (var j in jobs)
            {
                var src = Path.GetFullPath(j.Key).TrimEnd('\\', '/');
                foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    files.Add(Tuple.Create(f, Path.Combine(j.Value, f.Substring(src.Length + 1)), new FileInfo(f).Length));
            }
            long total = Math.Max(1, files.Sum(f => f.Item3)), done = 0;
            foreach (var f in files)
            {
                CopyFile(f.Item1, f.Item2, b => Report(new Progress(what + "  " + Gb(done + b) + " of " + Gb(total), (double)(done + b) / total)));
                done += f.Item3;
            }
        }

        static string Gb(long b)
        {
            return b >= 1073741824L ? (b / 1073741824.0).ToString("0.00") + " GB" : Math.Max(1, (long)Math.Round(b / 1048576.0)) + " MB";
        }

        static void CopyFile(string src, string dst, Action<long> onBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst)) { File.SetAttributes(dst, FileAttributes.Normal); File.Delete(dst); }   // never write through a hard link
            var buf = new byte[4 << 20];
            long n = 0, last = 0;
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
            using (var o = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                int r;
                while ((r = i.Read(buf, 0, buf.Length)) > 0)
                {
                    o.Write(buf, 0, r);
                    n += r;
                    if (n - last > (16 << 20)) { last = n; if (onBytes != null) onBytes(n); }
                }
            }
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            if (onBytes != null) onBytes(n);
        }

        static void ForceDelete(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            Directory.Delete(dir, true);
        }

        // ------------------------------------------------------------------ the Reforged build

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        static bool hardLinksWork = true;

        static void LinkOrCopy(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (hardLinksWork)
            {
                try { if (CreateHardLink(dst, src, IntPtr.Zero)) return; }
                catch (DllNotFoundException) { hardLinksWork = false; }
                catch (EntryPointNotFoundException) { hardLinksWork = false; }
            }
            CopyFile(src, dst, null);
        }

        public void InstallBuild()
        {
            var zip = PackPath(Pack.Build ?? "");
            if (!File.Exists(zip)) throw new Exception("The pack has no Reforged build. Put Reforged-" + Pack.Version + ".zip in the pack's build folder.");
            Report(new Progress("Checking the Reforged build...", -1));
            var sha = HashFile(zip, "Checking the Reforged build");
            if (GameReady && State.BuildSha256 == sha) return;

            Log("installing build " + zip + " " + sha);
            if (Directory.Exists(GameDir)) { Report(new Progress("Removing the old game folder...", -1)); ForceDelete(GameDir); }
            // a game folder of another version (the pack moved from v3.6.0 to v3.6.0-lan, say) is not used again
            foreach (var old in Directory.GetDirectories(DataRoot, "game-*"))
                if (!SamePath(old, GameDir)) { Report(new Progress("Removing " + Path.GetFileName(old) + "...", -1)); ForceDelete(old); }

            var src = Path.GetFullPath(BaseDir).TrimEnd('\\', '/');
            var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 50 == 0) Report(new Progress("Linking Zero Hour files  " + i + " of " + files.Length, (double)i / files.Length * 0.5));
                LinkOrCopy(files[i], Path.Combine(GameDir, files[i].Substring(src.Length + 1)));
            }

            using (var archive = ZipFile.OpenRead(zip))
            {
                // the exe may sit in a folder inside the zip: everything is taken from that folder
                var exe = archive.Entries.Where(e => e.Name.Equals("generals.exe", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(e => e.FullName.Length).FirstOrDefault();
                if (exe == null) throw new Exception("The build zip has no generals.exe in it.");
                var prefix = exe.FullName.Substring(0, exe.FullName.Length - exe.Name.Length);
                var root = Path.GetFullPath(GameDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var entries = archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (i % 20 == 0) Report(new Progress("Installing Reforged " + Pack.Version + "  " + i + " of " + entries.Count, 0.5 + (double)i / entries.Count * 0.5));
                    var rel = e.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var target = Path.GetFullPath(Path.Combine(root, rel));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // a name climbing out of the folder
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target)) { File.SetAttributes(target, FileAttributes.Normal); File.Delete(target); }
                    e.ExtractToFile(target);
                }
            }
            if (!Is64BitExe(Path.Combine(GameDir, "generals.exe")))
                throw new Exception("generals.exe from the build zip is not a 64-bit Reforged exe.");
            State.BuildSha256 = sha;
            State.BuildVersion = Pack.Version;
            SaveState();
        }

        // ------------------------------------------------------------------ add-ons

        // Archives in the pack's addons folder go next to generals.exe, the place the game mounts
        // every .big from. ReforgedTextures.big (Reforged's HD textures) is the one this is for: the
        // game puts its Reforged*.big over the mods' files only where a mod has no copy of its own,
        // and Classic Graphics in the game's options leaves them out. They do not touch a match, so a
        // PC without them still plays with everyone and they are not part of the LAN code.
        public List<string> PackAddons()
        {
            if (!Directory.Exists(PackAddonsDir)) return new List<string>();
            // Reforged*.big only: the names the game treats as Reforged's art, so nothing in here can
            // shadow one of Zero Hour's own archives
            return Directory.GetFiles(PackAddonsDir, "Reforged*.big").Select(f => Path.GetFileName(f))
                            .Where(n => n.StartsWith("Reforged", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(BaseDir, n)))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void SyncAddons()
        {
            var wanted = Settings.NoAddons ? new List<string>() : PackAddons();

            // this PC's copy of the add-ons, so a new build does not copy them off the stick again
            Directory.CreateDirectory(LocalAddonsDir);
            var copies = wanted.Select(n => Tuple.Create(Path.Combine(PackAddonsDir, n), Path.Combine(LocalAddonsDir, n)))
                               .Where(c => Changed(c.Item1, c.Item2))
                               .Select(c => Tuple.Create(c.Item1, c.Item2, new FileInfo(c.Item1).Length)).ToList();
            long total = Math.Max(1, copies.Sum(c => c.Item3)), done = 0;
            foreach (var c in copies)
            {
                CopyFile(c.Item1, c.Item2, b => Report(new Progress("Copying add-ons  " + Gb(done + b) + " of " + Gb(total), (double)(done + b) / total)));
                done += c.Item3;
            }
            if (copies.Count > 0) Log("copied add-ons: " + string.Join(", ", copies.Select(c => Path.GetFileName(c.Item2))));
            foreach (var f in Directory.GetFiles(LocalAddonsDir))
                if (!wanted.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }

            // the game folder: linked to that copy, and whatever is no longer wanted taken out
            foreach (var old in State.AddonsInGame.Where(o => !wanted.Contains(o, StringComparer.OrdinalIgnoreCase)))
            {
                var target = Path.Combine(GameDir, old);
                if (File.Exists(target) && !File.Exists(Path.Combine(BaseDir, old))) { File.SetAttributes(target, FileAttributes.Normal); File.Delete(target); }
            }
            foreach (var n in wanted)
            {
                var local = Path.Combine(LocalAddonsDir, n);
                var target = Path.Combine(GameDir, n);
                if (!Changed(local, target)) continue;
                if (File.Exists(target)) { File.SetAttributes(target, FileAttributes.Normal); File.Delete(target); }
                LinkOrCopy(local, target);
            }
            State.AddonsInGame = wanted;
            SaveState();
        }

        // ------------------------------------------------------------------ mods

        // a single-archive mod is kept as .big on this PC, whatever the pack calls it
        string LocalModName(ModInfo m) { return m.IsFile ? Path.ChangeExtension(m.Name, ".big") : m.Name; }
        public string LocalModPath(ModInfo m) { return Path.Combine(LocalModsDir, LocalModName(m)); }

        // Mods arrive the way their authors or GenLauncher keep them: archives called .gib (ShockWave) or
        // .ctr (Contra) so the game does not load them by itself, the mod's own launcher and exe next to
        // them. 3.6.0's -mod reads *.big only, so the copy on this PC names every archive .big, found by
        // its BIGF/BIG4 header the way GenLauncher does, and leaves programs out (Contra's generals.ctr
        // is an exe). mod.json's "exclude" drops what else the pack does not want.
        static readonly HashSet<string> SkipExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".exe", ".dll", ".ico", ".enb", ".lnk", ".url", ".bat", ".cmd", ".ps1", ".vbs", ".msi", ".scr", ".asi" };

        static byte[] Head(string file)
        {
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var b = new byte[4];
                    int n = fs.Read(b, 0, 4);
                    if (n < 4) Array.Resize(ref b, Math.Max(0, n));
                    return b;
                }
            }
            catch { return new byte[0]; }
        }

        public static bool IsBigArchive(string file)
        {
            var h = Head(file);
            return h.Length == 4 && h[0] == 'B' && h[1] == 'I' && h[2] == 'G' && (h[3] == 'F' || h[3] == '4');
        }

        static bool IsProgram(string file)
        {
            var h = Head(file);
            return h.Length >= 2 && h[0] == 'M' && h[1] == 'Z';
        }

        static bool WildMatch(string text, string pattern)
        {
            var rx = "^" + Regex.Escape(pattern.Replace('/', '\\')).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(text.Replace('/', '\\'), rx, RegexOptions.IgnoreCase);
        }

        // GenLauncher keeps a mod as <mod>\<version>\files: a folder that holds nothing but one other
        // folder is looked through, so "ShockWave\1.201 GenLauncher Fix 1" works copied either way
        static readonly HashSet<string> GameFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Data", "Art", "Window", "Maps", "Movies", "Audio", "Music", "Speech" };

        public static string ContentRoot(string modDir)
        {
            var dir = modDir;
            for (int depth = 0; depth < 3; depth++)
            {
                var files = Directory.GetFiles(dir).Where(f => !Path.GetFileName(f).Equals("mod.json", StringComparison.OrdinalIgnoreCase)
                                                          && !Path.GetFileName(f).StartsWith("readme", StringComparison.OrdinalIgnoreCase)).ToList();
                var subs = Directory.GetDirectories(dir);
                if (files.Count == 0 && subs.Length == 1 && !GameFolders.Contains(Path.GetFileName(subs[0]))) dir = subs[0];
                else break;
            }
            return dir;
        }

        // pack file -> its relative path in this PC's copy, or null to leave it out
        static string LocalRel(string file, string rel, ModInfo m)
        {
            var name = Path.GetFileName(rel);
            if (name.Equals("mod.json", StringComparison.OrdinalIgnoreCase)) return rel;
            if (m.Exclude.Any(p => WildMatch(rel, p) || WildMatch(name, p))) return null;
            if (SkipExtensions.Contains(Path.GetExtension(name))) return null;
            if (IsProgram(file)) return null;
            if (!name.EndsWith(".big", StringComparison.OrdinalIgnoreCase) && IsBigArchive(file)) return Path.ChangeExtension(rel, ".big");
            return rel;
        }

        // What the copy of a folder mod on this PC is made of: (pack file, local file) pairs.
        public List<KeyValuePair<string, string>> ModFiles(ModInfo m)
        {
            var list = new List<KeyValuePair<string, string>>();
            var local = LocalModPath(m);
            var outer = Path.GetFullPath(m.PackPath).TrimEnd('\\', '/');
            var root = Path.GetFullPath(ContentRoot(outer)).TrimEnd('\\', '/');
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // a real .big goes first, so "x.big" beats a "x.gib" next to it
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                                 .OrderBy(f => f.EndsWith(".big", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var rel = LocalRel(f, f.Substring(root.Length + 1), m);
                if (rel == null || !taken.Add(rel)) continue;
                list.Add(new KeyValuePair<string, string>(f, Path.Combine(local, rel)));
            }
            var mj = Path.Combine(outer, "mod.json");
            if (root != outer && File.Exists(mj) && taken.Add("mod.json")) list.Add(new KeyValuePair<string, string>(mj, Path.Combine(local, "mod.json")));
            return list;
        }

        // Make this PC's copy of each mod match the pack: copy what is new or changed, delete the rest.
        public void SyncMods(List<ModInfo> mods)
        {
            var copies = new List<Tuple<string, string, long>>();
            foreach (var m in mods)
            {
                var local = LocalModPath(m);
                if (m.IsFile)
                {
                    if (Directory.Exists(local)) ForceDelete(local);
                    if (Changed(m.PackPath, local)) copies.Add(Tuple.Create(m.PackPath, local, new FileInfo(m.PackPath).Length));
                    continue;
                }
                if (File.Exists(local)) File.Delete(local);
                var files = ModFiles(m);
                if (!files.Any(p => p.Value.EndsWith(".big", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception(m.Label + ": no game archives in its folder in the pack (files starting with BIGF, " +
                                        "named .big, .gib, .ctr or the like). If it came as an installer, run that once and copy what it installed.");
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in files)
                {
                    wanted.Add(Path.GetFullPath(p.Value));
                    if (Changed(p.Key, p.Value)) copies.Add(Tuple.Create(p.Key, p.Value, new FileInfo(p.Key).Length));
                }
                if (Directory.Exists(local))
                {
                    foreach (var f in Directory.GetFiles(local, "*", SearchOption.AllDirectories))
                        if (!wanted.Contains(Path.GetFullPath(f))) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                    foreach (var d in Directory.GetDirectories(local, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                        if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                }
            }
            long total = Math.Max(1, copies.Sum(c => c.Item3)), done = 0;
            foreach (var c in copies)
            {
                CopyFile(c.Item1, c.Item2, b => Report(new Progress("Copying mods  " + Gb(done + b) + " of " + Gb(total), (double)(done + b) / total)));
                done += c.Item3;
            }
            if (copies.Count > 0) Log("copied " + copies.Count + " mod files");

            // mods the pack no longer has
            if (Directory.Exists(LocalModsDir))
            {
                var keep = new HashSet<string>(Mods.Select(LocalModName), StringComparer.OrdinalIgnoreCase);
                foreach (var e in Directory.GetFileSystemEntries(LocalModsDir))
                {
                    var name = Path.GetFileName(e);
                    if (keep.Contains(name)) continue;
                    if (Directory.Exists(e)) ForceDelete(e); else File.Delete(e);
                }
            }
        }

        static bool Changed(string src, string dst)
        {
            if (!File.Exists(dst)) return true;
            var a = new FileInfo(src); var b = new FileInfo(dst);
            return a.Length != b.Length || a.LastWriteTimeUtc != b.LastWriteTimeUtc;
        }

        // ------------------------------------------------------------------ hashes and the LAN code

        public string HashFile(string path, string what)
        {
            var fi = new FileInfo(path);
            var key = fi.FullName;
            HashEntry h;
            if (State.Hashes.TryGetValue(key, out h) && h.Size == fi.Length && h.Time == fi.LastWriteTimeUtc.Ticks) return h.Sha256;
            string hex;
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                var buf = new byte[4 << 20];
                long n = 0; int r;
                while ((r = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, r, null, 0);
                    n += r;
                    if (what != null && fi.Length > (64 << 20)) Report(new Progress(what + "  " + (100 * n / fi.Length) + "%", (double)n / fi.Length));
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                hex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
            State.Hashes[key] = new HashEntry { Size = fi.Length, Time = fi.LastWriteTimeUtc.Ticks, Sha256 = hex };
            return hex;
        }

        // A short code from the build and every file of every mod, in load order. Two PCs that
        // show the same code run the same game.
        public string LanCode(List<ModInfo> mods)
        {
            var sb = new StringBuilder();
            sb.Append(Pack.Version).Append('\n').Append(State.BuildSha256 ?? "").Append('\n');
            foreach (var m in mods)
            {
                sb.Append(m.IsPatch ? "patch " : "mod ").Append(m.Name.ToLowerInvariant()).Append('\n');
                var local = LocalModPath(m);
                if (m.IsFile) { sb.Append(HashFile(local, "Checking " + m.Label)).Append('\n'); continue; }
                var root = Path.GetFullPath(local).TrimEnd('\\', '/');
                var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                                     .Select(f => f.Substring(root.Length + 1))
                                     .OrderBy(f => f.ToLowerInvariant().Replace('\\', '/'), StringComparer.Ordinal);
                foreach (var rel in files)
                    sb.Append(rel.ToLowerInvariant().Replace('\\', '/')).Append(' ')
                      .Append(HashFile(Path.Combine(root, rel), "Checking " + m.Label)).Append('\n');
            }
            SaveState();
            using (var sha = SHA256.Create())
            {
                var hex = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
                return hex.Substring(0, 4) + "-" + hex.Substring(4, 4);
            }
        }

        // ------------------------------------------------------------------ play

        public string BuildArguments(List<ModInfo> mods)
        {
            // -rk7 is the official launcher's "started by a launcher" marker: without it (or
            // -multiInstance) generals.exe only shows "Please start Zero Hour Reforged from its launcher".
            var args = new List<string> { "-rk7" };
            if (string.Equals(Settings.Mouse, "reforged", StringComparison.OrdinalIgnoreCase)) { args.Add("-mouse"); args.Add("reforged"); }
            foreach (var m in mods)
            {
                args.Add(m.IsPatch ? "-modpatch" : "-mod");
                args.Add("\"" + LocalModPath(m) + "\"");
            }
            if (!string.IsNullOrWhiteSpace(Pack.ExtraArgs)) args.Add(Pack.ExtraArgs.Trim());
            if (!string.IsNullOrWhiteSpace(Settings.ExtraArgs)) args.Add(Settings.ExtraArgs.Trim());
            return string.Join(" ", args);
        }

        // Everything Play does before the game starts. Returns the LAN code.
        public string Prepare(List<ModInfo> mods)
        {
            if (!BaseReady) throw new Exception("Zero Hour is not set up on this PC yet. Open Settings and press Copy Zero Hour.");
            InstallBuild();
            SyncAddons();
            SyncMods(mods);
            Report(new Progress("Checking files...", -1));
            return LanCode(mods);
        }

        public Process Start(List<ModInfo> mods)
        {
            var exe = Path.Combine(GameDir, "generals.exe");
            var args = BuildArguments(mods);
            Log("start " + exe + " " + args);
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = GameDir, UseShellExecute = false };
            return Process.Start(psi);
        }
    }
}
