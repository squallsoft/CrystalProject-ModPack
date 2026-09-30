# Crystal Project Mod Installer v0.1.0

Windows Steam release. Home Points passed player testing; the music test log showed no errors across battle, boss, and victory selections.

**[Download the latest installer](https://github.com/squallsoft/CrystalProject-ModPack/releases/latest)** — under **Assets**, download `CrystalProjectModInstaller-v0.1.0-win-x64.zip`. The GitHub **Source code** downloads are for developers, not the player installer.

**Start here:** [Install](#installation) · [Add your music](#adding-your-own-music-step-by-step) · [Home Points](#using-unlimited-home-points) · [Troubleshooting](#troubleshooting) · [Road to 1.0](docs/road-to-1.0.md)

## What this mod does

- **Random Music:** randomly selects your custom OGG tracks for normal battles, bosses, and victories. Preserves the existing v0.2.3 folder-pool and nested LoopPoint behavior. PvP is unchanged.
- **Unlimited Home Points:** with the game's **Enhanced Home Point** option enabled, adds a New Home Point Slot after the first three points and continues as you add more. Turning the game option off keeps its normal single-Home-Point behavior.

Use either mod or both. The installer rebuilds your own game executable; it includes no game executable or music tracks.

## Supported Crystal Project version

Steam Windows **1.6.9.0**, verified by its exact original SHA-256:
`36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6`

Other builds are refused. A matching version number alone is not sufficient.

## Installation

1. Close Crystal Project completely.
2. Download the installer ZIP from [Releases](https://github.com/squallsoft/CrystalProject-ModPack/releases/latest). Right-click it and choose **Extract All**. Keep the extracted installer outside the game folder, such as in Documents. Do not run it from inside the ZIP.
3. Open **CrystalProjectModInstaller.exe** and allow its Administrator prompt. No scripts, development tools, or separate .NET installation are needed.
4. Check the detected game folder. It must contain **Crystal Project.exe**. Use **Browse** if needed; other Steam drives are supported.
5. Check **Random Battle / Boss / Victory Music**, **Unlimited Home Points**, or both, then click **Install / Apply Changes**. Wait for confirmation.
6. Follow the music or Home Point instructions below before playing.

## Adding your own music: step by step

**You supply the music. Installing the mod does not download or include any songs.**

### 1. Open the actual game folder

In your Steam Library, right-click **Crystal Project → Manage → Browse local files**. Find `Crystal Project.exe` in the folder that opens.

A typical location is `C:\Program Files (x86)\Steam\steamapps\common\Crystal Project`, but your Steam library may be on another drive. Use the folder Steam opens rather than assuming this example path.

### 2. Create the music folders

Next to `Crystal Project.exe`, create a folder named **ogg** if it does not exist. Inside it, create three folders named **Battle**, **Boss**, and **Victory**. Preserve any music already there.

Your folders should look like this:

```text
Crystal Project/
├── Crystal Project.exe
├── CrystalProjectRandomMusic.dll
└── ogg/
    ├── Battle/
    │   ├── My Battle Song.ogg
    │   └── Another Battle Song.ogg
    ├── Boss/
    │   └── My Boss Song.ogg
    └── Victory/
        └── My Victory Fanfare.ogg
```

The example song names are placeholders. You can use your own filenames, including spaces. No numbering or `.normalized` suffix is required.

### 3. Copy Ogg Vorbis music into the right category

| Put the file directly in… | Used for… |
| --- | --- |
| `ogg/Battle` | Normal encounters |
| `ogg/Boss` | Boss encounters |
| `ogg/Victory` | Victory music after normal and boss encounters |

Use **Ogg Vorbis audio with a `.ogg` extension**. MP3, WAV, FLAC, and Ogg Opus are not the documented input format. If necessary, export/convert the audio to **Ogg Vorbis** in an audio editor or converter. **Renaming `song.mp3` to `song.ogg` does not convert it.**

Files must be directly inside the category folder. These placements will **not** be picked up:

```text
ogg/My Song.ogg                   ← outside a category
ogg/Battle/Album/My Song.ogg       ← nested subfolder
installer-folder/ogg/Battle/...   ← outside the game folder
```

You may put several tracks in each category, or just one. With one track, that category always selects it. With several, selection is random and the mod tries to avoid immediately repeating the last track; this is not a playlist that plays every song in order. Copy a track into multiple category folders if you want it eligible in each.

An empty or missing category leaves that category's game music unchanged on a fresh launch. You do not have to fill all three folders.

### 4. Check the two different music switches

- In **this installer**, **Random Battle / Boss / Victory Music** must be **checked and applied**.
- In **Crystal Project itself**, its built-in **Random Music** option must be **OFF**.

These are different settings. The installer enables this custom music mod; the in-game option controls the game's own randomizer.

### 5. Launch and test

Start Crystal Project. Enter a normal battle, a boss battle, and finish a battle to hear each category. This mod does not replace field/exploration music or PvP music.

To add, remove, or replace songs later: **close the game, change the files, and relaunch**. You do not need to reinstall the mod or apply the installer again just to change music files. Restarting also clears any previously assigned tracks from memory.

### Looping and volume

For reliable looping, use Ogg Vorbis tracks with valid embedded loop metadata. Copying a file does not create seamless loop points, trim silence, or normalize volume. Adjust those in your audio editor before export, and verify that conversion preserved the loop tags.

Supported tag names are `LOOPSTART` / `LOOP_START`, `LOOPEND` / `LOOP_END`, and `LOOPLENGTH` / `LOOP_LENGTH`. With a valid start tag but no end or length tag, the physical end of the audio is used as the loop end.

**Current v0.1.0 limitation:** a track without loop tags can be selected, but the runtime leaves existing loop values unchanged. Correct looping is therefore not guaranteed for untagged tracks. Prefer properly tagged tracks for this release. See [music hardening planned for 1.0](docs/road-to-1.0.md).

For advanced metadata editing, decimal values are interpreted as seconds. Integer values use a duration-based heuristic to distinguish seconds from PCM samples; do not assume every integer is a sample count. For example, `LOOPSTART=5.0` unambiguously means five seconds. Choose an end after the start and within the track.

## Using Unlimited Home Points

1. Check **Unlimited Home Points** in the installer and apply.
2. Enable **Enhanced Home Point** in the game's assist options.
3. Set your first three points normally. **New Home Point Slot** remains available for point #4, #5, and beyond.
4. Scroll the destination list to reach additional points. They are stored in the game's existing saves.

When Enhanced Home Point is OFF, the game's normal single-Home-Point behavior applies. Read the save compatibility warning below before disabling the mod itself.

## Updating

Close the game, extract the newer installer outside Steam, and open it. Use the same Windows account. If it supports your game build, apply your desired choices. Keep your external backup store.

## Changing enabled mods

Change the checkboxes and apply. Each configuration starts from the verified original backup, so changing one mod preserves your choice for the other.

**Expanded saves need the Home Points mod:** before disabling it, preserve your expanded save and use a separate save containing at most three Home Points. The vanilla game's sanitizer cannot safely load expanded arrays. This installer does not rewrite saves or silently discard points.

## Restoring vanilla

Close the game and click **Restore Vanilla**. This restores the exact supported original executable. Follow the expanded-save guidance above. OGG files are preserved.

## Steam game updates

An unknown executable is never patched or replaced with an older backup. Wait for an installer that explicitly supports the updated game. If a known legacy install has no trustworthy original backup, use **Steam → Properties → Installed Files → Verify integrity of game files**, then rerun the installer.

## Save compatibility

Existing one-to-three-point collections are supported. Added points use the game's existing BSON save structure. Automated tests cover 40 points, serialization/reload, malformed arrays, and switching collections. Home Points also passed player gameplay testing.

## Troubleshooting

- **Game running:** close it completely before applying, restoring, repairing, or cleaning.
- **Unsupported:** do not force installation. Verify game files in Steam or wait for support for your build.
- **Missing/corrupt backup:** the installer refuses to guess. Keep the log and recover a verified original or use Steam verification.
- **Read-only/access denied:** remove an intentional read-only setting or resolve folder permissions, then retry. Do not keep retrying an unsupported build.
- **Interrupted install:** use **Repair**. Its transaction journal restores the previous files before rebuilding. If an external program changed a file during interruption, recovery stops rather than overwriting it.
- **Legacy files:** after applying a supported configuration, **Clean Legacy Mod Files** archives and removes only exact known filename/hash matches. Changed and unknown files stay.
- **Still hearing original music:** confirm the mod checkbox was applied, turn the game's built-in Random Music OFF, check that `.ogg` files are directly in the correct category folder beside the game EXE, and restart the game. Field/exploration music is intentionally unchanged.
- **Only one song plays:** check how many actual `.ogg` files are in that category. Files in nested folders or other formats are ignored. Several encounters are needed to judge random selection.
- **A track is silent, fails, or loops incorrectly:** check that it is genuine Ogg Vorbis, can play in an audio player, and has suitable loop metadata. Remove that track while the game is closed and retest with a known-working track. Export volume/silence and loop boundaries are properties of your audio files.
- **Music diagnostics:** paste `%LOCALAPPDATA%\CrystalProjectRandomMusic` into File Explorer's address bar and open `randommusic.log`. `BATTLE`, `BOSS`, and `VICTORY` lines show selected paths. `DEBOUNCE` is normal repeated-call suppression. `SKIP` indicates an unavailable/empty category; `ERROR` indicates a reported assignment failure. A selected-path entry does not prove the audio sounded correct.

If you need help, [open an issue](https://github.com/squallsoft/CrystalProject-ModPack/issues) with your installer version, game version, affected category, and relevant log lines. Review personal paths in logs before sharing. Do not upload game executables, saves, or music files.

## Uninstalling

Restore Vanilla, optionally clean legacy files, then delete the installer folder. Keep external backups until you no longer need them. Your saves and OGG tracks are never deleted by this installer.

## Where backups/logs are stored

`%LOCALAPPDATA%\CrystalProjectModInstaller\`

- `backups`: original game EXE, verified by SHA-256.
- `manifests`: installed selections, versions, hashes, timestamps, and interrupted-operation journals.
- `logs`: diagnostics.
- `legacy`: archived old installers, patch payloads, backups, and a cleanup report.
- `work`: temporary staging and recovery files.

If Windows asks for another administrator account, these folders belong to that elevated account. Never distribute your backup or legacy archive: they contain your local game files. See NOTICE.md for third-party components.
