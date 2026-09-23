# Quiver Launcher 3.5.0-rc.1

This is a prerelease for testing before 3.5.0.

## Flatpak on Linux (experimental)

- A Linux x64 Flatpak bundle is now included with the release.
- Install and update games distributed as direct `.flatpak` release bundles, launch them through the host, and preserve their application data when uninstalling.
- Improve host game launching, Wine/Proton discovery, Steam integration and game process tracking when Quiver runs inside Flatpak.
- The Flatpak launcher disables its built-in updater. Install a newer bundle to update Quiver; a Flathub repository is not yet available.

## Platform availability and filters

- Save filters using platform availability, tags, or both, including a platform-only filter such as “Waiting for Linux”.
- Discover newly detected platform builds in the catalog's **New platform support** view. Notices follow the selected platforms and can be dismissed independently of catalog changes.
- Simplify catalog cards and filter labels, make active filters clearer, and improve keyboard/gamepad navigation.

## Installation and release fixes

- Fix release selection incorrectly treating distinct version tags as equivalent. Exact tags take priority while supported numeric aliases still match.
- Revalidate automatic release choices before installation and refresh obsolete cached selections.
- Support Windows MSI release assets through the setup wizard, with executable selection and Windows-managed uninstall.

## Flatpak testing notes

Install `QuiverLauncher.flatpak` using your software manager, or run:

```sh
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user ./QuiverLauncher.flatpak
flatpak run io.github.tgeorgiadis.QuiverLauncher
```

Flatpak uses a separate Quiver library; existing AppImage libraries are not imported automatically. Please test game installation, updates, launching and Steam shortcuts on your Linux desktop. Automated startup checks do not replace testing on real desktop/Steam Deck hardware.
