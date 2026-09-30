# Implementation and audit findings

## Baseline and crash root cause

The initial installed EXE was the failed hash `65cfbba9ed7af1f0c7db110f85719fa695a92cc088873a157fa95d92fa720d79`. Its predecessor backup was `1768ff8268fc09f817c8109a43e683411f0f647bb7d74928c00f1a102a5a38f3`. Both legacy music backup locations contained the supported pristine hash. These were recalculated locally, not inferred from filenames.

The failed EXE's **TeleportPoint.SalmonSprintReception constant is 255**, while the pristine EXE's is **3**. `Sang.SangData.DataReferenceUtil.Validate` computes the required teleport database size from the maximum enum value plus one. This changed constant requires at least 256 entries and explains the reported TeleportPoints initialization error. That database is distinct from saved Home Points. Merely retaining three Home Points would not repair this error.

The new implementation rebuilds from the pristine executable, never edits constant metadata, and verifies **every constant in every type** after writing. The particular 255-enum corruption therefore cannot be produced by this patch. This does not guarantee startup against independently damaged game content.

## Actual Home Point call paths

- `Sang.PartyData.HomePointCollection`: constructor allocates three `HomePointData` structs; `_homePoints` has `[JsonProperty("HomePoints")]`.
- Parameterless `Sanitize` assumes length three and copies the old array into a new three-slot array. Expanded arrays can throw during this copy. Replaced with a helper that repairs null/short arrays to a three-slot minimum and preserves longer arrays.
- `IsSet`, `Get`, and `Set` contain an off-by-one check (`slot <= Length`). Changed only those verified upper-bound branches to strict `< Length`.
- `Set` calls `BeforeSet` first. Only `slot == Length`, with `GameplayFlags.MultiHomePoint` true, allocates length+1. New entries have ID -1 and empty names. It does not allocate sparse requested indices. The existing Set body still writes the selected point.
- The constructor remains byte-for-byte semantically unchanged. `Clear` resets the storage to three entries before the original clearing loop, avoiding cross-save capacity/state retention. `Party.Sanitize` repairs a null HomePoints collection before calling its normal sanitizer.
- `Sanitize(id,name,coord)`, `IsAnySet`, and `IsHomePointSet` already iterate array length. Their game bodies are unchanged.
- `WindowHomePointSelect.RefreshContent` has two independent literal-three checks: enumeration and availability of the new slot. They call `VisibleLength` and `NewSlotLimit` respectively. The original vocabulary, duplicate disabling, labels, and menu reuse remain.
- `WindowHomePointSelect.UpdateInput` has a separate three-entry fallback loop; its bound is dynamic. Selected row indices are translated to actual array slots. `OnPopupConfirmationOK` receives the same translation, including the current-point swap slot. Holes therefore do not misdirect warps or replacements.
- `WindowHomePointMenu` already branches on `GameplayFlags.MultiHomePoint`, routing OFF to the vanilla single-point fields. Its set-before-save and `UserData.ConfirmSetHomePoint` paths require no patch. The assist option's `WindowAssistOptionsSelect.OnPopupConfirmationOK` migrates the existing single point to slot zero when enabled.
- Warp passes the integer slot through `MenuState.TeleportPointIndex` to `EntityAction`'s HomePoint case, which calls collection IsSet/Get. No byte conversion or three-slot limit exists on this path.
- `VoxelField` updates matching saved destinations through the length-aware overload of Sanitize.
- `CField.MAX_HOMEPOINTS` is **4**, an initial capacity for world entity pooling, not the saved three-point restriction. It remains unchanged.

`Menu.Items` is a List, `PoolPop` allocates another item when its stack is empty, and menu navigation/scrolling uses Items.Count. The constructor's three visible lines remain intentional. Tests execute the real RefreshContent and InputDown methods with 40 points; all 41 Set rows and all 40 Warp rows are reachable/enumerated.

