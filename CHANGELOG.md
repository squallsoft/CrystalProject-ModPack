# Changelog

## Nonblocking sprite validation — 2026-10-09

- Scan edited folders in the background with cancellation; keep navigation, searching and replacement controls available.
- Show all invalid-image filenames and actionable dimension/file-size errors in a scrollable panel instead of repeated modal dialogs.
- Keep Apply disabled for invalid folders; recheck corrected files and refuse oversized PNGs before loading their full contents.

## 10× enemy sprites — 2026-10-09

- Accept uniform 10× PNG replacements alongside 1×, 2× and 4× images while retaining the original in-game size, atlas fit and portrait crops.
- Upgrade the existing HD helper safely. Image limits remain 8192 pixels per axis, 16,777,216 pixels total and 32 MiB per PNG.

## Sprite edit detection — 2026-10-09

- Refresh pending sprite updates when opening the sprite page, choosing an editing folder, replacing a PNG, or returning from an external editor.
- Distinguish an unchecked folder from zero changes, and revalidate current PNGs immediately before applying.

## HD installer compatibility fix — 2026-10-09

- Recognize verified music and Home Points helper DLLs from the previous manager build when installing HD support.
- Record helper hashes after successful installation so later app builds can upgrade their own helpers while refusing externally modified DLLs.
- Test migration from the installed older helpers on a private game copy, including preservation of music/Home Points and refusal of unknown DLLs.

## HD enemy sprites — 2026-10-09

- Accept original, 2× and 4× PNGs in the same library; show resolution in previews and pending changes.
- Automatically compose an exact-build HD rendering patch with music and Home Points before deploying HD images, using an original-size catalog rather than resizing image pixels.
- Preserve battle sizes, indicators, shader outline width, atlas fit/centers, and portrait crops, including alternate textures and odd-sized canvases.
- Added Install/Remove HD Support, retained the renderer through Home Apply, and blocked removal/vanilla restore while HD textures remain.
- Verify all eight patch combinations, actual .NET Framework texture lookup and battle sizing, mixed and full-library HD updates, and exact restoration on private game copies. Native visual/performance acceptance remains pending.

## Enemy sprite tools — 2026-10-09

- Added Enemy Sprites with search, PNG preview, full archive extraction, edited-folder validation, single PNG replacement, and bulk apply.
- Discovered and round-trip tested all 273 installed 1.6.9 enemy PNG textures.
- Preserve sprite names, dimensions, timestamp table, and every unchanged image; reject corrupt PNGs, unsafe paths, mismatched folders, and outside edits.
- Keep verified installation-specific backups, apply archives atomically, recover interrupted state updates, and restore byte-identical originals without removing edited artwork.
- Sprite Apply/Restore are separate from executable/music/Home Points actions; native battle visual acceptance remains pending.

## Manager UI update — 2026-10-09

- Added All songs / Replaced / Not replaced filters and alphabetical or replacement-status sorting, with visible cue counts.
- Replacement status follows enabled, nonempty draft pools and the global music setting. Browser choices survive editing and page navigation.
- Simplified preview to a shared icon player with accessible labels, removing the duplicate pool Play button.
- Refreshed the sidebar, player, list borders, search label, and dark dropdown styling.

## Mod Manager 0.1.0-rc1 development preview — 2026-10-07

- Added .NET 8 WPF manager with all seven pages, pool editing, playback preview, and Steam detection.
- Discovered all 71 music cues and seven ambience cues from the verified executable and installed config/biome data.
- Added managed Vorbis library, content deduplication, missing-file repair, legacy-pool import, and configuration sharing without audio.
- Added visible operation progress and per-file import progress, including audio validation, filenames, and total percentage for regular and legacy imports.
- Updated the manager's NVorbis decoder to 1.0.0-rc.2 after reproducing a decoding hang with a user-owned normalized Ogg track; verified all 117 existing legacy tracks import. Added optional user-owned file regression coverage for import and preview.
- Added Preview Original for each music/ambience cue, playing the installed soundtrack through the shared player without importing or exporting audio.
- Generalized random pools to actual queued playback starts with duplicate suppression and exact-file bookmarks.
- Fixed original-music fallback, packed-file offsets, and untagged replacement loop bounds.
- Composed music with the newer safe Home Points implementation; journaled music/configuration/helpers and executable together.
- Recognized exact previous-release hashes for migration; protected unknown builds and linked paths.
- Added build/test tooling and native acceptance plan. Gameplay and real high-DPI acceptance remain pending; this is not a production release.

## Previous installer releases

## 0.1.0 — 2026-09-30

- Promoted the tested RC to the first release without changing either mod's runtime or patch logic.
- Home Points accepted by the player with no reported issues.
- Music log review found 22 battle, 2 boss, and 22 victory selections with no logged errors, skips, unrecognized cues, or invalid loop intervals. The player recalled no audio issues.
- Retains the RC's 101 passing automated checks and successful native main-menu startup.
- Updated installer labels, version metadata, and player documentation.
## 0.1.0-rc1

- Added a self-contained Windows x64 installer with Steam-library detection and folder browsing.
- Preserved the existing Random Music v0.2.3 custom OGG runtime and music hook behavior.
- Added on-demand Home Point array expansion, safe bounds, expanded-array preservation, and menu row-to-slot mapping.
- Preserved the original three-slot constructor and every game constant, including teleport destination enums.
- Added pristine external backups, deterministic rebuilds, verified atomic replacements, rollback journals, restoration, repair, and strict hash-based legacy cleanup.
- Migrates known legacy states only with a verified original. Unknown builds remain untouched.
- RC status: automated checks passed; native gameplay acceptance is pending.
