# BeanSlots Editor (WPF)

A Windows 10/11, .NET 8 WPF editor for the **BeanSlots** Oxide/uMod plugin configuration.

## Build

Open `BeanSlotsEditor/BeanSlotsEditor.csproj` in Visual Studio 2022+ with the .NET desktop development workload, or on Windows run:

```powershell
dotnet build .\BeanSlotsEditor\BeanSlotsEditor.csproj -c Release
```

Launch the resulting `BeanSlotsEditor.exe` from `bin/Release/net8.0-windows/`.

## Usage

1. Copy `oxide/config/BeanSlots.json` from your Rust server to the PC.
2. Click **Open config** and select it, or create defaults from **New defaults**.
3. Filter and multi-select Rust items on the left, add them to the slot prize list on the right.
4. Edit the **Shortname**, **Amount**, **Chance %**, and **Skin ID** directly in the grid. Multi-select prize rows and use **Apply batch changes** for bulk updates.
5. Configure Economics spin purchases, spin/animation settings, completion rewards for five Raidable Bases difficulties, and the HUD button in their own tabs.
6. Click **Save** / **Save As**, copy the JSON back to `oxide/config/BeanSlots.json`, and run `o.reload BeanSlots` in the server console.

The application **preserves unknown root and nested configuration properties** rather than recreating the file from a fixed model. Prize entries are saved in the exact field names the plugin expects (`Shortname`, `Amount`, `Chance`, `Skin`). Each chance is an **absolute percent per spin**. Total chance must not exceed 100%; the balance is no-prize probability. Existing plugin code uses the first matching prize in list order.

A built-in common-item catalog is provided for offline use. Use **Import item list** to load a more complete Rust item database JSON with fields like `shortname` and `displayName` (or `name`). No image URLs are shipped; the plugin itself displays Rust item icons in-game from shortnames. The editor currently presents item names/shortnames rather than thumbnails.

This project is source-only and hasn't been compiled in a Windows WPF toolchain in this environment. The project is separate from the Rust server plugin; do not upload the editor executable to the server.

## Version 1.1 - RustHelp catalog and thumbnails

