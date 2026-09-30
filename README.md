# Crystal Project Mod Installer v0.1.0

Windows Steam release. Home Points passed player testing; the music test log showed no errors across battle, boss, and victory selections.

## What this mod does

- **Random Music:** randomly selects your custom OGG tracks for normal battles, bosses, and victories. Preserves the existing v0.2.3 folder-pool and nested LoopPoint behavior. PvP is unchanged.
- **Unlimited Home Points:** with the game's **Enhanced Home Point** option enabled, adds a New Home Point Slot after the first three points and continues as you add more. Turning the game option off keeps its normal single-Home-Point behavior.

Use either mod or both. The installer rebuilds your own game executable; it includes no game executable or music tracks.

## Supported Crystal Project version

Steam Windows **1.6.9.0**, verified by its exact original SHA-256:
`36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6`

Other builds are refused. A matching version number alone is not sufficient.

## Installation

1. Close Crystal Project.
2. Keep this installer outside the game folder. Open **CrystalProjectModInstaller.exe** and allow its Administrator prompt. This installer requests elevation at launch so it can update installations in Program Files.
3. Check the detected folder, or choose **Browse**. Select the mods and click **Install / Apply Changes**.
4. For music, place your own `.ogg` files in `ogg/Battle`, `ogg/Boss`, and `ogg/Victory` inside the game folder. An empty folder uses the original music. Files directly in `ogg` are ignored. Keep the game's built-in Random Music option OFF, as required by the existing custom-music mod.
5. For additional Home Points, enable **Enhanced Home Point** in the game's assist options.

No scripts, development tools, or separate .NET installation are needed. Music files are not included.

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
- **Music:** check the three OGG folders and turn the game's built-in Random Music OFF. Runtime log: `%LOCALAPPDATA%\CrystalProjectRandomMusic\randommusic.log`.

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
