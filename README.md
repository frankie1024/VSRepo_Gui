# VSRepo_Gui

`VSRepo_Gui` is a Windows desktop application for managing VapourSynth plugins through `VSRepo`.

## Features

- Browse available packages
- Install, uninstall, and upgrade packages
- Upgrade all installed packages
- View package details and dependencies
- Filter packages by status, category, and search text
- Inspect VSRepo definitions, binaries, and scripts paths

## Tech Stack

- `.NET 8`
- `WPF`
- `WPF-UI`

## Build

- Development build: `dotnet build`
- Release package: `python .\tools\package_release.py`
- Published output: `dist/`

