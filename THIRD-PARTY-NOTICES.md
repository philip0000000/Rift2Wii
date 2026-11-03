# Third-party notices

Rift2Wii's own source is MIT-licensed; see LICENSE. The application uses the following
third-party packages. Their license texts are included in the `licenses` directory.
The .NET Desktop Runtime and Dolphin Emulator are installed separately, not bundled.

| Component | Version | License / notice |
| --- | --- | --- |
| Silk.NET.Core, Silk.NET.Maths, Silk.NET.OpenXR | 2.23.0 | MIT; .NET Foundation and Contributors; `licenses/Silk.NET-MIT.txt` |
| Vortice.Direct3D11, Vortice.DXGI, Vortice.DirectX | 3.8.3 | MIT; Amer Koleci and contributors; `licenses/Vortice-MIT.txt` |
| Vortice.Mathematics | 2.1.0 | MIT; Amer Koleci and Contributors; `licenses/Vortice.Mathematics-MIT.txt` |
| SharpGen.Runtime, SharpGen.Runtime.COM | 2.4.2-beta | MIT; Alexandre Mutel, Jeremy Koritzinsky, Amer Koleci; `licenses/SharpGen-MIT.txt` |
| OpenXR.Loader, OpenXR.Headers | 1.0.10.2 | Apache-2.0; The Khronos Group Inc.; `licenses/OpenXR-Apache-2.0.txt` |
| Microsoft.CSharp | 4.7.0 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| Microsoft.DotNet.PlatformAbstractions | 3.1.6 | MIT; `licenses/DotNet-MIT.txt`; additional notices in `licenses/DotNet-PlatformAbstractions-NOTICES.txt` |
| Microsoft.Extensions.DependencyModel | 9.0.9 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| System.IO.Pipelines | 9.0.9 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| System.Memory | 4.6.3 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| System.Numerics.Vectors | 4.6.1 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| System.Text.Encodings.Web, System.Text.Json | 9.0.9 | MIT; Microsoft Corporation / .NET Foundation and Contributors |
| Microsoft.Windows.SDK.NET.dll | SDK package 10.0.19041.57 | Microsoft Windows SDK license; `licenses/Windows-SDK-License.rtf` |
| WinRT.Runtime.dll | 2.2.0.0 | Microsoft C#/WinRT; MIT; `licenses/CsWinRT-MIT.txt`; distributed with the Windows SDK targeting pack |

Microsoft/.NET MIT terms are in `licenses/DotNet-MIT.txt`. Build-time OpenXR headers
are not shipped as application binaries. The native OpenXR loader DLL is redistributed
unmodified from its pinned NuGet package under Apache-2.0. The Windows SDK projection and
WinRT runtime are supplied by Microsoft.Windows.SDK.NET.Ref 10.0.19041.57. The SDK license
is included in its original RTF format. Microsoft Corporation retains all applicable rights.

Upstream projects:

- https://github.com/dotnet/Silk.NET
- https://github.com/amerkoleci/Vortice.Windows
- https://github.com/amerkoleci/Vortice.Mathematics
- https://github.com/SharpGenTools/SharpGenTools
- https://github.com/KhronosGroup/OpenXR-SDK
- https://github.com/dotnet/runtime

No Dolphin source code or executable is bundled. Rift2Wii communicates through Dolphin's
configuration files, keyboard/mouse input and its DSU protocol interface.