All relevant indices are Int32. The new-slot threshold is Int32.MaxValue-1 to leave room for the extra menu row and checked length increment. CLR array limits and available memory impose earlier physical limits; no practical play limit such as 255 is introduced. This is not a promise to allocate billions of entries.

There is no separate vanilla delete-point menu. Replacement/swap behavior remains; invalid or removed entries are skipped and their free slots can be reused.

## Save behavior

`SaveDataManager` uses Newtonsoft JsonSerializer with BsonReader/BsonWriter inside its existing encoded stream and ObjectCreationHandling.Replace. `Load` calls `party.Setup()` before populating it. `Party.Setup` clears HomePoints; later Party.Sanitize repairs/preserves the loaded array. The array field's JSON attribute is unchanged. No serializer, save envelope, other save fields, or user save files are patched.

Tests use the installed Newtonsoft DLL and actual patched game collection, with real BSON round trips. They do not open user saves. A full game quit/relaunch and real save transition remain native acceptance checks.

**Downgrade limitation:** vanilla's sanitizer does not support arrays longer than three. Restoring the vanilla EXE cannot also make expanded saves backward-compatible without modifying saves. The installer leaves saves untouched and documents retaining the mod for expanded saves.

## Music provenance

Installed manifest identifies `0.2.3-nested-looppoint-fix-alpha`, not v0.1.4. The preserved source is `src/Mods/RandomMusic/LegacyPatcher.cs`. `src/Runtime/Music.cs` is extracted from its BuildRuntimeSource method using the supported executable's cue enum. Its runtime source and pool behavior are unchanged: normal battles, bosses, victories, nested LoopPoint metadata, fallback, and PvP exclusions.

Only `Sang.Field.FieldState.GetBattleCue` and `GetFanfareCue` receive the existing nullable-enum return hooks. Runtime compilation now happens during development; an installer user never runs a compiler. The DLL contains the mod author's reflection-based code, not game code. OGG files and legacy logs are not bundled. v0.1.4 and the v0.2.3 predecessor are migration signatures, not patch bases.

## Installer architecture

.NET 8 WinForms, self-contained win-x64, Mono.Cecil 0.11.6. The small net462 helper DLLs use the framework already required by Crystal Project. Steam registry paths, libraryfolders.vdf, appmanifest_1637730.acf, common drive library paths, and Browse are supported.

This RC uses the documented requireAdministrator-at-launch strategy. All persistent installer state is outside Steam and is keyed by installation path. The pristine backup is keyed by its original hash. Inspection distinguishes vanilla, known legacy, known generated output, unknown/update, and interrupted transactions. Supported generated hashes are computed from canonical bytes and embedded helpers, not accepted merely because a manifest claims them.

Apply verifies source and live identity, builds away from Steam, reloads with Cecil, checks references/branches/constants/unchanged methods, hashes the result, snapshots overwritten files, writes a durable journal, stages a transient sibling file for File.Replace, verifies each result, and commits the manifest. Failures restore prior files; interrupted transactions are recovered on the next Repair/Apply. Recovery stops on externally changed files or damaged recovery backups. Unused verified mod DLLs are removed only after a successful EXE commit. Access failures are logged. A named mutex serializes Apply operations; process-name checks refuse a running game.

Strict cleanup uses embedded relative-path plus SHA-256 pairs. It copies and verifies every eligible file into an external archive and writes a report **before deletion**. Unknown, changed, and reparse-point files remain. Only proven-empty artifact parent directories are removed. No wildcard deletion of game-folder backups occurs.

## Known release limitations

- Unsigned RC; no public-release certification.
- Full encounter audio, live warps, duplicate swapping, game-assisted saves/reloads, and alternate-library installations still need native acceptance.
- Administrative launch uses the elevated account's LocalAppData.
- A catastrophic power loss/storage failure is beyond what an application-level journal can guarantee; recovery evidence is retained rather than guessed.
- Expanded saves require the Home Points mod; no automatic destructive downgrade conversion is provided.
