using Silk.NET.OpenXR;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenXRInputExplorer.Services;

public unsafe partial class OpenXRService
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private Swapchain _screenSwapchain;
    private SwapchainImageD3D11KHR[] _screenImages = [];
    private byte[] _screenPixels = [];
    private bool[] _screenImageUploaded = [];
    private Format _screenFormat;
    private int _screenTextureWidth;
    private int _screenTextureHeight;
    private ID3D11Texture2D? _capturedScreen;
    private bool _disposed;

    // Non-owning reference. Capture and rendering are serialized on the same thread.
    public void SetCapturedScreen(ID3D11Texture2D? texture)
    {
        if (texture is not null && _screenFormat is not (Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb))
            throw new NotSupportedException("This runtime does not expose a BGRA swapchain for window capture.");
        _capturedScreen = texture;
    }

    private static void CheckXr(Result result, string operation)
    {
        if ((int)result < 0)
            throw new InvalidOperationException($"{operation} failed: {result}");
    }

    private void CreateTestScreen()
    {
        uint count = 0;
        CheckXr(_xr.EnumerateSwapchainFormats(_session, 0, &count, null), "xrEnumerateSwapchainFormats");
        var formats = new long[count];
        fixed (long* values = formats)
            CheckXr(_xr.EnumerateSwapchainFormats(_session, count, &count, values), "xrEnumerateSwapchainFormats");

        // WPF produces BGRA bytes; prefer sRGB so the compositor displays the card correctly.
        var format = new[] { Format.B8G8R8A8_UNorm_SRgb, Format.R8G8B8A8_UNorm_SRgb,
            Format.B8G8R8A8_UNorm, Format.R8G8B8A8_UNorm }
            .FirstOrDefault(candidate => formats.Contains((long)candidate));
        if (format == Format.Unknown)
            throw new InvalidOperationException("The OpenXR runtime has no supported RGBA/BGRA screen format.");
        _screenFormat = format;

        _screenPixels = CreateTestCard();
        if (format is Format.R8G8B8A8_UNorm_SRgb or Format.R8G8B8A8_UNorm)
            for (int i = 0; i < _screenPixels.Length; i += 4)
                (_screenPixels[i], _screenPixels[i + 2]) = (_screenPixels[i + 2], _screenPixels[i]);

        CreateScreenSwapchain(ScreenWidth, ScreenHeight);
    }

    private void CreateScreenSwapchain(int width, int height)
    {
        if (_screenSwapchain.Handle != 0)
        {
            CheckXr(_xr.DestroySwapchain(_screenSwapchain), "xrDestroySwapchain");
            _screenSwapchain = default;
        }
        var info = new SwapchainCreateInfo
        {
            Type = StructureType.SwapchainCreateInfo,
            UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
            Format = (long)_screenFormat,
            SampleCount = 1,
            Width = (uint)width,
            Height = (uint)height,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1
        };
        fixed (Swapchain* swapchain = &_screenSwapchain)
            CheckXr(_xr.CreateSwapchain(_session, &info, swapchain), "xrCreateSwapchain");

        uint count = 0;
        CheckXr(_xr.EnumerateSwapchainImages(_screenSwapchain, 0, &count, null), "xrEnumerateSwapchainImages");
        _screenImages = new SwapchainImageD3D11KHR[count];
        _screenImageUploaded = new bool[count];
        for (int i = 0; i < count; i++)
            _screenImages[i].Type = StructureType.SwapchainImageD3D11Khr;
        fixed (SwapchainImageD3D11KHR* images = _screenImages)
            CheckXr(_xr.EnumerateSwapchainImages(_screenSwapchain, count, &count,
                (SwapchainImageBaseHeader*)images), "xrEnumerateSwapchainImages");
        _screenTextureWidth = width;
        _screenTextureHeight = height;
    }

    private void RenderTestScreen()
    {
        int width = _capturedScreen is null ? ScreenWidth : (int)_capturedScreen.Description.Width;
        int height = _capturedScreen is null ? ScreenHeight : (int)_capturedScreen.Description.Height;
        if (width != _screenTextureWidth || height != _screenTextureHeight)
            CreateScreenSwapchain(width, height);
        uint index = 0;
        var acquire = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
        CheckXr(_xr.AcquireSwapchainImage(_screenSwapchain, &acquire, &index), "xrAcquireSwapchainImage");
        var wait = new SwapchainImageWaitInfo
        {
            Type = StructureType.SwapchainImageWaitInfo,
            Timeout = long.MaxValue
        };
        CheckXr(_xr.WaitSwapchainImage(_screenSwapchain, &wait), "xrWaitSwapchainImage");
        try
        {
            if (_capturedScreen is not null || !_screenImageUploaded[index])
            {
                // The runtime owns the texture. Give the temporary COM wrapper its own reference.
                nint pointer = (nint)_screenImages[index].Texture;
                Marshal.AddRef(pointer);
                using var texture = new ID3D11Texture2D(pointer);
                if (_capturedScreen is not null)
                    _graphicsService.DeviceContext!.CopyResource(texture, _capturedScreen);
                else
                    fixed (byte* pixels = _screenPixels)
                        _graphicsService.DeviceContext!.UpdateSubresource(texture, 0, null,
                            (nint)pixels, ScreenWidth * 4, 0);
                _graphicsService.DeviceContext!.Flush();
                _screenImageUploaded[index] = _capturedScreen is null;
            }
        }
        finally
        {
            var release = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
            CheckXr(_xr.ReleaseSwapchainImage(_screenSwapchain, &release), "xrReleaseSwapchainImage");
        }
    }

    private CompositionLayerQuad CreateTestScreenLayer() => new()
    {
        Type = StructureType.CompositionLayerQuad,
        Space = _localSpace,
        EyeVisibility = EyeVisibility.Both,
        SubImage = new SwapchainSubImage
        {
            Swapchain = _screenSwapchain,
            ImageRect = new Rect2Di { Extent = new Extent2Di(_screenTextureWidth, _screenTextureHeight) }
        },
        Pose = new Posef
        {
            Orientation = new Quaternionf(_screenPlacement.Orientation.X, _screenPlacement.Orientation.Y,
                _screenPlacement.Orientation.Z, _screenPlacement.Orientation.W),
            Position = new Vector3f(_screenPlacement.Position.X, _screenPlacement.Position.Y, _screenPlacement.Position.Z)
        },
        Size = new Extent2Df(_screenPlacement.Width,
            _screenPlacement.Width * _screenTextureHeight / _screenTextureWidth)
    };

    private static byte[] CreateTestCard()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 28, 46)), null,
                new Rect(0, 0, ScreenWidth, ScreenHeight));
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 57, 78)), 2);
            for (int x = 0; x <= ScreenWidth; x += 80)
                drawing.DrawLine(gridPen, new Point(x, 0), new Point(x, ScreenHeight));
            for (int y = 0; y <= ScreenHeight; y += 80)
                drawing.DrawLine(gridPen, new Point(0, y), new Point(ScreenWidth, y));
            drawing.DrawRectangle(null, new Pen(Brushes.Cyan, 12), new Rect(6, 6, 1268, 708));

            void Text(string text, double y, double size, Brush color)
            {
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1);
                drawing.DrawText(formatted, new Point((ScreenWidth - formatted.Width) / 2, y));
            }
            Text("Rift2Wii", 145, 64, Brushes.White);
            Text("VR SCREEN TEST", 245, 48, Brushes.Cyan);
            Text("Move your head: this screen stays in place.", 355, 32, Brushes.White);
            Text("Press Touch buttons and check the desktop input viewer.", 410, 30, Brushes.White);
            Text("Choose a window in the desktop app to view it here", 570, 28, Brushes.LightGray);
        }
        var bitmap = new RenderTargetBitmap(ScreenWidth, ScreenHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[ScreenWidth * ScreenHeight * 4];
        bitmap.CopyPixels(pixels, ScreenWidth * 4, 0);
        // This is an opaque screen; do not retain WPF's text antialiasing alpha.
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        return pixels;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isSessionStarted = false;
        _isSessionRunning = false;
        if (_screenSwapchain.Handle != 0) _xr.DestroySwapchain(_screenSwapchain);
        if (_leftGripSpace.Handle != 0) _xr.DestroySpace(_leftGripSpace);
        if (_rightGripSpace.Handle != 0) _xr.DestroySpace(_rightGripSpace);
        if (_rightAimSpace.Handle != 0) _xr.DestroySpace(_rightAimSpace);
        if (_viewSpace.Handle != 0) _xr.DestroySpace(_viewSpace);
        if (_localSpace.Handle != 0) _xr.DestroySpace(_localSpace);
        if (_session.Handle != 0) _xr.DestroySession(_session);
        if (_actionSet.Handle != 0) _xr.DestroyActionSet(_actionSet);
        if (_instance.Handle != 0) _xr.DestroyInstance(_instance);
        _xr.Dispose();
        _isInitialized = false;
    }
}
