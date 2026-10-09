# Crystal Project Mod Manager

**0.1.0-rc1 development preview · Windows x64 · Crystal Project Steam 1.6.9.0**

Manage your own soundtrack and Unlimited Home Points in one desktop app. This build is ready for local acceptance testing; **native gameplay validation is still pending**. It has not been uploaded as a new public release.

- Replace any of the **71 discovered music cues**: exploration, battle, boss, victory, story, title, and special themes. Seven ambience cues are also available.
- Extract and bulk-replace all **273 enemy sprites** from the installed `Monster.dat` archive, with PNG previews and verified backup restoration.
- Give each cue one track or a random pool. Empty or disabled pools play original music.
- Search by cue name, area, original title, or identifier. Filter by **All songs**, **Replaced**, or **Not replaced**, and sort alphabetically or put either replacement status first. Regions sharing one game cue share one replacement pool.
- Preview with play/pause, stop, seek, previous/next, and volume.
- Enable Unlimited Home Points alongside music in one safe patch operation.
- Repair, restore vanilla, and archive recognized legacy artifacts.

No Crystal Project executable, game libraries, soundtrack, user music, or saves are distributed. The app patches your legally installed game locally.

## Start the app

Local build: `artifacts/release/manager-0.1.0-rc1/CrystalProjectModManager.exe`. Portable ZIP: `artifacts/CrystalProjectModManager-0.1.0-rc1-win-x64.zip`. Extract outside Steam and keep its README and license notices with it.

1. Close Crystal Project before applying or restoring changes.
2. Open **CrystalProjectModManager.exe** and allow the Administrator prompt. The packaged app includes .NET; players need no scripts.
3. Check **Installation / Game**. Steam libraries are detected automatically; **Choose Game Folder** provides a manual override.
4. Earlier installer users: choose **Import Existing Music** on Home or **Import Legacy Pools** in Settings. Your Battle/Boss/Victory source folders stay untouched.
5. Configure Music and Home Points, then choose **Apply Changes** on Home.
6. Launch normally through Steam or use **Launch Game**.

Only the exact validated Windows executable is supported. A matching version label alone is insufficient; unknown updates are refused.

The previous battle/boss/victory-only installer remains documented in [its archived README](docs/installer-v0.1.0-README.md). Its public release is separate from this manager preview.

## Add your music

Open **Music**, select a cue, then **Add Music** for one or multiple files, or **Add Folder** for top-level OGG files. Drag files onto the pool to import them.

Use genuine **Ogg Vorbis `.ogg`**, mono or stereo. MP3, WAV, FLAC, and Ogg Opus imports are not supported in this preview. Convert them to Vorbis in an audio editor first; renaming does not convert audio. Managed copies are deduplicated by content; originals are never edited.

- One track: always selected for that cue.
- Multiple tracks: random selection at a new playback start; no immediate repeat when another file is available.
- Empty pool or unchecked **Use this replacement pool**: original music.

Repeated requests for an active cue keep the current song. Field bookmark restoration keeps the selected file so its saved position remains meaningful. Shared area cues retain continuous playback. The discovered **Proving Meadows** shares Spawning Meadows music; no separate Proving Grounds cue was found.

Vorbis loop tags are supported. Untagged replacements loop their own full duration rather than inheriting original soundtrack bounds. One-shot cues still play once. Audible transitions need the [native acceptance checks](docs/testing/manager-rc1-native.md).

Keep the game's built-in Random Music OFF for predictable per-cue assignments. With it ON the game changes the original cue it requests; that behavior remains a native acceptance item.

## Preview and edit

Select a track and use the bottom **Play** button, or double-click it. The compact player pauses/resumes, stops, seeks, adjusts preview volume, and moves between tracks. Hover over an icon for its label. Previewing does not modify deployed game files.

The Music browser counts enabled, nonempty pools as **Replaced**. Empty or disabled pools appear under **Not replaced**; turning replacement music off in Settings makes every cue not replaced. Status reflects your draft configuration, which reaches the game after Apply Changes. Search, category, status filter, and sort choices stay selected while editing pools or switching pages during the session.

Select a cue and choose **Preview Original** to hear its default game music or ambience. The same bottom player controls playback. Originals are read directly from your installed soundtrack; they are not imported into a replacement pool. Select your game folder in **Installation / Game** first. Previous/Next apply to replacement pools and are disabled during an original preview.

**Remove** takes a track out of a pool. **Reset to Original** clears the pool. **Locate File** reconnects missing music using the same audio bytes; its filename may differ. A missing assigned file blocks Apply.

Draft edits are saved automatically and reach the game only after **Apply Changes**. Disable all replacement music in Settings or clear individual pools to restore original music while keeping Home Points enabled.

## Enemy sprites

1. Select your game folder in **Installation / Game**, then open **Enemy Sprites**. The manager reads all textures in `Content/Textures/Monster.dat`.
2. Choose **Extract All Sprites** and a parent folder. The manager creates a new editing folder containing every original PNG and `sprites.json`.
3. Edit any or all PNGs in your image editor. Keep the extracted filenames and original canvas dimensions; preserve transparency for clean edges. **Replace Selected PNG** also imports a single prepared replacement into the editing folder. Search and select a sprite to preview its editing-folder image.
4. Choose **Validate Edited Folder**. The manager decodes every supplied PNG, checks its dimensions, and identifies files differing from the currently installed sprites. Missing PNGs leave their installed sprite unchanged; unknown PNG filenames are rejected. Use **Choose Edited Folder** to reconnect an existing export.
5. Close Crystal Project and choose **Apply N Sprite Updates**. This applies the validated snapshot; validate again after further artwork edits. The archive is replaced atomically, retaining every unchanged sprite and its metadata. The first update saves a verified backup outside Steam.
6. **Restore Backed-Up Sprites** restores the exact archive captured before the first manager sprite update. Your edited PNGs stay in the editing folder. Extraction after updates still exports the backed-up originals.

