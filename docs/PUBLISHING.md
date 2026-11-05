# Publishing a Rift2Wii release

The repository is already named Rift2Wii. Its existing releases and tags are retained;
use a new version for a new release rather than replacing a published tag or archive.
Run the commands below in PowerShell from the repository root.

## Prepare

1. Update `Version` in `Rift2Wii/Rift2Wii.csproj`.
2. Write matching release notes in `docs/RELEASE-v<version>.md`, without naming any games.
3. Review README.md and THIRD-PARTY-NOTICES.md, especially if dependencies changed.
4. Build, run the checks and verify the release on the headset:

```powershell
dotnet build Rift2Wii.sln -c Release
dotnet run --project tests/BridgeChecks/BridgeChecks.csproj
```

Commit the reviewed source and documentation before tagging. Keep personal settings,
Dolphin configuration backups and captured content out of the repository and release.

## Package

Use the same version as the project and release notes. `0.1.1` below is an example.
Publish into a fresh folder so an old installation's settings cannot enter the archive.
The downloadable build requires the .NET 8 Desktop Runtime for Windows x64.

```powershell
$version = '0.1.1'
$publishDirectory = Join-Path $PWD "publish/Rift2Wii-$version-win-x64"
$archive = Join-Path $PWD "publish/Rift2Wii-v$version-win-x64.zip"
$checksum = Join-Path $PWD "publish/Rift2Wii-v$version-win-x64.sha256"
if (Test-Path -LiteralPath $publishDirectory) { throw 'Choose a fresh publish directory.' }
dotnet publish Rift2Wii/Rift2Wii.csproj -c Release -r win-x64 --self-contained false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$entries = Get-ChildItem -LiteralPath $publishDirectory | Where-Object Extension -ne '.pdb'
Compress-Archive -LiteralPath $entries.FullName -DestinationPath $archive
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksum, $hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine)
```

Verify that the archive includes Rift2Wii.exe, openxr_loader.dll, README, LICENSE,
THIRD-PARTY-NOTICES, docs and all dependency license texts. It must exclude personal
controller mappings, debug symbols, captured content and Dolphin itself.

## Publish

Check `gh auth status`. If authentication is missing or expired, complete sign-in yourself:

```powershell
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
```

Confirm `git status --short` is empty and the release version is committed. Then:

```powershell
git tag -a "v$version" -m "Rift2Wii v$version"
git push origin main --follow-tags
gh release create "v$version" $archive $checksum --repo philip0000000/Rift2Wii --verify-tag --title "Rift2Wii v$version" --notes-file "docs/RELEASE-v$version.md"
gh release view "v$version" --repo philip0000000/Rift2Wii
```
