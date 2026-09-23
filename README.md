# QuickLook.Plugin.IsoViewer 
Plugin for [QuickLook](https://github.com/QL-Win/QuickLook), allowing to preview `.iso` file. PSP Game/Video/Audio UMD metadata + file tree; other ISOs show file tree.

This plugin combines [QuickLook.Plugin.PbpViewer](https://github.com/Xalk07/QuickLook.Plugin.PbpViewer) and [QuickLook.Plugin.FolderViewer](https://github.com/adyanth/QuickLook.Plugin.FolderViewer) adapted for ISO support.
This is a vibecoding project.

If you would like to add support for additional ISO file types and/or a preview feature, please feel free to do so.

## Features 

- Supports ISO 9660, Joliet, UDF (Universal Disk Format).
- Supports for previewing PSP Game/Video/Audio UMD.
- PSP ISO and other ISOs show file tree.


## Screenshots 
![ISO preview.](Preview%20images/iso.PNG)
ISO preview.


![PSP game ISO preview.](Preview%20images/pspiso.PNG)
PSP game ISO preview.


## Download and Installation
1. Go to [Release page](https://github.com/xalk07/QuickLook.Plugin.IsoViewer/releases) and download the latest version.
2. Make sure that you have QuickLook running in the background. Press `Spacebar` on the downloaded `.qlplugin` file.
3. Click the `Install` button in the popup window.
4. Restart QuickLook.

## Development
1. Clone repo and sub-modules
2. Build project with Release profile.
3. Run `Scripts\pack-zip.ps1`
4. Find plugin `QuickLook.Plugin.IsoViewer.qlplugin` in the project directory.


## License
 &nbsp;&nbsp;&nbsp;&nbsp;**GPL-3.0**
