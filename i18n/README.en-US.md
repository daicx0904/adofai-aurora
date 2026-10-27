# <center>Aurora</center>

**<center>A Windows Mod Manager for A Dance of Fire and Ice (ADOFAI).</center>**

<p align="center">
  | <a href="../README.md">简体中文</a> |
  <a href="i18n/README.en-US.md">English (US)</a> |
</p>

<br/>
Aurora brings Mods from multiple sources together in one interface, making it easy to browse, search, install, and check for updates. It also provides local Mod management and UnityModManager loader maintenance. The interface is primarily in Chinese. By default, Aurora connects to ADOFAI Tools and also supports modrift, TUF, modlist, and custom sites that implement the compatible API.

<br/>

> [!IMPORTANT]
> The project was originally named ADOFAI Mod Manager (AMM). It was renamed to Aurora starting with `v0.1.6` to avoid confusion with another tool of the same name.

> [!WARNING]
> This is a third-party tool and is not affiliated with 7th Beat Games, the official UnityModManager project, or any Mod hosting sites.

## Features

### Online Mods

- Switch between ADOFAI Tools, modrift, TUF, modlist, and custom sources
- Browse, search, sort, and filter Mods by category, depending on the features supported by each source
- View Mod descriptions and versions, and install specific versions
- Identify installed Mods and available updates
- Display source information and links to the original Mod pages
- Verify downloaded files
- Identify Mods that require MelonLoader; automatic installation of these Mods is not currently supported, and Aurora does not install the MelonLoader loader itself

### Manage Installed Mods

- Enable, disable, update, uninstall, and open the folders of Mods
- Install local ZIP archives containing Mods that are not listed on supported sites, created by you, or obtained from other sources
- Manually link GitHub repositories to check for and obtain updates within the application
- Check for updates across multiple sources
- Detect duplicate installations to reduce the chance of old copies being left behind after updates

### UMM Environment

- Detect, install, repair, and uninstall the UnityModManager loader
- Update or roll back the UMM loader independently
- Use bundled files, download loader files from supported sites, or import local loader files
- Launch the game through Steam or by directly running the game executable

### Other Features

- Favorite Mods from different sources and install them through their respective sources
- Check for installed Mod updates at startup
- Check for Aurora updates and upgrade the application directly within the software
- First-launch setup wizard
- Light, dark, and system-matched themes

## System Requirements

- Windows 10 / 11 (x64)
- Steam version of A Dance of Fire and Ice

The release packages include the .NET runtime, so no separate installation is required.

## Download and Upgrade

Go to [Releases](../../releases) to download:

| Package      | Description                                                       |
| ------------ | ----------------------------------------------------------------- |
| Installer    | Choose the installation scope and directory, and create shortcuts |
| Portable ZIP | Extract and run `Aurora.exe`; no registry entries are written     |

Configuration, logs, and cache files are stored in the user directory rather than the application installation directory, so Aurora can also be installed in `Program Files`.

To upgrade from an older version of AMM, you can install the new version over the existing installation or extract the portable ZIP over the existing directory. Existing settings, favorites, and logs will be preserved. Old GitHub URLs will also redirect automatically to the new repository.

## First-Time Setup

The first-launch wizard guides you through configuring the game directory, Mod sources, and UMM environment. After setup, select a source in the upper-right corner of the **Online Mods** page to start browsing and installing Mods.

## Mod Sources

Configure the default and custom sites under **Settings**, then switch between sources on the **Online Mods** page when browsing.

| Source                                                  | Description                                                                         |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| [ADOFAI Tools](https://adofaitools.top/)                | Default source. Requires an API key, which you can create in the site’s user center |
| [modrift](https://modrift.org)                          | No API key required; provides a public API                                          |
| [TUF (The Universal Forums)](https://tuforums.com/mods) | No API key required; provides a Mod catalog and download service                    |
| [modlist](https://modlist.org)                          | No API key required; provides a public API                                          |
| Custom sites                                            | Add sites under **Settings → Custom Sites** after implementing the compatible API   |

Aurora is not tied to a single Mod hosting site. Content from each source is labeled with its origin and includes a link to the corresponding original page.

When a Mod is installed from a particular source, Aurora prioritizes that source for future updates. The **Installed** page also checks other sources for newer versions. Manual checks refresh the relevant data, while automatic checks at startup use cached data.

Aurora does not include a built-in API key. If a source requires authentication, create your own key on the corresponding site and enter it in Aurora. Mod downloads are handled through each source’s download API, and a local cache helps reduce redundant requests.

See the [Resource Site API Specification](docs/RESOURCE-SITE-API.md) for requirements for custom sites.

## Usage Notes

- A newer version is not necessarily compatible with your current game environment. Read the Mod author’s instructions before updating.
- Compatibility information provided by Mods or hosting sites is for reference only. Aurora cannot automatically determine whether a Mod is compatible with your current game version.
- Bulk updating all Mods with a single click is not currently supported. Updates must be selected and applied individually.
- Online features depend on the availability of the relevant Mod sources. If a site is temporarily unavailable, you can still install Mods from local ZIP archives.

## Development

The .NET 10 SDK is required.

Run in debug mode:

```powershell
dotnet run --project src/AdofaiModManager
```

Build release packages:

```powershell
powershell -ExecutionPolicy Bypass `
  -File installer/build-installer.ps1 `
  -Version 0.1.6
```

By default, the build generates a portable ZIP package in `dist\Aurora\`. To generate an installer, add the `-Installer` parameter.

## Acknowledgments

- Thanks to [small-lizi](https://github.com/small-lizi/), the maintainer of [ADOFAI Tools](https://adofaitools.top/), for allowing Aurora to integrate with the service and providing online Mod data and downloads. Related code is available in the [ADOFAI Tools repository](https://github.com/small-lizi/ADOFAI-Tools).
- Thanks to Ilythra, the creator of [modrift](https://modrift.org), for providing a public API and allowing Aurora to integrate with it.
- Thanks to the TUF (The Universal Forums) maintenance team ([GitHub: T21C](https://github.com/T21C)) for providing a Mod catalog, recommendations, and download services. Data from TUF displayed in Aurora is attributed to its source and links back to the TUF site.
- Thanks to square3ang, the creator of [modlist.org](https://modlist.org), for providing a public API and allowing Aurora to integrate with it. Related projects are available under [modlist-org](https://github.com/modlist-org).
- Thanks to [UnityModManager](https://github.com/newman55/unity-mod-manager) and its author [newman55](https://github.com/newman55/) for providing the loader foundation.

## Contributors

- [@create-xiaoyu](https://github.com/create-xiaoyu): Installation security, update checks, and stability fixes ([PR #2](https://github.com/lishangi688/adofai-aurora/pull/2)).
- [@xbodwf](https://github.com/xbodwf): Markdown renderer and research into multi-loader feasibility ([PR #1](https://github.com/lishangi688/adofai-aurora/pull/1)).

## License

This project is released under the [MIT License](LICENSE).

Licenses for third-party components distributed with the application are located in:

```text
src/AdofaiModManager/Resources/UmmLoader/
```

## Disclaimer

- We recommend backing up your game files, saves, and Mods before use. You assume all risks associated with using this software.
- Online features depend on third-party Mod hosting sites. Their availability and content are the responsibility of the respective sites.
- Before installing a Mod, comply with the license and usage terms provided by the Mod author and hosting site.
