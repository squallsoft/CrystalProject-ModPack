# Manager 0.1.0-rc1 native acceptance

**Pending. This development build is not a completed release candidate.** Automated checks do not establish audible gameplay or real save/restart behavior. The Steam installation was left unchanged because the game was running.

Use your own test music and an expendable save. Keep expanded saves separate from vanilla saves. Do not load a save with more than three Home Points after restoring vanilla. No user saves are rewritten by the manager.

## Installation and composition

- [ ] Close game, start manager through its EXE/UAC prompt, verify detection and supported status.
- [ ] Import existing legacy folder pools; verify assignments match battle/boss/victory and source folders are untouched.
- [ ] Test vanilla, music only, Home Points only, and both in actual gameplay.
- [ ] Change each option separately; the other selected feature still works.
- [ ] Repair rebuilds deployed settings while preserving unapplied draft changes.
- [ ] Restore exact vanilla with a vanilla-compatible save. Imported music remains.
- [ ] Alternate Steam library, manual Browse, ordinary Windows account/UAC, game launch through Steam.
- [ ] Unknown executable/update refused; no replacement with an older backup.
- [ ] Legacy cleanup preserves changed and unknown files; archive/report readable.

## Music matrix

For every row test no replacement, one track, and three tracks. Listen through a loop boundary; repeat entries; check no immediate repeats when multiple files are available.

| Context | Evidence to record |
|---|---|
| Spawning Meadows / Proving Meadows | Both share cue 32; game data does not name a distinct Proving Grounds cue |
| Other overworld areas | Cue mapping and fade; two areas sharing a cue keep continuous playback |
| Town | Choose a town from the discovered area registry; record actual cue |
| Dungeon | Choose an indoor dungeon and record actual cue |
| Normal battle | Several battles; verify battle → victory → field bookmark restores exact file and position |
| Boss | Multiple supported bosses and boss triumph |
| Event/story | Narration and scripted cue transitions; one-shot plays once and continues correctly |
| Title / credits | Launch/restart and return-to-title behavior |
| Rival / special / endgame | Cue-specific pool remains separate from ordinary battle |
| Ambience | Weather pool uses separate ambience manager; does not replace unrelated BGM |

- [ ] Save/load, full game restart, area transitions and warp transitions.
- [ ] Game's built-in Random Music ON/OFF: with ON, replacements follow whichever original cue the game chooses. Verify and document expected behavior.
- [ ] Soundtrack atlas playback/bookmarks and metadata display.
- [ ] Missing/corrupt runtime music falls back safely or reports clearly; no stuck transition.
- [ ] Real embedded loop tags and untagged full-file looping at several sample rates.

## Preview and UI

- [ ] Play, pause, resume, stop, seek, previous, next, volume, natural end.
- [ ] Preview Original on area, battle, title, victory, and ambience cues; pause/seek/stop/replay and volume work. Replacement pools stay unchanged. Switch back to a replacement preview and verify Previous/Next work again.
- [ ] One-track and large pools, missing/invalid file, Unicode and long filenames.
- [ ] Preview and editor changes do not modify deployed game files until Apply.
- [ ] All seven pages at 100%, 150%, 200% Windows DPI, small screen, resized window.
- [ ] Keyboard traversal, focus visibility, search/filter empty states, loading, disabled actions and error dialogs.
- [ ] Pool editing remains accessible at minimum window size.

## Home Points

- [ ] Game launches; no TeleportPoints database error.
- [ ] Enhanced Home Point OFF retains ordinary behavior.
- [ ] ON: create #1, #2, #3, #4, #5, then additional points.
- [ ] Scroll; warp to first/middle/last; replace/swap beyond third slot.
- [ ] Save, exit fully, restart, reload; every extra destination remains usable.
- [ ] Switch multiple saves; load old vanilla-compatible save; no cross-save retained destinations.

Record manager version, executable hashes from Advanced Diagnostics, save test type, cue IDs, pass/fail and relevant logs. Do not publish executables, saves or music with the report.
