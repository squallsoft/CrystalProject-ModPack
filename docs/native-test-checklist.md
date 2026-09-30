# RC acceptance checklist

Status: v0.1.0 released on 2026-09-30 following player acceptance. The checklist below preserves the original RC test plan; individual boxes are not retroactively marked when the player did not enumerate them.

Completed: native combined 1.6.9 title screen and main menu, with no database error. Installer window inspected visually; auto-detected Steam path, legacy failed-build status, controls, and hash display are legible.

The initial Steam-relayed smoke-test launches did not expose a window. A direct launch with the normal Steam app-ID environment reached the main menu. Steam-library Play relaunch should be rechecked on this machine. No Steam configuration or security settings were changed.

Original player test plan (player subsequently reported Home Points had no issues and recalled no music issues):

- [ ] Enhanced Home Point OFF: ordinary set/warp behavior.
- [ ] Enhanced Home Point ON: create #1, #2, #3, #4, #5, and several more; New Home Point Slot continues appearing.
- [ ] Scroll to every destination and warp to early, middle, and last entries.
- [ ] Replace/swap points beyond slot three and verify duplicates behave normally.
- [ ] Save more than three points; fully quit; restart and load; verify names, locations, and successful warps.
- [ ] Switch between expanded and old saves without retained points from another save.
- [ ] Normal encounter, boss encounter, and victory choose appropriate custom OGG pools; verify looping and fallback with the game's built-in Random Music OFF.
- [ ] Repeat encounter tests with both mods enabled.
- [ ] Player-facing install/change/restore/repair UI on a normal user account with UAC.
- [ ] Alternate Steam library installation and Browse.

Do not disable Home Points and load an expanded save into vanilla. Keep an expanded save backed up and use a separate vanilla-compatible save for restore testing. The installer does not edit saves.
