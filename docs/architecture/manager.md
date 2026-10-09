# Manager architecture and configuration

The WPF shell links the existing patch engine and exact-build verifier. Core owns cue metadata, library/configuration and runtime projections. AudioPreview uses our adapter around NVorbis with NAudio output; no installed-game DLL is shipped. Steam detection reads registry/library manifests and offers a folder override. The net462 music and Home Points helpers execute inside the game; the manager itself uses .NET 8.

Each cue has a stable public string ID. Numeric TrackCue values appear only in the compiled runtime projection. Areas sharing a cue share one pool. IDs are not IL method names.

```json
{
  "schemaVersion": 1,
  "musicEnabled": true,
  "unlimitedHomePoints": true,
  "musicPools": {
    "z1_spawning_meadows": { "enabled": true, "tracks": ["<64-character content SHA-256>"] }
  },
  "library": {
    "<64-character content SHA-256>": {
      "id": "<64-character content SHA-256>",
      "filename": "My exploration music.ogg",
      "importedFrom": "C:/My Music/My exploration music.ogg",
      "duration": 120.0,
      "format": "Ogg Vorbis"
    }
  },
  "previewVolume": 0.65
}
```

Configuration exports remove importedFrom, gamePath and enemySpritesFolder; they do not contain music or sprites. Locate File reconnects an exact-content track. Library IDs are deduplicated, lower-case SHA-256 values; users see filenames. Disabled pools and unassigned cues compile to empty lists. Imports decode the complete mono/stereo Vorbis stream; source files remain untouched.

User data: `%LOCALAPPDATA%/CrystalProjectModManager/`. Library files are content-addressed OGG copies. Backups contain the exact original executable. Runtime config recovery copies are keyed by their hash. Draft configuration is distinct from deployed manifest state. Repair reconstructs deployed assets using verified local copies, never editor drafts. If a trusted source is missing, repair stops.

Runtime: `Mods/CrystalProjectModManager/config.json` and `Music/<hash>.ogg` under the game folder. Two helper DLLs remain beside the EXE because CLR resolution requires them there. Only assigned music is initially deployed. Previously deployed music remains for safe restoration/ownership tracking; automatic garbage collection is deliberately absent in this preview.

Apply composes the selected features from the original, verifies all game constants and unrelated method bodies, stages music/config/helpers away from Steam, snapshots existing targets, writes a durable journal, atomically replaces files, verifies their hashes, then commits the manifest. Removal of unused recognized helpers is journaled too. Recovery and cleanup share the per-install mutex. Linked deployment ancestors are refused. Unknown changed files remain protected during ordinary Apply; explicit Repair may replace recorded managed music/config from verified copies. No unknown EXE is patched or restored.

The generalized hook executes at NAudioMusicManager's queued construction gate. The game's active/fading/queued duplicate suppression remains intact. One-shot continuation uses the same queue. Bookmarks remember the selected file per manager so PCM offsets still refer to the same audio. Empty or unavailable pools restore original packed-track metadata. Replacements reset offsets, play-start, volume and loop metadata; untagged files loop from zero to their own duration. PlayOnce still uses the game's one-shot constructor.

Home Points patch logic is retained from the verified newer implementation, not the failed enum-editing prototype. It grows from three slots by one as needed, retains expanded BSON arrays, repairs malformed short/null arrays, and translates menu rows without compacting saves. Expanded saves require this patch when loaded.

Original preview resolves the selected logical cue through the installed `bgm.config`, reads the matching bounded MP3 segment from `bgm.dat` (or the game's loose BGM file), and plays it through NAudio. The stream is read-only and retained only during playback; no soundtrack cache or library import is created. Pause/stop/seek/volume use the existing player. Previous/Next remain replacement-pool controls and are disabled during original preview.

The net8.0-windows Sprites library independently handles `Content/Textures/Monster.dat`. The installed 1.6.9 enemy database's nonempty TexturePath/TexturePathAlt references all use the Monster category; the archive contains 273 distinct PNGs. It has a zero version byte, zero boolean flag, little-endian entry count, seven-byte timestamps for each entry, then length-prefixed UTF-8 character names and PNG byte payloads. Rebuilds preserve the header/timestamp table and entry order and copy all unchanged payloads exactly. PNG chunks and CRCs are checked and all pixels decoded through WPF. Images must use uniform 1×, 2× or 4× original dimensions; validation uses the backed-up baseline even when replacing already enlarged images. No game code or images are embedded in the library.

`enemySpritesFolder` is an optional local configuration preference. Exported PNG workspaces contain `sprites.json`, binding them to the baseline archive hash. Validation builds a memory snapshot of all changes against the live archive. Missing PNGs keep installed textures; unknown filenames fail preflight. Apply checks that the live archive still matches the validated snapshot, runs the existing game-closed guard, and atomically replaces the single archive. Sprite Apply and Restore have separate UI controls. When the resulting archive contains HD images, an engine callback must successfully install HD rendering and its original-size catalog before archive replacement; callback failures leave the archive unchanged. If a later archive step fails, the committed rendering patch remains safe for existing original-size images.

Sprite state and the original archive live in `EnemySprites/<installation-key>/` outside Steam. Sprite operations share the engine's per-install mutex, including reentrant HD installation callbacks, so another process cannot remove rendering support between patch installation and archive replacement. Backups are verified against their recorded hash before use. A durable state record stores baseline, applied and pending hashes before atomic replacement; recovery accepts only the previous or proposed archive and reconciles an interrupted commit. External archive changes or a corrupted backup stop the operation. Restoration retains the state record and edited workspaces. Synthetic tests and private-copy archive acceptance cover individual and full-library updates; native battle/atlas visual acceptance remains pending.

`Selection.HdSprites` defaults to false for older manifests. All eight music/Home Points/HD combinations are built from the pristine hash-verified executable. HD changes only monster texture lookup, battle sizing/indicators, monster outline pixel offsets, atlas detail fit/origin and the six-argument monster portrait helper. The net462 `CrystalProjectHDSprites.dll` uses reflection and a weak texture-to-size map without game-library references. `Mods/CrystalProjectModManager/sprite-sizes.txt` records each original Monster texture's dimensions; the engine owns, journals and hash-verifies this file and keeps an external repair copy. CBattle texture lookup registers the actual normal/alternate key and full-resolution texture. Logical dimension hooks preserve world geometry and indicator ratios; atlas draw scale compensates for density and portrait source rectangles multiply by density. Integer-rounded atlas centers match the original even for odd canvas dimensions.

Home Apply retains the installed HD selection. Engine removal of HD rendering, including legacy-installer restore, checks live archive dimensions against the verified catalog and refuses while enlarged images remain. Restore Backed-Up Sprites returns the original archive but leaves the renderer installed; Remove HD Support then retains current music/Home Points. Existing helpers and unrelated method bodies/constants are preserved. Runtime tests execute actual patched texture lookup and SetMonsterTexture under .NET Framework with synthetic texture objects; they do not substitute for native GPU/battle/atlas acceptance.

Known limits: first import format is Vorbis OGG only; no transcoder; no separate per-area pools for areas sharing one cue; area categorization is a single factual Areas group; unsigned preview; administrative launch stores data under the elevated account; native gameplay, real DPI, atlas and event occurrence completeness remain pending. Tests use original synthetic tones and private local game assemblies; no game binaries or decoded source belong in Git or releases.
