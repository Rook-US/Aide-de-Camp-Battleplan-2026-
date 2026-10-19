# Battleplan Aide-de-Camp — quick start

An unofficial Windows tool for renaming units in a Battleplan campaign.

## Start

1. Extract the entire ZIP to a folder you can keep. Run **Battleplan ADC.exe**. This Windows x64 package includes the .NET runtime; there is no installer.
2. Choose your campaign from the top picker and click **Open**. The app looks in the current Windows account's Battleplan save folder automatically. Use **Browse .camp** if your save is elsewhere.
3. In **Whole army**, select a command in the tree. If the app cannot tell what level its HQ represents, open **Set command level or number** and assign it, such as Division or Regiment.
4. Click **Preview names**. Edit any suggestion in the table or uncheck units you want to leave alone. Click **Apply checked names**.
5. Close Battleplan, then click **Save with backup**. The app creates a timestamped `.bak` beside the campaign before writing it. Check the names in game.

For one regiment's battalions, use **One regiment** → **Show regiment units**. Double click a unit in the left tree or press F2 to rename it directly. **More tools** reveals batch filters, naming patterns, game data, and find/replace.

Number choices and naming rules are stored separately under `%LocalAppData%\Aide-de-Camp\Battleplan\catalogs`. A preview can reserve numbers there even before you save the campaign. Your save is changed only when you click **Save with backup**.

This beta was checked against a Battleplan v1.0.8 campaign. Keep your own copy of a save when trying a different game version. See [the full user guide](USER_GUIDE.md) for examples and troubleshooting.
