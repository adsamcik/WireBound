# Installation and Updates

WireBound is self-contained: you do not need to install the .NET runtime.

## Windows

1. Open the [latest WireBound release](https://github.com/adsamcik/WireBound/releases/latest).
2. Download `WireBound-win-x64-Setup.exe`.
3. Run Setup. WireBound installs for the current Windows user and launches when installation finishes.

The installer is the recommended Windows download. It supports in-app updates and keeps the installed application separate from your monitoring history and settings.

## Linux

1. Download `WireBound-linux-x64.AppImage` from the latest release.
2. Mark it executable: `chmod +x WireBound-linux-x64.AppImage`.
3. Run the AppImage.

## Updating

Installed builds check GitHub Releases without sending telemetry. In **Settings**, the update card clearly shows whether WireBound is checking, current, downloading, ready to restart, or needs a manual installer.

When an update is ready, select **Restart & Install**. WireBound saves settings, stops background work, installs the update, and relaunches. A downloaded update remains ready after closing and reopening WireBound.

Portable ZIP/TAR.GZ builds link to the release download page instead of modifying their own files.

## Repairing an installation

Re-running the latest Windows Setup repairs an installed copy without deleting WireBound's monitoring database. Before repair, close WireBound and any `WireBound.Elevation` process shown in Task Manager.

WireBound stores persistent data separately from the application:

- Windows: `%LOCALAPPDATA%\WireBoundData`
- Linux: the platform `LocalApplicationData/WireBoundData` directory

### Recovering a v0.9.0 Windows installation

Version 0.9.0 could place persistent files in Velopack's `%LOCALAPPDATA%\WireBound` application directory. If Setup reports that it cannot remove the existing application directory:

1. Close all WireBound processes.
2. Create `%LOCALAPPDATA%\WireBoundData`.
3. Copy `wirebound.db`, `wirebound.db-wal`, `wirebound.db-shm`, `.elevation-secret`, `logs`, and `app-icons` from `%LOCALAPPDATA%\WireBound` when present.
4. Keep the originals until WireBound opens successfully and your history is visible.
5. Run the latest Setup again.

Newer versions perform this copy automatically on first launch and never overwrite data already present in the new directory.

### WireBound does not open

Version 0.9.0 has a known Avalonia/LiveCharts compatibility error that can close WireBound while the first dashboard is loading. Install the latest Windows Setup to replace that build; your database can be retained using the recovery steps above.

Newer versions display a startup error with the diagnostic-log location. On Windows, logs are stored under `%LOCALAPPDATA%\WireBoundData\logs`.

## Uninstalling

Use **Settings > Apps > Installed apps** on Windows. Uninstall removes the application and its startup registrations but preserves `%LOCALAPPDATA%\WireBoundData`, allowing a later reinstall to retain history and settings.

To remove all history permanently, delete that data directory only after uninstalling and only if you no longer need its contents.
