using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace OpenXRInputExplorer.Services;

/// <summary>
/// Polls capture frames on the render/UI thread, so immediate-context GPU work is serialized.
/// VR gets a GPU copy; only the slower desktop preview is read back to the CPU.
/// </summary>
public sealed class WindowCaptureService : IDisposable
{
    private readonly D3D11GraphicsService _graphics;
    private IDirect3DDevice? _captureDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _poolSize;
    private volatile bool _sourceClosed;
    private long _lastFrameTime;
    private long _nextPreviewTime;
    private ID3D11Texture2D? _previewStaging;
    private bool _previewCopyPending;

    public ID3D11Texture2D? LatestTexture { get; private set; }
    public WriteableBitmap? Preview { get; private set; }
    public bool IsCapturing => _session is not null;
    public nint WindowHandle { get; private set; }
    public string Status { get; private set; } = "Choose Dolphin's game window to show it in VR.";

    public WindowCaptureService(D3D11GraphicsService graphics) => _graphics = graphics;

    public void StartWindow(nint hwnd)
    {
        var item = CaptureInterop.CreateForWindow(hwnd);
        Start(item);
        WindowHandle = hwnd;
    }

    private void Start(GraphicsCaptureItem item)
    {
        Stop();
        try
        {
            if (!GraphicsCaptureSession.IsSupported())
                throw new NotSupportedException("Windows Graphics Capture is unavailable on this system.");
            _captureDevice = CaptureInterop.CreateCaptureDevice(_graphics.Device
                ?? throw new InvalidOperationException("The graphics device is not initialized."));
            _poolSize = item.Size;
            if (_poolSize.Width <= 0 || _poolSize.Height <= 0)
                throw new InvalidOperationException("The selected window has no capturable image. Restore it and try again.");
            _item = item;
            _sourceClosed = false;
            _item.Closed += OnSourceClosed;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_captureDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = true;
            _session.StartCapture();
            _lastFrameTime = Environment.TickCount64;
            Status = $"Waiting for frames from {item.DisplayName}. Keep the window restored.";
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void OnSourceClosed(GraphicsCaptureItem sender, object args)
    {
        if (ReferenceEquals(sender, _item)) _sourceClosed = true;
    }

    public void Update()
    {
        if (_pool is null) return;
        if (_sourceClosed)
        {
            Stop("The captured window closed. Showing the test card; choose another window to continue.");
            return;
        }

        // Drain the two-buffer pool and retain only its newest frame.
        Direct3D11CaptureFrame? frame = _pool.TryGetNextFrame();
        if (frame is null)
        {
            if (Environment.TickCount64 - _lastFrameTime > 2000)
                Status = "No new capture frames. The window may be minimized or unchanged; restore it if needed.";
            UpdatePreview();
            return;
        }

        SizeInt32 resize = default;
        try
        {
            var newer = _pool.TryGetNextFrame();
            if (newer is not null) { frame.Dispose(); frame = newer; }
            var size = frame.ContentSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                resize = size;
                Status = "Resizing the capture…";
                return; // Don't copy undefined pixels from the old buffer size.
            }

            using var source = CaptureInterop.GetTexture(frame.Surface);
            EnsureTexture(size.Width, size.Height);
            _graphics.DeviceContext!.CopySubresourceRegion(LatestTexture!, 0, 0, 0, 0, source, 0,
                new Vortice.Mathematics.Box(0, 0, 0, size.Width, size.Height, 1));
            _graphics.DeviceContext.Flush(); // Submit the copy before returning the capture buffer.
            _lastFrameTime = Environment.TickCount64;
            Status = $"Capturing {_item!.DisplayName} — {size.Width} × {size.Height}";
        }
        finally
        {
            frame.Dispose(); // Return the frame before recreating the pool.
            if (resize.Width > 0 && resize.Height > 0)
            {
                _pool.Recreate(_captureDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, resize);
                _poolSize = resize;
            }
        }
        UpdatePreview();
    }

    private void EnsureTexture(int width, int height)
    {
        if (LatestTexture is not null && LatestTexture.Description.Width == width &&
            LatestTexture.Description.Height == height) return;
        LatestTexture?.Dispose();
        _previewStaging?.Dispose();
        _previewStaging = null;
        _previewCopyPending = false;
        Preview = null;
        var description = new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.None
        };
        LatestTexture = _graphics.Device!.CreateTexture2D(description);
    }

    private void UpdatePreview()
    {
        if (LatestTexture is null) return;
        var context = _graphics.DeviceContext!;
        if (_previewCopyPending)
        {
            var result = context.Map(_previewStaging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out var mapped);
            if (result.Code == unchecked((int)0x887A000A)) return; // GPU still drawing; never stall VR for the preview.
            result.CheckError();
            try
            {
                int width = (int)LatestTexture.Description.Width;
                int height = (int)LatestTexture.Description.Height;
                Preview ??= new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                Preview.WritePixels(new Int32Rect(0, 0, width, height), mapped.DataPointer,
                    checked((int)mapped.RowPitch * height), (int)mapped.RowPitch);
            }
            finally { context.Unmap(_previewStaging!, 0); }
            _previewCopyPending = false;
        }

        if (Environment.TickCount64 < _nextPreviewTime) return;
        if (_previewStaging is null)
        {
            var description = LatestTexture.Description;
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CPUAccessFlags = CpuAccessFlags.Read;
            _previewStaging = _graphics.Device!.CreateTexture2D(description);
        }
        context.CopyResource(_previewStaging, LatestTexture);
        context.Flush();
        _previewCopyPending = true;
        _nextPreviewTime = Environment.TickCount64 + 100; // Desktop preview at up to 10 Hz; VR uses every available frame.
    }

    public void Stop(string status = "Capture stopped. Showing the test card.")
    {
        if (_item is not null) _item.Closed -= OnSourceClosed;
        _session?.Dispose();
        _session = null;
        _pool?.Dispose();
        _pool = null;
        _item = null;
        WindowHandle = 0;
        _captureDevice?.Dispose();
        _captureDevice = null;
        LatestTexture?.Dispose();
        LatestTexture = null;
        _previewStaging?.Dispose();
        _previewStaging = null;
        Preview = null;
        _previewCopyPending = false;
        _nextPreviewTime = 0;
        Status = status;
    }

    public void Dispose() => Stop();

    public void ReportError(string message) => Status = message;
}
