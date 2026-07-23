# vAutoBackup  ·  v26.7.23.1911

vAutoBackup is a Rhino 8 and Rhino 9 plug-in for verified periodic and one-shot backups of the active document.

## Features

- Starts periodic backups automatically when configured.
- Supports immediate one-shot backups and runtime start/stop controls.
- Skips unchanged documents when requested.
- Writes each backup to a temporary archive, verifies that Rhino can reopen it, and only then promotes it to the final `.3dm` file.
- Preserves source-relative folders for saved documents and gives unsaved documents timestamped names.
- Optionally removes older matching backups while retaining the configured count.

## Commands

| Command | Purpose |
| --- | --- |
| `vAutoBackup` | Open the command-line menu for OneShot, Start, Stop, Status, and Options. |
| `vAutoBackupOneShot` | Create a verified backup immediately. |
| `vAutoBackupStart` | Start the periodic monitor. |
| `vAutoBackupStop` | Stop the periodic monitor. |
| `vAutoBackupStatus` | Report monitor state and the next scheduled backup. |
| `vAutoBackupOptions` | Edit and immediately save backup settings. |

## Configuration

`vAutoBackup.config.json` is stored beside the plug-in DLL. Default values are included in the repository:

```json
{
  "backupRoot": "D:\\Backup\\Rhino",
  "intervalMinutes": 10.0,
  "enableCleanup": true,
  "keepLast": 100,
  "skipIfUnchanged": true,
  "logLevel": "Info",
  "autoStart": true
}
```

Available log levels are `Errors`, `Info`, and `Verbose`. Settings changed through `vAutoBackupOptions` are saved immediately.

## Build

From the repository folder:

```powershell
.\build.ps1
```

The default Release build does not require Git and never commits or pushes. Maintainers can use `.\build.ps1 -Publish` to build, create a signed semantic commit when the DLL changes, push `master`, and publish a GitHub release containing separate Rhino 8/.NET 7 and Rhino 9/.NET 10 DLLs, plus any generated `.rui` files.

## Installation

The Release plug-ins are:

- `bin/Release/net7.0-windows/vAutoBackup.dll` for Rhino 8
- `bin/Release/net10.0-windows/vAutoBackup.dll` for Rhino 9 Load it with Rhino's Plug-in Manager and keep `vAutoBackup.config.json` beside the DLL when deploying custom defaults.

Runtime diagnostics are written to `vAutoBackup.log` beside the loaded DLL. The file is cleared at startup and begins with the Rhino and plug-in versions.

## Versioning

Build versions use `yy.m.d.hmm`, derived from the newest C# source file rather than the compile time.

## License

Released under the [MIT License](LICENSE).