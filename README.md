# Mount 3DX
Mount your 3DX server as a drive letter, so you can browse and open documents without going through the 3DX web UI.

![Mount3DX](https://github.com/fiddyschmitt/Mount3DX/assets/15338956/2840f2cd-4738-4183-afd1-43f7aaec8379)

# Download
Releases can be found over in the [releases](https://github.com/fiddyschmitt/Mount3DX/releases) section.

# How the drive is served
On Windows 11 24H2 or later the documents are served as an SMB share, `\\127.0.0.1\3DX`, by a bundled copy of [portable-smb-server](https://github.com/fiddyschmitt/portable-smb-server) (a single executable, no admin rights, no installation). Older Windows gets the same tree over WebDAV at `\\localhost@11000\DavWWWRoot`, as before. Over SMB, what is read is cached in 8 MiB pieces under `%LOCALAPPDATA%\Mount3DX\cache`, at most `CacheSizeMB` (2048) at a time, so files of any size can be opened and copied; the cache is cleared when the session stops. `Transport` in `settings.json` (beside the executable) picks one, `Smb` or `WebDAV`, or leaves it to the program (`Auto`, the default); the `Smb` and `WebDAV` groups next to it hold each one's address. The log says which one is in use.

# Building
The SMB server is fetched, checksummed and embedded by the build: run `scripts\fetch-smb-server.ps1` once after cloning (and whenever `scripts\smb-server.json` pins a new version). A build without it still works and serves WebDAV only.
