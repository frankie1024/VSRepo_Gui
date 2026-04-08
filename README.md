# VSRepo_Gui

`VSRepo_Gui` is a Windows desktop GUI for `VSRepo`.


It focuses on the common VapourSynth workflow:

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
- Release publish: `dotnet publish`
- Published output: `dist/`

