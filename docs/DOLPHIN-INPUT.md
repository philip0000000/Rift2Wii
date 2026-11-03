# Dolphin input bridge and tracked motion

Research checked 30 October 2026 against Dolphin's controller guide and source.
Automatic configuration added 2 November 2026 after checking the INI and device naming formats.

## Automatic setup

Close Dolphin, then click **Configure Dolphin** in the viewer. Standard Windows user
folders and registry overrides are detected. For portable/custom user folders, click
**Choose Dolphin folder…** and select Dolphin's User folder (or Config subfolder) once.
The location is remembered in `controller-mapping.json`. Portable mode is detected from
the executable hint when available; an unrecognized custom path requires that folder choice.

Setup configures only Wii Remote 1's bindings, keeps other controller slots, preserves other
DSU servers, and adds one named `Rift2Wii` at the current loopback port. That
unique name lets its expressions address left/right slot 0/1 consistently. It enables
emulated Bluetooth and background input, writes a reusable profile, and applies it immediately
to WiimoteNew.ini for the next Dolphin launch. Do not run Dolphin during installation.

| Physical control | Wii control |
| --- | --- |
| Right trigger | Wii A (confirm) |
| Right grip | Wii B (cancel) |
| Right B | Wii 1 |
| Right A | Wii 2 |
| Left X (left equivalent of A) | Wii plus |
| Left Y (left equivalent of B) | Wii minus |
| Right stick click | Unassigned |
| Left stick click (L3) | Unassigned |
| Left grip | Nunchuk C |
| Left trigger | Nunchuk Z |
| Left stick | Nunchuk stick |
| Right stick | Wii D-pad |
| Right aim | Existing mouse-controlled Wii pointer |

Assigned keyboard keys are used for the corresponding buttons/triggers; **None** uses
a DSU fallback in the generated profile. Trigger and grip keyboard assignments are both
supported; stick axes use DSU directly. The viewer enables keyboard output, DSU and mouse
control, and disables stick-direction keys to avoid duplicate stick output. Wii A no
longer uses a mouse-click binding, so trigger mouse clicks can still navigate desktop UI.
Both hands' face buttons target the same main Wii Remote, not a second Wii Remote.
The default keyboard keys for left Y/X are Q/E for Wii minus/plus. Wii Home has no
automatic binding; left stick-click remains available for manual key assignments if desired.
Existing saved key choices
are retained until you click **Use Wii defaults**; then click **Configure Dolphin** to
regenerate the matching profile. Screen and other preferences are not reset by this action.

Restart Dolphin, capture its game window, and test. Changing key assignments, the trigger
threshold or DSU port requires running setup again while Dolphin is closed. Per-game
profiles can override this global configuration. Physical motion uses sensor bindings;
the separate canned shake, tilt and swing bindings remain empty.

Backups are in `Config/Rift2Wii Backups/<timestamp-id>/`, preserving original
bytes and a `manifest.json` listing files that did not exist before setup. For restoration,
close Dolphin and copy backup files to their matching paths beneath Config; files marked
`Existed: false` were created by setup. A failed install attempts to roll back written
files and reports the backup location. Files changed externally during installation
are not silently overwritten.

## What the current implementation provides

- Configurable keyboard output for six face/stick-click buttons across both hands,
  with additional trigger and grip assignments. Menu/system buttons are excluded.
- Optional four-direction key mappings per stick. These are digital: partial stick
  travel cannot reproduce a proportional Nunchuk stick through keyboard input alone.
- A loopback DSU server for two independent analog pads, left slot 0 and right slot 1.
  This carries stick values, trigger/grip values and buttons without a controller driver.
- Existing right-hand ray-to-screen mouse pointing, with optional trigger left-click.

## Triggers

Wii Remote B is a digital button. Nunchuk C and Z are digital buttons too. Map a Touch
trigger to a key and bind that key to B or Z in Dolphin; the viewer applies a configurable
press threshold and hysteresis. Analog trigger values are also available as DSU L2 if
preferred; grip uses L1. The current automatic profile maps right trigger to Wii A and
right grip to Wii B. Mouse-clicking can be disabled separately if you want only game input.

## Analog Nunchuk setup

1. Enable DSU in the viewer. Default endpoint: `127.0.0.1:26760`. Pick another port before
   enabling if that port is already used by another DSU server.
2. In Dolphin's Alternate Input Sources, enable DSU and add that endpoint.
3. In Nunchuk stick bindings, right-click to open the advanced binding dialog and choose
   the left virtual DSU controller. Check its live axes to identify it if other DSU
   servers are installed; Dolphin's device enumeration number is not a universal slot ID.
4. Bind Up=`Left Y-`, Down=`Left Y+`, Left=`Left X-`, Right=`Left X+`. Calibrate in Dolphin
   if needed. The viewer's radial dead zone rescales the remaining travel continuously.
5. If Wii buttons still use keyboard input, retain Keyboard/Mouse as their default and
   insert fully qualified DSU expressions only into the stick fields. Changing the global
   device changes the meaning of unqualified expressions in other bindings too.

## Motion: keyboard simulation versus DSU

Dolphin distinguishes **Motion Simulation** (generate movement from ordinary controls)
from **Motion Input** (accelerometer and gyro samples). The screenshots illustrate that
distinction: a key can activate a canned shake, tilt or swing, but that does not transmit
the physical controller's continuous movement. Buttons are useful for a fallback shake
command, and analog axes can drive simulated movement, but tracked motion needs more data.

