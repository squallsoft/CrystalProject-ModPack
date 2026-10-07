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

WPF pages were rendered and visually inspected at 1200×800 and minimum 880×620 layout, with an additional 144-DPI rendering. Populated Music used synthetic tracks, a long Unicode filename, and a missing-file row. Readability, button contrast, wrapping, pool scrolling and minimum-window access were corrected. These snapshots are private `artifacts/ui-qa-*` files. They are **not** evidence of real Windows per-monitor DPI changes, keyboard interaction, or audible gameplay.

The portable ZIP includes only the manager EXE, README, changelog, project license, notices, audio dependency licenses, cue metadata report and native checklist. It contains no game executable/DLLs, audio or saves. Build output and ZIP are ignored by Git. The legacy installer README and original output hashes were archived under docs to distinguish release history from new manager outputs.

The Steam executable remains SHA-256 `f6fb1be24074cd6a53abcafe0233b22da8a763fc1678b92c9a68d158067f5479`, the same combined installer build observed at audit start. Its pristine external backup remains `36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6`. Live deployment was not attempted while the game was running.

Outstanding: audible area/story/title/atlas behavior, transitions and loop boundaries, full restart/save/load, real warps and multiple native saves, actual high-DPI and alternate Steam/UAC acceptance, and exhaustive world-script occurrence mapping. See manager-rc1-native.md. WAV/MP3/FLAC import, transcoding, original soundtrack preview, finer area categories and automatic deployed-track garbage collection are absent from this preview.
