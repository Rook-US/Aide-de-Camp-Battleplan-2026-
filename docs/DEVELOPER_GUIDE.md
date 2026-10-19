# Developer guide

## Build and checks

This is a C# WPF app targeting `net9.0-windows`. From the project directory:

```powershell
dotnet build BattleplanADC.csproj -c Release -o bin\documented
dotnet run --project tests\CampaignCheck -c Release -- 'C:\path\to\a\copied\campaign.camp'
dotnet run --project tests\UiSnapshot -c Release -- 'artifacts\ui-check-documented' 'C:\path\to\a\known\campaign.camp'
```

`CampaignCheck` copies the supplied campaign to a temporary path, exercises parsing, naming, save and backup, and deletes its test copy. `UiSnapshot` renders WPF screens, checks selection and apply flows in memory, and writes PNGs to the supplied directory. Pass a known campaign snapshot as its optional second argument when the live save has changed. It sets `BATTLEPLAN_ADC_CATALOG_DIR` to isolate its editor catalog from the player's profile. The UI harness references `bin\documented\Battleplan ADC.dll`; rebuild the app first.

Do not test writes against a live campaign. The app itself detects a running `Battleplan` process before saving; the service-level `Campaign.Save` method expects its caller to enforce that UI rule.

## Source map

| File | Responsibility |
| --- | --- |
| `Campaign.cs` | JSON save parsing, `Unit` tree, hidden structural root, optimistic disk hash, temporary write, backup and replace. |
| `BattleplanTypeCatalog.cs` | Installed-build numeric type and country labels; unknown IDs remain unknown. |
| `NamingCatalog.cs` | Per-campaign sidecar choices, rules, reservations, pattern inference and advanced rule preview. |
| `ScatteredNumberService.cs` | Division/regiment number retention and allocation from persistent reservations. |
| `BattalionNamingService.cs` | Immediate-child slot, branch, suffix, and format suggestions for One regiment. |
| `MassOobNamingService.cs` | Whole-army plan, nested regiment cascade, division support suggestions, unresolved duplicates. |
| `OobNamingService.cs` | Older hidden regiment planner; its final-name validator also serves Find / replace. |
| `MainWindow.xaml` | Visible UI and optional tool tabs. |
| `MainWindow.xaml.cs` | Load/save, tree display, one-unit editor, Find / replace, older hidden planner. |
| `MainWindow.OobSelection.cs` | Ctrl/Shift multi-selection and batch selection in the tree. |
| `MainWindow.OobEdit.cs` | Tree classification, manual numbers, inline name editing. |
| `MainWindow.Battalions.cs` | One-regiment preview/apply state and validation. |
| `MainWindow.MassOob.cs` | Whole-army preview/apply state and validation. |
| `MainWindow.Catalog.cs` | Advanced filters, policies, pattern creation, rule preview/apply. |
| `MainWindow.Ergonomics.cs` | More tools progressive disclosure. |

## Data ownership and lifecycle

`Campaign.Load` parses the full save as `JsonNode` and builds `Unit` wrappers around the original `JsonObject` nodes. `Unit.Name` shows `UserTypedName` when present, otherwise `OriginalName`. Code that applies a rename sets `Unit.UserTypedName` only. The intact JSON tree is serialized on save, preserving unmodeled game fields. The parser requires nonempty, unique game unit IDs; `ChildGroups` supplies hierarchy and order.

The sampled save has a zero-element organizational root. `Campaign.VisibleRoots` omits it from the tree; `Campaign.EditableUnits` excludes it from current mass rename. It remains in the serialized save. Avoid treating a displayed name or OOB path as a durable identity: IDs are the catalog keys.

`NamingCatalog` is stored under `%LocalAppData%\Aide-de-Camp\Battleplan\catalogs`. On first use, the app copies a matching catalog from the older development layout when present. The catalog filename combines the campaign base name with a hash of its normalized full path. It stores user-assigned tier, branch, rename policy, optional command number, rule patterns, and a permanent reservation list. Previewing Whole army can allocate numbers and save this sidecar before applying names. This is intentional: later previews must not reuse a retired number. The sidecar is not part of the `.camp` file. Moving or renaming a campaign path creates a distinct catalog unless migrated deliberately.

Name edits are staged in memory and set `_dirty`. **Save with backup** calls `Campaign.Save`: compare the current file's SHA-256 to the loaded bytes; write a complete temporary JSON file; parse it; replace the original while creating a timestamped `.bak`; refresh the source hash. The UI blocks save while a process named `Battleplan` is running. Catalog changes are saved independently and do not mark the game campaign dirty.

## Naming behavior

The save's numeric `Type` helps distinguish infantry, artillery, anti-tank, engineers, and other branches. `Type == 11` means generic Headquarters, not a particular command tier. The inspected unit records do not contain a serialized `CommandType`. HQ levels are player-assigned in the sidecar; do not infer them from `OriginalName` or assume the default army layout.

`ScatteredNumberService.Assign` surveys all classified units at a tier even when preview scope is one subtree. It first honors existing editor choices and player-entered leading numbers, then picks from unused Division 1–90 or Regiment 1–300. Randomness affects the first assignment only; reserved choices remain stable thereafter. A manual override uses the same reservation ledger. `OriginalName` never claims a number.

`MassOobNamingService.Build` generates a review plan. It preserves an existing user name when its assigned number is valid. For changed regiments, it traverses combat descendants through attached HQs, stopping at a separately classified Division or Regiment. For Division support descendants with a player-entered leading number, it proposes replacing that number when the division changed or the child remains duplicated. Saved tier/branch rules can rename further descendants. Remaining duplicates are emitted as unchecked rows without an invented name.

The apply handlers commit grid edits before validation. They reject stale previews, blank checked names, and collisions with unchanged or other proposed names. For Division and Regiment rows, Whole army also requires the proposed leading number to match its reservation; changing the number requires setting it on the tree and previewing again. Applying changes `UserTypedName` in memory; saving the campaign is a separate step.

`NamingPatterns` supports `{n}`, `{n:ordinal}`, `{slot}`, and `{parentNumber}` for advanced rules. One regiment uses `{slot}`, `{regiment}`, and `{suffix}`. Unknown tokens fail preview. Inference uses only player-entered examples and reports inconsistent sequences. Avoid broadening inference to game-generated names: the player may be using a custom hierarchy unrelated to the game's default naming.

## UI conventions

Keep the common flow in **Whole army**, **One regiment**, and **One unit**. Put token syntax, filters, and game diagnostics behind the existing expanders and **More tools**. Keep names editable in preview tables. Use the shared colors and control styles in `MainWindow.xaml` so text remains readable against light and dark backgrounds. Whenever you change XAML `x:Name` controls, update their code-behind and `tests/UiSnapshot` references together. Check placeholder validation after changing picker labels.

## Known limits

- The mapping is based on the inspected v1.0.8 game metadata and sampled campaign; another build may require remapping and save validation.
- The sample has no separate per-unit equipment ID, so role-specific suffixes use known game `Type` IDs and remain editable.
- Some nested support names can only receive a leading-number suggestion, which requires human review.
- One regiment enumerates immediate children; Whole army handles nested attached HQ descendants for regiment cascades.
- The older OOB planner is hidden but retained in source. Its heuristic discovery uses game-generated names and is not the current recommended path.

See [Battleplan data map](BATTLEPLAN_DATA_MAP.md) for the evidence behind game-provided and editor-assigned fields.