Sprite updates have their own Apply and Restore controls on the Enemy Sprites page. Home's **Apply Changes** and **Restore Vanilla** handle music/Home Points and do not change the sprite archive. Restore sprites separately before uninstalling. Backups are stored under `%LOCALAPPDATA%/CrystalProjectModManager/EnemySprites/`, separately for each installation.

Externally changed sprite archives and corrupted backups are refused; unknown archive formats are never written. A workspace must match the installation's source archive. If Steam updates or another tool changes the archive, preserve your artwork and backup before reconciling it; the manager will not silently replace those changes. The backup captures the installed archive at the first update, including any existing edits from other tools.

The automated round trip is verified on a private copy of the 1.6.9 archive; native battle/atlas visual acceptance remains pending. Extraction and tests keep game artwork local and never add it to this repository or app package.

## Unlimited Home Points

Check **Unlimited Home Points** and Apply. Enable **Enhanced Home Point** in the game's assist options. Set the first three destinations normally, then continue using **New Home Point Slot**. Storage grows only when needed; destinations remain in existing saves.

**Saves with more than three destinations require this mod.** Before disabling it or restoring vanilla, retain expanded saves and use a separate save with at most three points. The manager never changes or truncates saves.

The old initialization crash came from an unrelated corrupted teleport enum. This patch preserves every game constant. Automated tests exercise 40 destinations, BSON reload and actual menu scrolling. Full native save/restart and warp acceptance remains required for the manager build.

## Configuration sharing

Settings offers **Export Configuration** and **Import Configuration**. Exports contain assignments, filenames and preferences, without music bytes or source paths. Use **Locate File** after importing on another computer. Sharing configuration does not grant rights to music.

## Repair, restore, and Steam updates

**Repair Installation** rebuilds deployed settings from the verified original and trusted managed copies, preserving unapplied drafts. Interrupted transactions are rolled back first. Externally changed files can stop recovery rather than being silently overwritten.

**Restore Vanilla** restores the exact original executable and removes recognized unused helpers. It preserves music and saves. Follow the expanded-save guidance above.

After a Steam update, unknown executables are never patched or replaced with older backups. Wait for explicit support. If the original is missing or fails verification, use **Steam → Properties → Installed Files → Verify integrity of game files**.

**Clean Legacy Mod Files** archives and removes exact known filename/hash matches only. Unknown files, changed files, linked paths, official Crystal Edit files, and your music remain.

## Data and uninstall

`%LOCALAPPDATA%/CrystalProjectModManager/` contains your library, drafts, logs, verified backups, deployment manifests, recovery config and legacy archives. The previous installer's verified backup is imported when needed. If UAC uses another account, data belongs to that elevated account.

Runtime files are the required helpers beside the EXE and `Mods/CrystalProjectModManager/` for config/music. Previously deployed tracks are retained; this preview has no automatic garbage collection.

To uninstall: close game, Restore Vanilla, optionally clean legacy files, then delete the manager folder. After restoration you may remove its isolated runtime folder. Preserve imported music/backups before deleting app data. The app never deletes saves.

## Development

Use a .NET 8 SDK. Build with `./tools/build-manager.ps1`; it also discovers the private SDK under `artifacts/dotnet`.

After a Release build, run the isolated Music browser checks and render all pages with `dotnet src/CrystalProjectModManager/bin/Release/net8.0-windows/CrystalProjectModManager.dll --render artifacts/ui-check --verify-browser`. Add `--small --dpi150` to check the minimum window size at a 150% render scale. Results are written to `music-browser-checks.txt` beside the screenshots; configuration uses an isolated `qa-data` folder.

Run sprite tests with `./tools/test-sprites.ps1`. To additionally check the installed archive on a private copy, pass `-Archive 'C:/Program Files (x86)/Steam/steamapps/common/Crystal Project/Content/Textures/Monster.dat'`. Sprite tests generate their own PNG fixtures and never write to the supplied archive.

Run tests with your supported pristine executable and a developer FFmpeg executable that generates original test tones:

```powershell
./tools/test-manager.ps1 -FFmpeg 'C:/Tools/ffmpeg.exe' -PristineExecutable 'C:/PrivateBackups/Crystal Project.exe' -GameDirectory 'C:/SteamLibrary/steamapps/common/Crystal Project'
```

Tests run in isolated artifact folders; they never patch live Steam files or open user saves. Never commit game assemblies, decoded proprietary source, soundtrack, user music, saves, backups, or build artifacts.

See [audit](docs/MASTER_TASK_AUDIT.md), [cue map](docs/MUSIC_CUE_AUDIT.md), [architecture](docs/architecture/manager.md), and [native checklist](docs/testing/manager-rc1-native.md). LICENSE covers this project; NOTICE.md and the NAudio/NVorbis license files cover dependencies.
