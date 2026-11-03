# Publishing Rift2Wii v0.1.0

The release files are prepared locally. Run these commands in PowerShell from
`C:\repos\Rift2Wii` after reviewing the README and release notes.

Sign in to GitHub again (complete the browser/device-code flow yourself):

```powershell
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
```

Rename the existing repository; this preserves its history. Skip the rename command
if the repository has already been renamed.

```powershell
gh repo rename Rift2Wii --repo philip0000000/OpenXR-Input-Explorer --yes
git remote set-url origin https://github.com/philip0000000/Rift2Wii.git
gh repo edit philip0000000/Rift2Wii --description "Independent Rift S controller bridge and VR window viewer for Dolphin Emulator."
```

Push the prepared commit and annotated release tag:

```powershell
git push origin main --follow-tags
```

Publish the release with its Windows x64 archive and SHA-256 checksum:

```powershell
gh release create v0.1.0 publish/Rift2Wii-v0.1.0-win-x64.zip publish/Rift2Wii-v0.1.0-win-x64.sha256 --repo philip0000000/Rift2Wii --verify-tag --title "Rift2Wii v0.1.0" --notes-file docs/RELEASE-v0.1.0.md
```

The downloadable build requires the .NET 8 Desktop Runtime. The archive excludes
personal controller settings, debug symbols, captured content, and Dolphin itself.
