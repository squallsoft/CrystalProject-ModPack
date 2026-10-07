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

Configuration exports remove importedFrom and gamePath; they do not contain music. Locate File reconnects an exact-content track. Library IDs are deduplicated, lower-case SHA-256 values; users see filenames. Disabled pools and unassigned cues compile to empty lists. Imports decode the complete mono/stereo Vorbis stream; source files remain untouched.

User data: `%LOCALAPPDATA%/CrystalProjectModManager/`. Library files are content-addressed OGG copies. Backups contain the exact original executable. Runtime config recovery copies are keyed by their hash. Draft configuration is distinct from deployed manifest state. Repair reconstructs deployed assets using verified local copies, never editor drafts. If a trusted source is missing, repair stops.

Runtime: `Mods/CrystalProjectModManager/config.json` and `Music/<hash>.ogg` under the game folder. Two helper DLLs remain beside the EXE because CLR resolution requires them there. Only assigned music is initially deployed. Previously deployed music remains for safe restoration/ownership tracking; automatic garbage collection is deliberately absent in this preview.

Apply composes the selected features from the original, verifies all game constants and unrelated method bodies, stages music/config/helpers away from Steam, snapshots existing targets, writes a durable journal, atomically replaces files, verifies their hashes, then commits the manifest. Removal of unused recognized helpers is journaled too. Recovery and cleanup share the per-install mutex. Linked deployment ancestors are refused. Unknown changed files remain protected during ordinary Apply; explicit Repair may replace recorded managed music/config from verified copies. No unknown EXE is patched or restored.

The generalized hook executes at NAudioMusicManager's queued construction gate. The game's active/fading/queued duplicate suppression remains intact. One-shot continuation uses the same queue. Bookmarks remember the selected file per manager so PCM offsets still refer to the same audio. Empty or unavailable pools restore original packed-track metadata. Replacements reset offsets, play-start, volume and loop metadata; untagged files loop from zero to their own duration. PlayOnce still uses the game's one-shot constructor.

Home Points patch logic is retained from the verified newer implementation, not the failed enum-editing prototype. It grows from three slots by one as needed, retains expanded BSON arrays, repairs malformed short/null arrays, and translates menu rows without compacting saves. Expanded saves require this patch when loaded.

Original preview resolves the selected logical cue through the installed `bgm.config`, reads the matching bounded MP3 segment from `bgm.dat` (or the game's loose BGM file), and plays it through NAudio. The stream is read-only and retained only during playback; no soundtrack cache or library import is created. Pause/stop/seek/volume use the existing player. Previous/Next remain replacement-pool controls and are disabled during original preview.

Known limits: first import format is Vorbis OGG only; no transcoder; no separate per-area pools for areas sharing one cue; area categorization is a single factual Areas group; unsigned preview; administrative launch stores data under the elevated account; native gameplay, real DPI, atlas and event occurrence completeness remain pending. Tests use original synthetic tones and private local game assemblies; no game binaries or decoded source belong in Git or releases.
