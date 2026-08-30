# Destination folder panel

This fork adds a thin, dockable `DestinationFolderPanel` without changing image rendering, decoding, caching, or archive readers.

## Quick classification

The fork registers `MoveToDestinationFolder1` through `MoveToDestinationFolder9` as native commands with default shortcuts `1` through `9`. Their shortcut, destination index, and multi-page policy are editable in the normal command settings; enabling the Scripts folder is not required.

For the original NeeView 46.3 release, copy `SampleScripts/MoveToDestination1.nvjs` through `MoveToDestination9.nvjs` into the Scripts folder shown by **Options > Scripts > Open scripts folder**. The fork ZIP does not bundle these scripts, which prevents duplicate shortcuts if scripting is enabled later.

Keys `1` through `9` follow the current Destination Folders order. Renaming a folder or changing its path does not require editing a script; reordering Destination Folders changes the numeric mapping.

If those number keys are already assigned to another command or script, resolve the shortcut conflict in NeeView's command/script settings before using the classification scripts.

Only real images opened from a folder can be moved. Archive, PDF, and playlist pages are rejected. `MultiPagePolicy` is `Once`, so only the main current image is moved.

## Panel and history

The panel always shows every configured Destination Folder without an item limit. When the list exceeds the available panel height, a draggable vertical scrollbar is shown. Clicking a row moves the main current image. The global `UndoDestinationMove` and `RedoDestinationMove` commands default to `Ctrl+Z` and `Ctrl+Y`; both shortcuts remain editable in the normal command settings.

Move history is kept in memory for the current session and is limited to 300 successful file moves by default. Its capacity is configurable from 0 to 1000 in the same settings section; zero disables move history. Cancelled or failed operations do not change the history. Undo and redo keep their top record when a file is missing or an overwrite is cancelled.

## Isolated profile

Use the ZIP package to keep the fork's `Profile` directory beside the executable, or set `NEEVIEW_PROFILE` to an existing absolute directory before starting NeeView. Do not replace files under `C:\Program Files\WindowsApps`.

To migrate settings once, export all settings from the Store version as an `.nvzip`, then import it into the fork. The profiles remain independent after the import.
