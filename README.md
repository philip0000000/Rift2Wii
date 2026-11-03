# Rift2Wii

An independent Rift S controller bridge and VR window viewer for Dolphin Emulator.

Use your Rift S Touch controllers as an emulated Wii Remote and Nunchuk while viewing
Dolphin on a flat screen inside the headset. Rift2Wii handles viewing and controller
input in the same OpenXR session.

## Features

- Capture a window and display it on a movable, resizable VR screen.
- Aim and click the Windows mouse with the right controller.
- Direct mouse-to-infrared pointing through Dolphin's **Point (Passthrough)** bindings.
- Configurable keyboard mappings for both controllers, including triggers and grips.
- Analog joystick input through a local DSU server; no virtual controller driver needed.
- Right-hand acceleration and rotation for Wii Remote / MotionPlus input.
- Left-hand acceleration for Nunchuk motion.
- Automatic Dolphin setup with backups and a reusable controller profile.
- Settings saved automatically in a readable `controller-mapping.json` file.

## Requirements

- Windows 10 version 2004 (build 19041) or newer, 64-bit.
- Oculus Rift S with its Touch controllers and working Meta Horizon Link software.
- An active OpenXR runtime that recognizes the headset and controllers.
- [Dolphin Emulator](https://dolphin-emu.org/), installed separately.
- [.NET 8 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
  for the downloadable build. Choose **Desktop Runtime**, not just the standard runtime.

Tested on **Windows 11 with Oculus Rift S**. Windows 10 version 2004 is the minimum
Windows API requirement; the complete setup has not been verified on Windows 10.
Other headsets and controller layouts are unverified.
Buttons, analog input, motion and pointing have been tested on Rift S; this does not
promise compatibility with every application or input requirement.

## Quick start

1. Extract the release ZIP into a folder you can write to. Keep all its files together.
2. Run Dolphin once to create its configuration, then **close Dolphin**.
3. Start `Rift2Wii.exe` and click **Configure Dolphin**. If needed, use **Choose Dolphin
   folder…** and select Dolphin's User or Config folder.
4. Start Dolphin again and open its render window. Keep that window restored.
5. In Rift2Wii, click **Choose window…** and select Dolphin's render window. If rendering
   uses a separate window, select it instead of the main library window.
6. Put on the headset. Adjust **Screen width** and **Distance** as needed.
7. Click **Recenter in 3 s**, then look forward. The screen stays fixed until recentered.

Use Rift2Wii's built-in screen while playing. The app owns the OpenXR session used for
input and viewing; a separate VR desktop viewer can interrupt that session's input.
If keyboard or mouse output stops, restore and focus the captured Dolphin window.

## Default controls

Both hands control one Wii Remote with a Nunchuk extension.

| Rift S input | Dolphin input | Default output |
| --- | --- | --- |
| Right trigger | Wii A | Keyboard A |
| Right grip | Wii B | Keyboard B |
| Right B | Wii 1 | Keyboard 1 |
| Right A | Wii 2 | Keyboard 2 |
| Left Y | Wii minus | Keyboard Q |
| Left X | Wii plus | Keyboard E |
| Left trigger | Nunchuk Z | DSU L2, left device |
| Left grip | Nunchuk C | DSU L1, left device |
| Left thumbstick | Nunchuk stick | Analog DSU |
| Right thumbstick | Wii D-pad | DSU axes |
| Right controller aim | Desktop mouse / Wii pointing | Mouse coordinates |
| Right controller movement | Wii Remote motion | DSU accelerometer and gyro |
| Left controller movement | Nunchuk motion | DSU accelerometer |

Stick clicks and Wii Home have no default assignments. The right trigger also performs
a desktop left-click when **Right trigger clicks mouse** is enabled.

Expand **Left / right keyboard assignments** to change keys. Trigger and grip presses
use the configurable threshold. **Configure Dolphin** matches the selected keys; rerun
it with Dolphin closed after changing assignments. Automatic setup disables digital
stick-key output so the analog DSU bindings are used. **Use Wii defaults** restores the
button assignments above.

## Pointing

**Configure Dolphin** enables direct screen pointing. This calculates two virtual
infrared dots from the Windows mouse position, bypassing Dolphin's mouse-to-simulated-
rotation conversion. Dolphin's **Point (Passthrough)** is intentionally enabled in this
mode. Sensor-based gyro pointing remains disabled.

For adjustments, expand **Dolphin pointer calibration**:

- **Direct screen pointing (IR passthrough)** selects direct infrared pointing.
- **Horizontal reach** and **Vertical reach** adjust the coverage about the centre.
- **Vertical offset** shifts the infrared coordinates vertically.
- **Reset pointer defaults** selects direct mode, reach 25 horizontally / 20 vertically,
  and offset 0.

Close Dolphin, click **Apply pointer settings**, then restart Dolphin. Slider changes
alone do not change a running Dolphin. Apply updates pointer settings in Wii Remote 1
and Rift2Wii's saved profile, preserving button, stick and motion bindings.

Uncheck direct mode and apply to return to the previous mouse-controlled rotation path.
In that mode, reach values correspond to Dolphin's total yaw/pitch in degrees.
The emulated software ultimately interprets the infrared data, so exact pointer alignment
can vary. Start with the defaults before adjusting reach or offset.

## Motion and DSU

The local DSU server defaults to `127.0.0.1:26760`. Slot 0 is the left controller and
slot 1 is the right controller. **Configure Dolphin** registers it automatically.

Physical movement appears under Dolphin's **Motion Input** and **Extension Motion
Input**. The separate **Motion Simulation** shake/tilt/swing bindings stay empty;
those are for generating artificial movement from buttons or axes.

Motion is estimated from OpenXR tracking, including gravity, with filtering and
tracking-loss handling. It is not a direct feed from the controllers' internal IMUs.
Uncheck **Enable tracked motion (DSU)** to test with stationary sensors while keeping
buttons, sticks and mouse output active.

## Configuration and backups

Rift2Wii creates `controller-mapping.json` beside the executable when it is missing and
saves changes automatically. Keep the program in a writable folder. The file stores
key mappings, output switches, thresholds, DSU port, screen placement dimensions,
pointer settings and the selected Dolphin configuration directory.

Dolphin setup requires Dolphin to be closed. Before writing, it saves backups beneath
Dolphin's Config folder in `Rift2Wii Backups/<timestamp-id>/`. Each backup includes a
manifest describing which files previously existed. Failed writes attempt rollback.
See [Dolphin input details](docs/DOLPHIN-INPUT.md) for restoration and protocol notes.

Upgrading from OpenXR Input Explorer: keep `controller-mapping.json` beside the new
executable. Existing mappings continue to load. Run **Configure Dolphin** once to
install the Rift2Wii profile and migrate the previous DSU server entry. Previous
profiles and backup folders are retained.

## Build from source

Install the .NET 8 SDK, or Visual Studio 2022 with the .NET desktop development workload.
The C# namespaces retain their original internal name, `OpenXRInputExplorer`.

```powershell
dotnet build Rift2Wii.sln -c Release
dotnet run --project Rift2Wii/Rift2Wii.csproj -c Release
```

Create the Windows x64 framework-dependent release folder:

```powershell
dotnet publish Rift2Wii/Rift2Wii.csproj -c Release -r win-x64 --self-contained false -o publish/Rift2Wii
```

The native `openxr_loader.dll` is copied from the pinned OpenXR.Loader package. Keep it
beside `Rift2Wii.exe`. Do not substitute an arbitrary DLL download. Dependency versions
are intentionally retained from the tested build.

Automated bridge checks, without a headset:

```powershell
dotnet run --project tests/BridgeChecks/BridgeChecks.csproj
```

Checks cover mappings, configuration, Dolphin profile generation and rollback, pointer
expressions, motion conversion, and local UDP packets. Localhost UDP must be permitted.
Hardware behavior still requires headset testing.

## Project and license

Rift2Wii was developed from OpenXR Input Explorer. It uses C#, WPF, Silk.NET.OpenXR,
Vortice.Direct3D11 / DXGI, Windows Graphics Capture, and the Khronos OpenXR loader.

Rift2Wii is licensed under the [MIT License](LICENSE). Dependency licenses and notices
are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This is an independent project, not an official Meta, Nintendo, or Dolphin release.
Dolphin is installed separately; it is not included in the download.
