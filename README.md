# vsrepo_Gui

`vsrepo_Gui` is a Windows desktop GUI for `python -m vsrepo.vsrepo`.

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

- The application is designed around modern VSRepo, not the old `vsrepo.py` workflow.
- Elevated operations are supported when VSRepo needs to write into protected directories such as `Program Files`.
