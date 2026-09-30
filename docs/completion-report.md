# RC1 delivery report

## Installation audit and cleanup

Full before/after inventories: `steam-folder-before.csv`, `steam-folder-after.csv`. They contain filenames, relative paths, sizes, classifications, and hashes for executables, libraries, scripts, and artifacts. Content and OGG assets were inventoried but not unnecessarily hashed. Unknown files were retained.

67 exact legacy files were archived and removed. `removed-legacy-files.csv` lists them individually. These include the failed payload, prepatch EXE backup, original legacy backups, old PowerShell/BAT installers, source Patcher.cs, manifests, Home Points README/markers/log, and the two legacy Mono.Cecil tool trees. The README was initially UNKNOWN and was positively identified by reading its Home Points installer content before adding its exact hash to the allowlist.

Archive and pre-deletion cleanup report:
`C:\Users\umtsq\AppData\Local\CrystalProjectModInstaller\legacy\20260930-024803-729295a58c5e411f81f19f448d06c9f8`

The Steam folder now contains existing game/unknown files, the preserved OGG collection, and the two required runtime DLLs. No installer/development source, legacy installer folders, backup EXEs, or patch logs remain there. Crystal Edit and all other unknown files remain untouched.

## Current executable

- Assembly version: **1.6.9.0**.
- State: **Random Music + Unlimited Home Points RC1**.
- SHA-256: `f6fb1be24074cd6a53abcafe0233b22da8a763fc1678b92c9a68d158067f5479`.
- Canonical backup: `%LOCALAPPDATA%\CrystalProjectModInstaller\backups\36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6\Crystal Project.exe`.

## Architecture and findings

`implementation.md` documents the complete traced Home Point paths, storage/serialization changes, menu mapping, OFF behavior, bounds, actual crash cause, music provenance, transaction/recovery model, and constraints. The old crash was caused by an unrelated teleport enum constant changed from 3 to 255, not by an empty Home Point array alone. RC1 retains all original constants and the original three-entry constructor.

Music v0.2.3 source/runtime behavior is preserved. Installation always rebuilds from the hash-verified pristine executable, applies the selected independent patches, validates the written assembly, and replaces files transactionally. The WinForms GUI is self-contained .NET 8 x64 and documents its Administrator-at-launch strategy. Source uses Git outside Steam.

Supported original and generated hashes are in `output-hashes.json`. Migration recognizes the three supplied legacy hashes ending `138e7`, `a38f3`, and `0d79` only when a verified original exists. No failed/modified executable is used as a release base.

## Validation

**101 automated assertions passed:** 32 installer checks, 4 additional running-process/read-only checks, 65 runtime/BSON/menu checks. Full outputs: `installer-tests.txt`, `failure-extra-tests.txt`, `runtime-tests.txt`.

Coverage includes all 16 pairwise configurations; exact vanilla restoration; deterministic outputs; unchanged unrelated methods and every constant; unknown/corrupt/missing backup rejection; exclusive file locks; read-only refusal; running-process refusal; interrupted staging and commit rollback; archive-first strict cleanup; safe 0â€“3/null array repair; expansion to 40; actual BSON serialization; gap mapping/reuse; real menu enumeration and scrolling; patched input/confirmation methods JIT compilation.

Native combined EXE reached the 1.6.9 title screen and main menu without the TeleportPoints error. No user save was loaded or edited. The initial Steam-relayed launches stalled before exposing a window; direct startup with the normal Steam app-ID environment succeeded. See `native-test-checklist.md` for remaining player tests, including Steam Play relaunch, live warps, real save/reload, and encounter audio.

Expanded saves are not safely readable by unpatched vanilla. This material limitation is documented and shown before disabling Home Points. No destructive save downgrade is attempted.

## Deliverables

- Repository: `C:\Games\CrystalProject-ModPack`.
- Installer: `artifacts\release\v0.1.0-rc1\CrystalProjectModInstaller.exe`.
- Distribution: `artifacts\CrystalProjectModInstaller-v0.1.0-rc1-win-x64.zip`.
- Package contains only our installer, README, changelog, and third-party notices. No game executable, original game DLL, music, save, backup, or proprietary decompilation is distributed.
- `tools/build.ps1` is a developer convenience only; players use the GUI.

Git milestones are recorded in the repository history. Build outputs, SDK/tools, local decompilation, test fixtures containing game bytes, and diagnostics remain under ignored `artifacts/` or bin/obj directories.
