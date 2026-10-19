# Aide-de-Camp for Battleplan

Windows campaign editor for Battleplan. It borrows the useful Aide-de-Camp layout: a unit hierarchy on the left, unit details and mass naming on the right. The Battleplan loader and field mapping are separate from the Grand Tactician editor. The default save folder is resolved from the current Windows account.

Documentation: [Quick start](docs/QUICK_START.md) · [User guide](docs/USER_GUIDE.md) · [Developer guide](docs/DEVELOPER_GUIDE.md) · [Battleplan data map](docs/BATTLEPLAN_DATA_MAP.md) · [Release notes](docs/RELEASE_NOTES.md).

## Run

For most players, download the Windows ZIP from [Releases](https://github.com/Rook-US/Aide-de-Camp-Battleplan-2026-/releases), extract it, and run `Battleplan ADC.exe`. The release includes its .NET runtime. Windows may show a first-run warning for this unsigned community app.

To build from source, use Windows with the .NET 9 Desktop SDK: `dotnet build BattleplanADC.csproj -c Release -o bin\documented`, then run `bin\documented\Battleplan ADC.exe`. The campaign picker finds `.camp` files in the current Windows account's `AppData\LocalLow\Foolish Mortals\Battleplan` folder. A sole campaign opens automatically. **Browse .camp** can open a campaign elsewhere. Build output and personal campaign files are excluded from this repository.

## Renaming

The default **Whole army** screen follows preview, edit, apply. Set command levels in the left army list when needed. **One regiment** shows and renames the selected regiment's child units. **One unit** edits a single name. Format rules stay inside the optional **Change the name format** sections; **More tools** shows patterns, filters, game data, and find/replace.

For battalions, open **One regiment**. Click a regiment (or one of its battalions) in the tree, choose **Show regiment units**, review the regiment number and proposed child names, then **Apply checked names**. The initial format is `{slot}-{regiment} {suffix}`. The No., Suffix, format, and each proposed name are editable. The editor reads a regiment number only from a saved editor choice or a user-entered name, never the game's original name. Apply changes names in memory; **Save with backup** writes the campaign.

The save's top-level `1 Bn` is a zero-element organizational wrapper. The editor hides this wrapper and shows its child `8th Army` as the top OOB command; the wrapper remains intact in the save.

For duplicated formations, select the intended HQs in the army tree and set their command level and unit type under **Set command level or number**. The tree supports inline name editing by double-click or F2, and a manual number field. In **Whole army**, choose the whole army or a selected group, then **Preview names**. Divisions draw from 1–90 and regiments from 1–300. Existing numbered formations with an assigned level retain their numbers; repeated numbers get unused scattered numbers. The editor uses saved schemes or the most common pattern in player-entered names for that level and type; it does not infer from game-generated names. These assignments and retired numbers are saved in the editor catalog at preview time, so renaming a formation later cannot release an old number. The preview also updates child names under renumbered regiments, including children inside attached HQs, and suggests division-number replacements for numbered support names. Ambiguous duplicate names appear unchecked for review. Edit any proposed name or uncheck it before applying. **Apply checked names** changes memory; **Save with backup** writes the campaign.

For larger jobs, turn on **More tools** and use **Patterns and filters**. The army tree supports Ctrl-click toggling, Shift-click ranges, **Select visible**, **Select this group**, and **Clear** inside **Select several units**. Tree selections feed advanced batch actions. You can also select multiple rows directly in the advanced grid; a grid selection replaces the tree selection. Assign selected units a command tier and branch, and mark them for modification or exclusion. The editor never assigns an HQ tier from `OriginalName`; a Battleplan Headquarters can serve any organizational level. The **Duplicate names** filter exposes every unit currently sharing a name, including units already named by the player.

Choose a tier and branch, enter one or more consecutive user-name examples, then infer and review a naming pattern. You can edit the pattern directly. Tokens include `{n}` for a sequence, `{n:ordinal}` for `1st`/`2nd`, `{slot}` for position among selected siblings, and `{parentNumber}` for the nearest numbered ancestor. Set a command number manually for a specific unit, such as regiment `25` for `25IR`. The preview shows each proposed name and permits edits. The entire army can be renamed after assigning the desired tiers, branches, and schemes. The preview flags duplicate final names.

Classifications, rename policies, number overrides, and patterns are stored under the current account's `%LocalAppData%\Aide-de-Camp\Battleplan\catalogs`, keyed to the campaign path and game unit IDs. Older catalogs from this development folder are copied on first use. The catalog is not written into the game save. Only applied `UserTypedName` changes are saved to the `.camp` file.

Pattern inference uses example syntax; it cannot determine whether a generic HQ is a division, regiment, or battalion, or whether a number means a parent or child slot, without player classification and review. Multiple examples are checked for a consecutive sequence. When inference fails, the editor explains whether examples are missing or inconsistent and suggests tier/branch assignments, more consecutive examples, or an advanced pattern. Army preview reports unclassified HQs, missing schemes, and missing numbered ancestors with corrective steps. Irregular naming schemes require an edited pattern or preview names.

**Find / replace** handles other naming jobs with scope, name, type, country, unnamed/leaf filters, literal or regex matching, tokens `{n}`, `{name}`, `{original}`, `{type}`, `{parent}`, and a numbering step. Preview before applying. Changes remain in memory until **Save with backup**.

The editor reads the nested `Units` / `ChildGroups` tree and shows original and user names, type and subtype IDs, country, organizational flag, element count, strength, officers, and ID. Editing is limited to `UserTypedName`; `OriginalName`, IDs, and other game fields stay intact. Saving creates a timestamped `.bak` beside the campaign and refuses to overwrite a file that changed after opening. Close Battleplan before saving.

The included copied-save check covers a local `v1.0.8` campaign with 79 units: load, catalog persistence, duplicate-name handling, pattern inference, rename, reopen, backup, preservation of other JSON data, and external-change detection. Acceptance and display inside Battleplan still need an in-game check. Other save versions need validation before editing.

See [Battleplan data map](docs/BATTLEPLAN_DATA_MAP.md) for game-provided IDs, the editor's inferences, and fields that are still unknown.

The **Data map** tab shows which game type IDs occur in the open campaign, their game labels, and the editor's default naming suffixes.
