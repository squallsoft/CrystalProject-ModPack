# v0.1.0 release acceptance

Date: 2026-09-30.

The player reported testing of both mods complete, stated Home Points had no issues whatsoever, recalled no music issues, and approved release.

Music log review covered 2026-09-29 22:55 through 2026-09-30 00:06 local time: 22 battle selections, 2 boss selections, 22 victory selections, 37 distinct tracks. No errors, skipped selections, empty/missing pools, unrecognized cues, or invalid loop intervals were logged. DEBOUNCE events match the existing repeated-call suppression. The game error log had no new entries after the prior failed build. Logs establish configuration/selection success rather than independently proving audible quality.

The first release changes version metadata, labels, and documentation only. Runtime DLLs must match the accepted RC byte-for-byte. Patch logic, cleanup allowlist, and test logic are unchanged. The existing 101 automated checks remain applicable; the release build is separately compiled and its archive inspected. User acceptance does not establish testing of every alternate Steam library or UAC configuration.

Distribution: artifacts/CrystalProjectModInstaller-v0.1.0-win-x64.zip
Installer: artifacts/release/v0.1.0/CrystalProjectModInstaller.exe

No game binaries, music, saves, backups, or local decompiled game source are included. The user's tested game installation is unchanged. This is a locally prepared release package; it has not been uploaded or published to a hosting service.
