# Rift2Wii v0.1.1

Maintenance release bringing the download in line with the current Rift2Wii repository.

- Consistent Rift2Wii solution, project, executable and source-folder names.
- Removed empty prototype classes, unused fields and deprecated OpenXR enum aliases.
- Updated setup, build and publishing documentation.
- Removed the outdated prototype screenshot and corrected GitHub language classification.
- Retained the existing controller mappings, direct infrared pointing and motion bridge.

Windows x64. Requires the .NET 8 Desktop Runtime, a working Rift S/OpenXR setup and
Dolphin Emulator installed separately. Extract the ZIP and run Rift2Wii.exe.

When upgrading, keep controller-mapping.json beside the new executable. Existing
v0.1.0 configurations continue to work; no controller remapping is required.

The input behavior was previously tested on Windows 11 with Rift S. This maintenance
build was checked with automated bridge tests and release-package validation; a new
physical headset session was not performed. Windows 10 and other headsets remain unverified.

The application is MIT-licensed; third-party license texts are included. The release
includes a SHA-256 checksum for its Windows x64 archive.