DSU is the output path for physical Wii motion. Dolphin already implements
its sticks, analog triggers, accelerometer and gyroscope inputs. Each DSU pad has one set
of motion sensors, so two pads allow right-hand Wii Remote and left-hand Nunchuk motion.
The Nunchuk needs acceleration; a MotionPlus-enabled Wii Remote can also use gyro input.
The protocol carries acceleration in g and angular velocity in degrees/second; a position
or orientation quaternion cannot simply be put into those fields.

The viewer now requests OpenXR linear/angular velocity alongside each tracked grip pose.
When a velocity is unavailable it differentiates timestamped position/quaternion samples.
It estimates acceleration from velocity changes, adds stationary gravity, transforms both
signals into right-hand aim-local or left-hand grip-local axes and encodes acceleration
in g and rotation in degrees/second. Right-hand motion uses the same runtime aim pose as
the desktop mouse ray, so the grip's mounting angle does not change the reported tilt.
The Wii axes are left/backward/up, unlike OpenXR's right/up/backward axes.
A 25 ms low-pass filter reduces derivative noise. Invalid samples, tracking jumps, long
sample gaps and loss of output reset derivative history; recovery primes it before
transmitting dynamic motion. The DSU watchdog returns stationary gravity and zero gyro.
Packets retain a monotonic timestamp even while neutral.

Close Dolphin and run **Configure Dolphin** again to install these bindings. Right-hand
sensors drive Wii Remote motion; left-hand acceleration drives Nunchuk motion. Tilt
comes from the changing gravity direction; physical shakes/swings produce acceleration.
The separate Motion Simulation bindings stay empty to avoid adding canned movement.
Mouse/ray pointing is unchanged, and sensor-based pointing remains disabled.

Test **Motion Input** and **Extension Motion Input** graphs: rest, slow tilt, rotation,
then shake/swing with each hand. Hold both controllers still at startup. There is no
mounting-offset calibration control in this version. Tracking-derived acceleration cannot reproduce raw IMU measurements
exactly; signs, grip angle and game sensitivity still need validation in the headset.

If the Windows cursor works but the Wii game's pointer does not, temporarily uncheck
**Enable tracked motion (DSU)** in the viewer. This sends stationary gravity and zero
gyro while retaining buttons, sticks and mouse aiming. The choice persists across
restarts and takes effect without reconfiguring Dolphin. Compare pointing with motion
off and on to isolate interaction between the game's pointer and motion interpretation.
Keep ray/mouse pointing for the current screen interaction. Dolphin can also derive
pointing from gyro/acceleration, but it needs calibration/recentering and does not directly
know where our virtual screen is. DSU does not carry a native OpenXR pose or a Wii IR packet.

## Pointer edge calibration

Expand **Dolphin pointer calibration** and enable **Direct screen pointing (IR passthrough)**.
Instead of mouse-controlled simulated rotation, this binds two IR objects to arithmetic
expressions using fully qualified Windows cursor axes. Each signed coordinate is rebuilt
from its positive/negative half axes. At the defaults, mouse X/Y in [-1,1] become
IR X `0.5 - X * 0.4` and IR Y `0.5 + Y * 0.4`, with horizontal dot offsets -0.04 and +0.04. Both dots
have size 0.2; dots 3 and 4 are disabled. This uses the existing mouse mapping and adds no
DSU channels, drivers, memory hooks or Dolphin modifications.

Direct mode bypasses Dolphin's EmulatePoint rotation/FOV conversion. The Wii game still
decides where its pointer appears, so it cannot force universal pixel-perfect placement.
Reach controls scale the IR coordinate spread (baseline horizontal 25, vertical 20),
and vertical offset shifts the dots by offset/100. Start with the defaults. In direct mode,
the controls are scaling parameters rather than physical rotation angles. Changing them
can fine-tune a particular game. In legacy mode, they set `IR/Total Pitch`, `IR/Total Yaw`
in degrees, and `IR/Vertical Offset` in cm; sensor-bar position affects offset direction.

Close Dolphin and click **Apply pointer settings**, then restart and test. Values are
saved beside the viewer, but changing a slider alone does not alter a running Dolphin.
Apply updates only pointer settings in the active Wii Remote 1 section and our existing
named profile. It preserves button/joystick/motion bindings, creates backups, and uses
the same running-process guard and rollback as full setup. It also clears the pointer
dead zone and restores square mouse calibration so previous manual calibration cannot
shrink the range. Full **Configure Dolphin** includes these pointer values too.

This is an adjustable correction; exact edge coverage still needs testing in each game.
Uncheck direct mode and apply to restore the original pointing path. The setting persists
across viewer restarts, and full Configure Dolphin uses it too. Gyro pointing stays off;
acceleration and angular velocity bindings are unchanged.

## Primary sources

- [Dolphin controller configuration guide](https://dolphin-emu.org/docs/guides/configuring-controllers/): alternate input sources and controller configuration.
- [Dolphin DSU client implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/ControllerInterface/DualShockUDPClient/DualShockUDPClient.cpp): actual exposed axis/button/motion names and scaling.
- [Dolphin DSU packet definitions](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/ControllerInterface/DualShockUDPClient/DualShockUDPProto.h): v1001 packet layouts.
- [Dolphin Nunchuk implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/Core/HW/WiimoteEmu/Extension/Nunchuk.cpp): digital C/Z, analog stick and motion groups.
- [Dolphin Wii Remote implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/Core/HW/WiimoteEmu/WiimoteEmu.cpp): motion simulation and sensor paths.
- [Dolphin cursor implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/ControllerEmu/ControlGroup/Cursor.cpp): pointer range and vertical offset settings.
- [Dolphin IR passthrough bindings](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/InputCommon/ControllerEmu/ControlGroup/IRPassthrough.cpp): infrared object positions and sizes.
