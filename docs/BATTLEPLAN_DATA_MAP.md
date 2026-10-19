# Battleplan campaign mapping

Source: the installed Battleplan `global-metadata.dat` (`Unit_Type`, `Country`, `CommandType`, and `Sub_Unit_Type` enums) and the local `v1.0.8` campaign's JSON structure. Enum numeric values were read from field default data, rather than inferred from the order of names in the string table. The metadata table layout was cross-checked against [Il2CppDumper's source](https://github.com/Perfare/Il2CppDumper/blob/master/Il2CppDumper/Il2Cpp/MetadataClass.cs). This mapping is specific to the inspected game build.

## Game-provided fields

| Save field | Meaning in the editor | Confidence |
| --- | --- | --- |
| `Units` / `ChildGroups` | Nested campaign order of battle | Directly observed in save |
| `ID` | Unique game unit GUID | Directly observed; 79/79 unique in sample |
| `OriginalName` | Game-generated name | Directly observed |
| `UserTypedName` | Player-entered name override; the editor writes this field | Directly observed |
| `Type` | Numeric `Unit_Type` enum | Matched to installed game metadata |
| `SubType` | Numeric `Sub_Unit_Type` enum; all sampled units use 0 (`None`) | Matched to game metadata and sample |
| `Country` | Numeric country enum; sample units use 2 (`US`) | Matched to game metadata |
| `Organizational` | Game flag | Directly observed; exact gameplay effect unverified |
| `NumUnits`, `StrengthPercentage`, `AttachedOfficers` | Unit details | Directly observed |
| `LocalPatchPath` | Patch image path | Directly observed; not used to infer branch |
| `VisibleDescription`, `ExtraInfo` | Empty for all 79 sampled units | Directly observed |

The campaign does **not** contain a separate equipment ID for these sampled units. `Type` distinguishes game roles such as anti-tank guns versus self-propelled anti-tank guns, and engineers versus mechanized engineers. A future game version or another save may add more data.

## Relevant game type IDs

| ID | Game enum | Default editor abbreviation |
| ---: | --- | --- |
| 0 | infantry | INF |
| 4 | engineers | ENG |
| 5 | antitank_guns | AT |
| 6 | artillery | ART |
| 11 | headquarters | command level assigned by the player |
| 13 | anti_air | manually assigned if attached |
| 18 | self_propelled_artillery | SPART |
| 19 | mechanized_engineers | ARMENG |
| 20 | self_propelled_antitank_guns | TD |

The full known ID list is in `BattleplanTypeCatalog.cs`. ID 16 is absent from the installed `Unit_Type` enum, so the editor reports it as unknown rather than inventing a label.

The current campaign has nine type-19 units named with `ENG` and nine type-20 units named with `TD`. The game type for 19 is **mechanized engineers**. These type labels help the player choose a branch, but do not determine the player's naming pattern.

## Editor-derived values

| Editor value | Derivation |
| --- | --- |
| Parent and depth | Position in `ChildGroups` |
| HQ tier and branch | Player-assigned in the editor catalog, keyed to game unit ID |
| Command number | Sequential from a player scheme, or explicitly assigned to a unit |
| Child slot | Suggested from game type and `ChildGroups` order; editable in the preview |
| Naming pattern | Player-entered or inferred from user-entered example names; editable |

Battleplan's `CommandType` enum has Battalion=0, Regiment=1, Division=2, Corps=3, Army=4, Company=5, Platoon=6, Squad=7. The sampled campaign unit nodes do **not** serialize a `CommandType` field, so the editor does not claim an exact game-provided command level. The active naming workflow does not infer HQ tier from `OriginalName`. The old regiment planner remains in source for reference but is hidden from the UI.
