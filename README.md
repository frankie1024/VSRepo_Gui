# VSRepo_Gui

`VSRepo_Gui` is a Windows desktop application for browsing, installing, upgrading, and removing VapourSynth plugins through `VSRepo`.

It is designed to make the day-to-day plugin workflow easier from one place:

- browse plugins
- install / uninstall / upgrade packages
- upgrade all packages
- inspect package details and dependencies
- filter by status / category / search
- view VSRepo paths and environment settings in a dedicated Settings page

## Tech Stack

- `.NET 8`
- `WPF`
- `WPF-UI`

## Notes

- Elevated operations are supported when VSRepo needs to write into protected directories such as `Program Files`.

## Build And Publish

- Development build: `dotnet build`
- Release package: `python .\tools\package_release.py`
- Published output: `dist/`
- GitHub release asset: upload the generated zip package only, not a standalone exe

