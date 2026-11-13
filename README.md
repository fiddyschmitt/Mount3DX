# Mount 3DX
Mount your 3DX server as a drive letter, so you can browse and open documents without going through the 3DX web UI.

![Mount3DX](https://github.com/fiddyschmitt/Mount3DX/assets/15338956/2840f2cd-4738-4183-afd1-43f7aaec8379)

# Download
Releases can be found over in the [releases](https://github.com/fiddyschmitt/Mount3DX/releases) section.

# How the drive is served
On Windows 11 24H2 or later the drive is served over SMB, by [LibSmb](https://www.nuget.org/packages/LibSmb). Older versions of Windows get it over WebDAV.
