# Aide-de-Camp for Battleplan: user guide

## What the editor changes

The editor reads a Battleplan `.camp` campaign, displays its unit tree, and changes the player name override (`UserTypedName`) for chosen units. It leaves the game's original names, IDs, combat fields, and other save data in place. Name changes stay in memory until **Save with backup**. Command levels, rename preferences, patterns, and reserved numbers live in a separate editor catalog under `%LocalAppData%\Aide-de-Camp\Battleplan\catalogs`; these choices can be saved when you preview even if you do not save the campaign.

Close Battleplan before saving. The editor checks that the campaign has not changed on disk since opening it, writes a timestamped `.bak` copy beside the campaign, and refuses to overwrite a newer save. Keep your own backup as well when working with a new game version.

## Open and navigate

1. Download the Windows ZIP from the GitHub Releases page, extract it, and run `Battleplan ADC.exe`. The ZIP includes the .NET runtime. If using the source repository instead, build it with `dotnet build BattleplanADC.csproj -c Release -o bin\documented` and launch `bin\documented\Battleplan ADC.exe`.
2. Select a campaign in the top picker and choose **Open**, or use **Browse .camp**. When there is exactly one campaign in Battleplan's local save folder, the editor opens it automatically.
3. Use the army tree on the left. Search finds displayed names, game unit IDs, and officer names. **Expand all** and **Collapse all** control the tree. Click a unit to see its details in **One unit**.
4. Double click a unit, press F2, or choose **Edit name (F2)** to edit a name in the tree. Enter commits; Escape cancels. **Save with backup** writes applied names to the campaign.

The sample campaign has a zero-element organizational wrapper that appears as `1 Bn` in save data. The editor hides that wrapper and begins the visible tree at its child army HQ. The wrapper remains in the saved file.

## Rename an entire army

**Whole army** is the starting screen. Battleplan's generic Headquarters game type does not identify Army, Division, Regiment, or another command level, so assign levels for the HQs you want to rename:

1. Select an HQ or several units in the army tree. Open **Set command level or number** on the left. Choose a **Command level** and **Unit type**, then choose **Set level + type**. These choices are saved in the editor catalog.
2. Choose **Entire army** or **Selected group and its units** under **Rename**.
3. Choose **Preview names**. The table shows current and suggested names. Checked rows will be applied. Edit suggested names directly or uncheck any row. Unresolved duplicates appear unchecked for manual review.
4. Choose **Apply checked names**, then **Save with backup**.

Divisions draw unused numbers from 1–90; regiments draw from 1–300. The editor reserves a new number as soon as you preview, so it will not be reused later. Existing player-entered numbers are retained when they are unique. When a regiment number changes, child names are suggested using their role and position, including units inside attached HQs. Some attached names only receive a suggested division number replacement; review those rows carefully. The editor will not silently decide an ambiguous HQ level or invent a name for an unresolved duplicate.

For a specific division or regiment number, select one unit and use **Number** → **Use this number** in **Set command level or number**. A number already reserved for another unit at that level cannot be assigned. Older reservations remain unavailable even if you later change a unit's name or number.

## Rename one regiment's units

1. Choose **One regiment**. Select the regiment HQ in the tree, or one of its immediate children.
2. Choose **Show regiment units**. The editor reads the regiment number from its saved choice or player-entered name. Enter a number if the field is empty, then choose **Update names**.
3. Review each row. You can change its **No.**, **Suffix**, and suggested name. Uncheck a unit to skip it.
4. Choose **Update names** again after changing the regiment number, slot, suffix, or format. Choose **Apply checked names**, then **Save with backup**.

The default format is `{slot}-{regiment} {suffix}`, giving names like `1-25 INF`, `5-25 TD`, and `6-25 ARMENG`. The starting slot suggestions are infantry 1–3, artillery 4, anti-tank/tank destroyers 5, engineers 6, and additional units 7 onward. The game's unit type supplies the suffix suggestion; every result is editable. **Change the name format** exposes the token pattern if you want a different style.

This screen works on immediate child units. **Whole army** also follows nested attached HQs when cascading a changed regiment number.

## Select several units

Ctrl-click toggles individual units. Shift-click selects a visible range from the last anchor. **Select several units** contains **Select visible**, **Select this group**, and **Clear**. Multi-selection can set a command level and unit type; a manual command number applies to one unit at a time. Selecting rows in the advanced grid replaces the tree selection for advanced batch actions.

## More tools

Turn on **More tools** in the top bar to reveal:

- **Patterns and filters**: filter headquarters, duplicate names, or unclassified units; batch set levels and branches; include or exclude units from renaming; infer a naming pattern from player-entered examples; save a rule for a level and branch; preview and apply rule-based names.
- **Game data**: inspect game type IDs present in the campaign and see which fields are game-provided versus editor-assigned.
- **Find / replace**: filter by scope, name, numeric game type, country, unnamed units, or leaves; use literal or regular-expression replacement; insert `{n}`, `{name}`, `{original}`, `{type}`, or `{parent}`. Preview and resolve conflicts before applying.

Pattern inference recognizes a leading number (`25IR` → `{n}IR`), an ordinal (`1st Infantry Division` → `{n:ordinal} Infantry Division`), or a child/parent pair (`1-25 INF` → `{slot}-{parentNumber} INF`). Multiple examples must be consecutive and consistently formatted. If inference reports a missing command level, branch, or numbered parent, assign those choices or edit the proposed pattern yourself. The game's `OriginalName` is not used as evidence for your personal scheme.

The older **OOB naming** planner remains hidden in the source for reference. Use **Whole army** for current mass naming.

## If a preview is empty or blocked

| What you see | What to do |
| --- | --- |
| No formation names suggested | Assign Division or Regiment command levels to the intended HQs, then preview again. |
| A row is unchecked with an existing duplicate | Enter a unique suggested name or classify the unit and preview again. |
| A numbered parent is missing | Assign the parent HQ's level and number or choose a pattern without `{parentNumber}`. |
| A number is already reserved | Choose another number; prior reservations remain occupied. |
| Apply asks for another preview | The scope, format, source name, slot, or suffix changed. Preview again before applying. |
| Save says the file changed | Reopen the campaign to avoid overwriting a newer game save. |
| The game is running | Close Battleplan, then save from the editor. |

The current mapping was checked against a local Battleplan v1.0.8 campaign. See [Battleplan data map](BATTLEPLAN_DATA_MAP.md) for known IDs and limits. Check edited names inside the game after saving, especially with another game version.
