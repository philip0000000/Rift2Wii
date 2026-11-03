using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Capture;
using WinRT;

namespace OpenXRInputExplorer.Services;

/// <summary>Bridges Windows Runtime capture surfaces and the existing native D3D11 device.</summary>
internal static unsafe class CaptureInterop
{
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, int length, out nint result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);

    public static GraphicsCaptureItem CreateForWindow(nint hwnd)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        nint factory = 0, item = 0;
        try
        {
            var factoryId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref factoryId, out factory));
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            Marshal.ThrowExceptionForHR(create(factory, hwnd, &itemId, &item));
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(hstring);
        }
    }

    public static IDirect3DDevice CreateCaptureDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pointer));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        nint surfacePointer = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        nint access = 0;
        try
        {
            var accessId = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePointer, ref accessId, out access));
            var textureId = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
            nint texture = 0;
            // IDirect3DDxgiInterfaceAccess::GetInterface follows the three IUnknown methods.
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(getInterface(access, &textureId, &texture));
            return new ID3D11Texture2D(texture); // GetInterface supplied an owned COM reference.
        }
        finally
        {
            if (access != 0) Marshal.Release(access);
            Marshal.Release(surfacePointer);
        }
    }
}
