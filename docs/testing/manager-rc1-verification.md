# Development preview verification — 2026-10-07

This records automated and rendered-UI evidence for Mod Manager 0.1.0-rc1. **Native gameplay acceptance remains pending.** The previously accepted installer release does not automatically validate generalized music playback.

| Suite | Passing checks | Evidence |
|---|---:|---|
| Composition / installer | 32 | Four outputs; all 16 transitions; constants and unrelated methods unchanged; original backup; unknown hash, corrupt backup, locked-file refusal; interrupted transaction; strict cleanup |
| Failure guards | 4 | Running process in selected installation; read-only executable; files preserved |
| Manager / library / preview | 37 | 71+7 catalog; deduplication; source preservation; Unicode/long filenames; config import/export; unsupported/invalid/missing files; Locate; runtime staging; repair; nested rollback; play/pause/resume/stop/seek/near-end/replay/volume and track switches |
| Actual game Home Point types | 65 | Three-slot initialization; on-demand growth to 40; real BSON roundtrip; old/null arrays; multiple collections; actual menu enumeration/scroll; warp rows; patched methods JIT; teleport enum preserved |
| Actual game music types | 193 | All 78 cue metadata assignments; 100 non-repeating selections; delayed/unqueued suppression; actual Play duplicate suppression; exact-file bookmark restoration; empty/missing pool restoration; loops/offsets; installed MCache decoder; patched methods JIT |

**331 checks passed.** Run logs are under ignored `artifacts/`; reproduce using tools/test-manager.ps1 with your own supported pristine executable, installed dependencies and developer FFmpeg. Original synthetic tones are generated locally. No user saves were opened; no live game files were patched.

The added end-of-file preview regression exposed an NVorbis final-page seeking failure. Preview now represents exclusive EOF without indexing a nonexistent packet and falls back to decoding forward from an earlier seek point when a valid final page cannot be indexed. Near-end and replay regressions pass.

Following import feedback, regular and legacy imports now show an overall progress bar, current filename, file count, and decoding progress. Validation reports are throttled to one update per 200 ms while decoding on a background thread. Other operations show an indeterminate bar. The manager suite was rerun with an added bounded/monotonic/completion progress regression: **38 manager checks passed**, and the updated portable build was repackaged. Native import responsiveness acceptance remains pending.

The next native report identified a real decoder hang on legacy track 15, `Battle 2 (Chrono Trigger - Battle).normalized.ogg`. An isolated NVorbis 0.10.5 probe opened this file but stalled inside its first sample read. Pinning the manager to NVorbis **1.0.0-rc.2** resolved the stall: a complete isolated decode took about 0.35 seconds, and a read-only batch import of all **117** user-owned legacy files completed in about **20 seconds**. The source tracks were not edited or re-encoded. This dependency is a prerelease and remains subject to native acceptance. The game runtime's decoder was not changed.

The manager regression runner accepts an optional second argument containing a user-owned Ogg path. It checks prompt full import and source preservation, muted preview decoding/pause, near-end seeking, and EOF restart. No regression song is distributed. Example: `artifacts\dotnet\dotnet.exe run --project tests\Manager -c Release -- C:\Games\CrystalProject-ModPack "C:\path\to\reported.ogg"`.

The optional run using the reported track passed **42 manager checks**, including all four track-specific checks. The portable manager was rebuilt with the updated decoder. These checks are muted and do not establish audible gameplay acceptance.

Original soundtrack preview adds 11 synthetic checks for archive selection/bounds, read-only access, traversal/missing/version errors, muted MP3 play/pause/seek/stop/EOF replay, and preservation of the archive/library/assignments. The default manager suite now has **49 checks**. An optional third runner argument selects an installed game folder: all **78** original music/ambience cues resolve and decode an initial audio buffer, plus one aggregate coverage check. With both optional inputs, **132 manager checks passed**. No game audio was exported or committed. The original preview UI and audible playback remain subject to native user acceptance.

WPF pages were rendered and visually inspected at 1200×800 and minimum 880×620 layout, with an additional 144-DPI rendering. Populated Music used synthetic tracks, a long Unicode filename, and a missing-file row. Readability, button contrast, wrapping, pool scrolling and minimum-window access were corrected. These snapshots are private `artifacts/ui-qa-*` files. They are **not** evidence of real Windows per-monitor DPI changes, keyboard interaction, or audible gameplay.

The portable ZIP includes only the manager EXE, README, changelog, project license, notices, audio dependency licenses, cue metadata report and native checklist. It contains no game executable/DLLs, audio or saves. Build output and ZIP are ignored by Git. The legacy installer README and original output hashes were archived under docs to distinguish release history from new manager outputs.

The Steam executable remains SHA-256 `f6fb1be24074cd6a53abcafe0233b22da8a763fc1678b92c9a68d158067f5479`, the same combined installer build observed at audit start. Its pristine external backup remains `36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6`. Live deployment was not attempted while the game was running.

Outstanding: audible area/story/title/atlas behavior, transitions and loop boundaries, full restart/save/load, real warps and multiple native saves, actual high-DPI and alternate Steam/UAC acceptance, and exhaustive world-script occurrence mapping. See manager-rc1-native.md. WAV/MP3/FLAC import, transcoding, finer area categories and automatic deployed-track garbage collection are absent from this preview.
