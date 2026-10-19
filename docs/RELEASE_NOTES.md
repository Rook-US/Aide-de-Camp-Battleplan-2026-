# v0.1.0 beta 1

First public beta of Aide-de-Camp for Battleplan. Windows x64; self-contained single executable.

## What it does

- Opens `.camp` campaigns from the current Windows account's Battleplan save folder or a manually selected path.
- Shows the army tree with search, expand/collapse, direct name editing, and Ctrl/Shift multi-selection.
- Renames one regiment's battalions or previews names across the army, with editable suggestions and opt-out checkboxes.
- Lets players assign generic HQs their intended command level, set numbers, reserve unused division and regiment numbers, and save naming patterns.
- Keeps editor preferences in the current account's LocalAppData folder. Existing catalogs from this project's older development layout migrate on first use.
- Saves only player name overrides, creates a timestamped backup, and refuses to overwrite a campaign changed after opening.

## Before using

Extract the ZIP, read `QUICK_START.md`, and close Battleplan before saving. The app is an unofficial community tool. It has been exercised against a v1.0.8 campaign; verify results in game and keep an extra copy when editing saves from another version.
