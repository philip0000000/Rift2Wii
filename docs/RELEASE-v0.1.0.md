# Rift2Wii v0.1.0

First release of Rift2Wii, developed from OpenXR Input Explorer.

- Rift S Touch input for one emulated Wii Remote and Nunchuk in Dolphin Emulator.
- Built-in OpenXR screen with window capture, size/distance controls and recentering.
- Configurable keyboard bindings for buttons, triggers and grips.
- Analog sticks and tracking-derived motion over local DSU.
- Direct mouse-to-infrared pointing with the corrected vertical direction.
- Automatic Dolphin setup, pointer adjustment, saved settings and configuration backups.

Windows x64; requires the .NET 8 Desktop Runtime, a working Rift S/OpenXR setup and
Dolphin Emulator installed separately. Extract the ZIP and run Rift2Wii.exe.

Existing users: keep controller-mapping.json beside the new executable. Close Dolphin
and run Configure Dolphin once to install the Rift2Wii profile and migrate the server name.

Buttons, sticks, motion and pointing were tested on Windows 11 with Rift S. Windows 10
and other headsets are unverified.
Motion is estimated from tracking rather than raw IMU data. Pointer behavior ultimately
depends on the emulated software. Read README.md for setup and controls.

The application is MIT-licensed; third-party license texts are included in the archive.
