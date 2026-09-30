# Changelog

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
