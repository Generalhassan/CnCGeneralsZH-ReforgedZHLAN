# Zero Hour LAN Launcher

A launcher for **Zero Hour Reforged v3.6.0**, the last Reforged version that loads mods. It reads
everything from one **LAN pack** folder (a USB stick or a NAS share), so the whole group plays the
same build with the same mods in the same order.

- **Presets:** one click to play, e.g. *ShockWave night* = ShockWave + its balance patch.
- **Custom:** tick mods, put them in order, and save the result as a preset in the pack.
- **LAN code:** a short code made from every file of the build and the mods in the preset. If everyone
  in the room shows the same code, everyone has the same game.
- **Leaves things alone:** the launcher plays from its own copy of Zero Hour. Your Steam/EA install
  and the official Reforged launcher (3.8.0) keep working as before.

## The LAN pack

```
ZH-LAN-Pack\
  ZHLauncher.exe
  pack.json                    name, presets, which mods are patches
  build\Reforged-v3.6.0.zip    the game build
  mods\                        each mod as a folder or a single .big
  addons\                      optional: ReforgedTextures.big (HD textures)
```

Each PC copies the build and the mods it needs from the pack the first time, and after that only what
changed. Files not in the pack are deleted from the PC's copy, so nobody keeps an old mod file around.

## 1. Get the build (once)

The repository's GitHub Actions workflow builds Reforged v3.6.0 and the launcher, and puts them
together as a ready pack. No Visual Studio needed on your PC.

1. Make a new GitHub repository and upload this folder into it, `.github` included. Make the
   repository **public**: Reforged is GPL code anyway, and public repositories don't count against
   GitHub's artifact storage limit (500 MB on a free private one, and the build with its art is
   larger than that).
2. **Actions** tab → **Build LAN pack** → **Run workflow** (the tag is already `v3.6.0`).
3. After roughly 15–25 minutes, open the run and download **ZH-LAN-Pack-v3.6.0** under Artifacts.
   Unzip it: that folder is your pack.

The other way: if you already built 3.6.0 with the picker script, run its `export -Version v3.6.0`
and put the zip into `build\` as `Reforged-v3.6.0.zip`.

## 2. Add mods and presets

- Put each mod into `mods\`: a folder holding the mod's `.big` files (and `Data`/`Art` if it has
  them), or a single `.big` file.
- Patches: a balance patch for a mod needs `"patch": true` in a `mod.json` in its folder, or its
  name in `"patches"` in `pack.json`. It's then loaded with `-modpatch` and only changes the mod
  loaded before it.
- Presets: in the launcher, **Custom...** → tick mods → **Move up/down** → **Save as preset**. Or
  write them into `pack.json` yourself:

```json
{
  "name": "Dübendorf LAN",
  "version": "v3.6.0",
  "build": "build/Reforged-v3.6.0.zip",
  "extraArgs": "",
  "patches": [],
  "presets": [
    { "name": "ShockWave night", "description": "ShockWave with the balance patch",
      "mods": ["ShockWave", "ShockWave-Balance"] },
    { "name": "Contra", "mods": ["Contra"] }
  ]
}
```

`mods` are the folder (or `.big` file) names in `mods\`, in load order: where two mods change the
same file, the later one wins. `extraArgs` adds a switch for everyone; each PC can also set its own in
Settings (e.g. `-win`).

## HD textures (optional)

3.6.0 already knows Reforged's HD textures. It loads `ReforgedTextures.big` (textures at twice EA's
detail and the sharper ground) when it sits next to `generals.exe`. The file used to come from a GitHub
release that no longer exists, so the build doesn't include it. Today it's the **HD Textures** add-on in
the official launcher (zerohour.gg, about 1.3 GB):

1. Install the HD Textures add-on in the official launcher, on any PC.
2. Copy `ReforgedTextures.big` into the pack's `addons\` folder. Only take that one file: the "rebuilt
   models" add-on (`ReforgedUSA.big` and similar) is for 4.0.0 and up.
3. The next Play copies it to each PC and puts it next to the game. Settings → **Add-ons** turns it
   off on a PC that is short of disk or video memory.

How 3.6.0 combines it with mods: a mod's own textures always win for that mod's armies. HD fills in
where a mod uses EA's textures unchanged, on Zero Hour's own armies and on the ground. A mod's own
`Reforged*.big` is ignored. **Classic Graphics** in the game's options switches HD off. Add-ons don't
touch the match, so PCs with and without them play together. That's also why they aren't part of the
LAN code.

## 3. On each PC

1. Copy the pack over, or run `ZHLauncher.exe` straight from the stick or share.
2. The first start opens **Settings**. It finds Zero Hour and Generals by itself in most cases; if
   not, press Browse. Then press **Copy Zero Hour into the launcher** (a few GB, once per PC).
   - If Reforged from the official launcher is installed into that Steam folder, the launcher warns
     you. Press **Uninstall** in the official launcher (or Verify files in Steam), copy, then install
     it again in the official launcher. Otherwise its 3.8.0 files would mix into your 3.6.0.
3. Pick a preset → **PLAY**. The first time it installs the build and copies the mods.
4. Before hosting, compare the **LAN code** across the room (**Check files** shows it without
   starting the game).

Where the launcher keeps its files on each PC: `%LocalAppData%\ZHLauncher` (base copy, game folder,
mods, settings, `launcher.log`). The game folder uses hard links to the base copy, so it costs
almost no extra space.

## Notes

- Windows SmartScreen may warn once, because the exe isn't code-signed: **More info → Run anyway**.
- Settings, hotkeys and replays are in `Documents\Command and Conquer Generals Zero Hour Data`, shared
  with the official launcher's 3.8.0. Going back and forth should be fine; if an option looks odd in
  3.6.0, reset it in the game's options.
- The launcher always passes `-rk7`: without it `generals.exe` only shows "Please start Zero Hour
  Reforged from its launcher". Settings → Mouse adds `-mouse reforged` for Reforged's mouse controls.
- Windows Defender may report the launcher as `Trojan:Win32/Wacatac…!ml`. That's a machine-learning
  guess about new, unsigned programs. Check on virustotal.com, restore it, and add the pack folder and
  `%LocalAppData%\ZHLauncher` as exclusions.
- The launcher runs on .NET Framework 4.8, which ships with Windows 10 and 11: there's nothing to
  install.

## Building the launcher yourself

- On Windows: the workflow does it with Visual Studio's `csc` (see `build-pack.yml`).
- On Linux/macOS with Mono: `launcher/build.sh`.

The code is in `launcher/src`:
- `Engine.cs`: the file work.
- `MainForm.cs`: the window.
- `PresetList.cs`: the preset cards.
- `SettingsForm.cs`: the setup dialog.
- `Model.cs`: the JSON files.
