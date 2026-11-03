using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace OpenXRInputExplorer.Services
{
    /// <summary>
    /// Creates a minimal Direct3D 11 device.
    /// OpenXR needs a graphics device before it can create a session.
    /// </summary>
    public sealed class D3D11GraphicsService : IDisposable
    {
        public ID3D11Device? Device { get; private set; }
        public ID3D11DeviceContext? DeviceContext { get; private set; }

        public bool IsInitialized => Device != null;

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            FeatureLevel[] featureLevels =
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };

            D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out ID3D11Device device,
                out ID3D11DeviceContext deviceContext);

            Device = device;
            DeviceContext = deviceContext;
        }

        public void Dispose()
        {
            DeviceContext?.Dispose();
            Device?.Dispose();

            DeviceContext = null;
            Device = null;
        }
    }
}
