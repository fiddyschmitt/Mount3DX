# Mount 3DX
Mount your 3DX server as a drive letter, so you can browse and open documents without going through the 3DX web UI.

![Mount3DX](https://github.com/fiddyschmitt/Mount3DX/assets/15338956/2840f2cd-4738-4183-afd1-43f7aaec8379)

# Download
Releases can be found over in the [releases](https://github.com/fiddyschmitt/Mount3DX/releases) section.

# How the drive is served
On Windows 11 24H2 or later the drive is served over SMB by a bundled copy of [portable-smb-server](https://github.com/fiddyschmitt/portable-smb-server). Older versions of Windows get it over WebDAV.

# Building
The SMB server is fetched, checksummed and embedded by the build: run `scripts\fetch-smb-server.ps1` once after cloning (and whenever `scripts\smb-server.json` pins a new version). A build without it still works and serves WebDAV only.
