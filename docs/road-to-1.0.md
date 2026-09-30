# Road to 1.0

Version 0.1.0 is a released, player-tested build. Calling a build 1.0 should mean the supported installation, customization, save, and recovery paths are dependable for other players—not simply that more features were added. The following is a proposed acceptance checklist, not a claim that these tasks are already complete.

## Already established

- Both mods work together on the supported Steam Windows 1.6.9.0 executable.
- Player acceptance reported no Home Point issues and no recalled music issues.
- Music log review covered normal battles, bosses, and victories without logged errors.
- 101 automated assertions cover storage/BSON/menu behavior, all configuration transitions, and several recovery failures.
- A native combined build reached the main menu.
- The installer uses a canonical external backup, refuses unknown hashes, supports restore/repair, and archives only proven legacy artifacts.
- A player-facing release ZIP, source repository, and setup guide are published.

## Recommended requirements before 1.0

### Music behaves predictably with ordinary user files

- [ ] Define and implement safe per-track defaults when loop tags are absent. Current v0.1.0 retains previous loop metadata in that case.
- [ ] Validate loop start/end together, reject invalid intervals, and make integer sample-versus-second handling unambiguous or explicitly configurable.
- [ ] Test untagged files, malformed tags, unreadable/corrupt files, unsupported codecs, 44.1/48 kHz audio, and tracks with different lengths.
- [ ] Ensure a failed selection leaves a known-good track state and produces an actionable diagnostic.
- [ ] Verify empty-category behavior and replacing/removing tracks, including previously used cues. Keep restart instructions clear.

### Independent installation and recovery testing

- [ ] Have at least one other player install from the published ZIP on a clean supported game installation.
- [ ] Exercise a non-default Steam library, paths with spaces/non-ASCII characters, and Browse.
- [ ] Test a standard Windows account through the actual UAC workflow, including a different administrator account and its backup location.
- [ ] Repeat install/change/restore/repair through the GUI and verify checkbox state accurately reflects the installed configuration.
- [ ] Test interrupted multi-file commits at different points, failed runtime-DLL writes, and externally modified files during recovery—not only an interrupted EXE replacement.
- [ ] Review concurrent Apply/Repair/Cleanup operations and stale staging-file handling.

### Save compatibility is explicit and reproducible

- [ ] Record a reproducible native test with >3 points: save, fully quit, relaunch, warp to early/middle/last entries, then switch to an older save.
- [ ] Document tested duplicate/swap behavior and Enhanced Home Point OFF behavior.
- [ ] Keep the prominent warning that expanded saves require the Home Points mod. Automatic save downgrade is not required for 1.0; silently deleting extra points is unacceptable.

### Release process is repeatable

- [ ] Build from a clean checkout and document the developer prerequisites without depending on this machine's private SDK folder or game binaries in Git.
- [ ] Automate package contents, licenses, checksums, version labels, and source-tag verification.
- [ ] Ensure build tooling copies the repository LICENSE as well as third-party notices; v0.1.0's published ZIP includes it, but the original build helper needs that step automated.
- [ ] Make the supported-game-version policy and Steam-update recovery instructions part of every release.
- [ ] Track acceptance results and unresolved issues, then run a short 1.0 release-candidate test period with no known blocking defects.

## Useful improvements that need not block 1.0

- Buttons to open the music folders and logs, plus category track counts and a file validator.
- A music setup wizard or preview player.
- Code signing to improve installer trust and reduce unsigned-app friction.
- More supported game versions, once explicitly audited and tested.

A reasonable next milestone is **0.2.0 for music input/loop hardening and setup diagnostics**, followed by **1.0-rc1 for independent installation and recovery testing**. The current release can remain available while that work proceeds.