- Loads the complete public RustHelp item export from `https://rusthelp.com/downloads/admin-item-list-public.json` when the editor opens. Use **Update RustHelp items** to refresh manually.
- Uses the export's `shortName`, `displayName`, and `iconUrl` fields. The item and prize grids display 38px item images.
- Caches the raw item catalog and downloaded PNG icons at `%LOCALAPPDATA%\BeanSlotsEditor\RustHelp\`. On an offline launch it uses the cached catalog; if no cache exists it uses the built-in smaller starter list.
- Downloads images asynchronously (up to six concurrent requests) without blocking the editor. Missing icons are left blank and do not affect JSON exports.
- Fixed the invalid unescaped ampersand in `MainWindow.xaml` tab title (`Economics &amp; spins`).

A live internet connection is required on the first launch to retrieve the full RustHelp catalog. Images and data belong to their respective owners. The RustHelp public export is a third-party resource and may change its format or availability.

## Version 1.2 - LocalAppData image cache and update check

- Persistent catalog: `%LOCALAPPDATA%\BeanSlotsEditor\RustHelp\items.json`.
- Persistent images: `%LOCALAPPDATA%\BeanSlotsEditor\RustHelp\icons\`.
- Image URL manifest: `%LOCALAPPDATA%\BeanSlotsEditor\RustHelp\icon-urls.json`.
- **Check new items & images** fetches RustHelp's latest catalog, reports newly added/changed/removed items, and downloads missing images or images whose source URL has changed. Existing unchanged icons are read from the disk cache, without downloading again.
- On subsequent launches the cached catalog and images are used immediately; network refresh is manual unless there is no local catalog.
- Downloads are limited to six concurrent requests; corrupt/failed new images do not replace working cached ones.
- The check detects image updates signaled by a URL change, not images replaced at an unchanged URL.

## Version 1.3 - Win vs. no-prize chances

- Shows large **Winning chance** and **Non-winning chance** totals above the slot prize list, with a live probability bar.
- Updates immediately when prizes are added, removed, duplicated or their chance values change (including batch changes).
- If total winning chance exceeds 100% or individual probabilities are invalid, the editor displays a warning. Saving still rejects totals over 100%.
- The remaining probability is the chance of receiving no prize: `max(0, 100 - sum(prize.Chance))`.

## Version 1.4 — prize display names
The selected-prizes grid now shows the RustHelp friendly item name alongside the editable shortname. Search matches both values. Export continues to use only the shortname so the BeanSlots plugin config stays compatible.

## Version 1.5 — probability guidance and recent config

- **App-only max winning chance** (default 65%, adjustable 0–100%) is saved to `%LOCALAPPDATA%\BeanSlotsEditor\editor-settings.json`. It is NOT added to the Rust server's `BeanSlots.json`.
- Winning odds and losing odds update live; the editor shows warnings and actionable suggestions when the total is above the chosen maximum.
- **Scale chances to max** proportionally adjusts all prize chances while preserving relative rarity. **Distribute evenly** sets equal chances, totaling the maximum. Both actions change the current editor document and must be explicitly saved to write the server config.
- Saving rejects odds above the app-only maximum. The actual Rust plugin's supported total remains at or below 100%.
- Automatically reopens the last successfully opened/saved config at startup, if its path exists. If missing or unreadable, shows a status message and keeps the default document open.
- Prize objects retain extra JSON fields including `LegendaryId` (as well as any other custom fields) across load, duplication and save.

## Legendary prize labels (v1.7)

The Selected Prizes item-name column displays the legendary name when `LegendaryId` is set (for example, `hellfire_ak` displays **Hellfire AK**), rather than reverting to RustHelp’s base item name after the catalog loads. If a prize supplies `LegendaryName`, that explicit value takes priority. Rust shortnames, icons, and all extra prize JSON fields are preserved. No new fields are exported automatically.

## v1.8 — Legendary rarity tiers and editable suggestion weights

Prizes support Unknown, Common, Uncommon, Rare, Very Rare, Legendary Common,
Legendary Uncommon, Legendary Rare, and Legendary Very Rare.
Existing stored `Legendary` assignments migrate to `Legendary Common`; new prizes
with a `LegendaryId` default to `Legendary Common`. All other new prizes default to
`Unknown`.

The **Rarity Weights (app only)** tab lets you edit the relative weighting for
all nine rarity tiers. Save weights, then click **Apply rarity suggestions** on
the prizes tab to distribute the app maximum winning chance among prizes. Weights
of zero exclude a rarity from suggestions. Applying suggestions requires confirmation;
changing weights alone does not change any prize probabilities.

Weights and rarity selections are persisted in
`%LOCALAPPDATA%\BeanSlotsEditor\editor-settings.json`, never in the server
`BeanSlots.json`. Additional per-prize server fields such as `LegendaryId` are
preserved. Full WPF compilation has not been tested in this environment.

## Reel animation mode

Under **Economics & spins**, select **Simultaneous** (original animation) or **Sequential** (left, middle, right stop). The editor writes `AnimationMode` to BeanSlots.json. It defaults to Simultaneous in older configs.


## v2.1 category and LegendaryItems catalog import

- The Category filter uses `admin-item-list-public.json` base categories and `categories.json` assignment overrides, even when a stale RustHelp cache exists. Only nonempty categories are shown.
- Press **Import LegendaryItems Config** in the Prize editor and select your `LegendaryItems.json`. Every enabled definition with a base shortname becomes a separate catalog entry under Legendary Weapons, Legendary Clothing, or Legendary Backpacks. The imported definitions are remembered in `%LOCALAPPDATA%/BeanSlotsEditor/legendary-items-import.json`.
- Selecting a Legendary entry adds the proper `LegendaryId`, `LegendaryName`, base Rust shortname, and skin to the BeanSlots prize JSON. This is only catalog import; it does not replace the live LegendaryItems plugin configuration.
- Each Legendary definition can be selected independently from the standard Rust item even when sharing a shortname.
