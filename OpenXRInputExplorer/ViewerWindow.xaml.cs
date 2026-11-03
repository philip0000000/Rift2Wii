using System.Windows;
using System.Windows.Threading;
using OpenXRInputExplorer.Services;
using Windows.Graphics.Capture;

namespace OpenXRInputExplorer
{
    /// <summary>
    /// Interaction logic for ViewerWindow.xaml
    /// </summary>
    public partial class ViewerWindow : Window
    {
        private readonly DispatcherTimer _timer;

        private readonly D3D11GraphicsService _graphicsService = new();
        private readonly OpenXRService _openXrService;
        private readonly WindowCaptureService _captureService;
        private bool _windowClosed;

        public ViewerWindow()
        {
            InitializeComponent();
            InitializeDesktopControls();

            _openXrService = new OpenXRService(_graphicsService);
            _captureService = new WindowCaptureService(_graphicsService);
            _openXrService.SetScreenDimensions((float)ScreenWidthSlider.Value, (float)ScreenDistanceSlider.Value);

            Closed += (_, _) =>
            {
                SaveConfiguration();
                _windowClosed = true;
                _timer?.Stop();
                ReleaseDesktopOutput();
                StopDsu();
                _openXrService.SetCapturedScreen(null);
                _captureService.Dispose();
                _openXrService.Dispose();
                _graphicsService.Dispose();
            };

            Title = "Rift2Wii";

            AddStatus("Application started.");

            _timer = new DispatcherTimer();
            // xrWaitFrame paces updates to the headset; avoid another 50 ms between frames.
            _timer.Interval = TimeSpan.FromMilliseconds(1);
            _timer.Tick += Timer_Tick;
            InitializeConfiguration();
            try
            {
                InitializeVr();
                RestoreConfiguredOutput();
                _timer.Start();
                ChooseWindowButton.IsEnabled = GraphicsCaptureSession.IsSupported();
                if (!ChooseWindowButton.IsEnabled)
                    CaptureStatusText.Text = "Windows Graphics Capture is unavailable on this system.";
                AddStatus("UI update timer started.");
            }
            catch (Exception exception)
            {
                _openXrService.Dispose();
                _graphicsService.Dispose();
                AddStatus($"VR initialization failed: {exception.Message}. Close and restart the viewer to retry.");
            }
        }

        private void InitializeVr()
        {
            AddStatus("Initializing D3D11...");
            _graphicsService.Initialize();
            AddStatus("D3D11 initialized.");

            AddStatus("Initializing OpenXR...");
            _openXrService.Initialize();
            AddStatus(_openXrService.GetStatus());

            // Debug block
            //AddStatus("Available OpenXR Extensions:");
            //AddStatus(_openXrService.ListExtensions());

            AddStatus("Starting OpenXR session...");
            _openXrService.StartSession();
            AddStatus("OpenXR session started.");
            AddStatus("VR test screen ready. Put on the headset; use the VR screen controls to resize, move or recenter it.");

        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _captureService.Update();
                _openXrService.SetCapturedScreen(_captureService.LatestTexture);
            }
            catch (Exception exception)
            {
                _openXrService.SetCapturedScreen(null);
                _captureService.Stop($"Capture stopped: {exception.Message}");
                AddStatus(_captureService.Status);
            }
            UpdateCaptureControls();
            try
            {
                _openXrService.Update();
            }
            catch (Exception exception)
            {
                _timer.Stop();
                ReleaseDesktopOutput();
                _openXrService.SetCapturedScreen(null);
                _captureService.Stop();
                UpdateCaptureControls();
                ChooseWindowButton.IsEnabled = false;
                AddStatus($"VR updates stopped: {exception.Message}. Close and restart the viewer to retry.");
                return;
            }

            var left = _openXrService.GetLeftController();
            var right = _openXrService.GetRightController();
            UpdateDesktopOutput();
            ScreenPlacementStatusText.Text = _openXrService.ScreenPlacementStatus;

            LeftPosXText.Text = $"X: {left.PositionX:F3}";
            LeftPosYText.Text = $"Y: {left.PositionY:F3}";
            LeftPosZText.Text = $"Z: {left.PositionZ:F3}";

            RightPosXText.Text = $"X: {right.PositionX:F3}";
            RightPosYText.Text = $"Y: {right.PositionY:F3}";
            RightPosZText.Text = $"Z: {right.PositionZ:F3}";

            LeftPitchText.Text = $"Pitch: {left.Pitch:F1}";
            LeftYawText.Text = $"Yaw: {left.Yaw:F1}";
            LeftRollText.Text = $"Roll: {left.Roll:F1}";

            RightPitchText.Text = $"Pitch: {right.Pitch:F1}";
            RightYawText.Text = $"Yaw: {right.Yaw:F1}";
            RightRollText.Text = $"Roll: {right.Roll:F1}";

            LeftTriggerBar.Value = left.Trigger * 100;
            RightTriggerBar.Value = right.Trigger * 100;

            LeftGripBar.Value = left.Grip * 100;
            RightGripBar.Value = right.Grip * 100;

            LeftThumbstickXText.Text = $"X: {left.ThumbstickX:F3}";
            LeftThumbstickYText.Text = $"Y: {left.ThumbstickY:F3}";

            RightThumbstickXText.Text = $"X: {right.ThumbstickX:F3}";
            RightThumbstickYText.Text = $"Y: {right.ThumbstickY:F3}";

            LeftXButtonText.Text =
                $"X: {left.X}";

            LeftYButtonText.Text =
                $"Y: {left.Y}";

            LeftThumbstickClickText.Text =
                $"Stick: {left.ThumbstickClick}";

            RightAButtonText.Text =
                $"A: {right.A}";

            RightBButtonText.Text =
                $"B: {right.B}";

            RightThumbstickClickText.Text =
                $"Stick: {right.ThumbstickClick}";
        }

        private void ChooseWindowClicked(object sender, RoutedEventArgs e)
        {
            ChooseWindowButton.IsEnabled = false;
            _desktopInputSuspended = true;
            ReleaseDesktopOutput();
            try
            {
                var picker = new WindowPickerWindow { Owner = this };
                // Cancellation leaves the current source unchanged.
                if (picker.ShowDialog() != true || _windowClosed || !_timer.IsEnabled) return;
                _openXrService.SetCapturedScreen(null);
                _captureService.StartWindow(picker.SelectedWindow);
                AddStatus("Window capture started for the selected window.");
                UpdateCaptureControls();
            }
            catch (Exception exception)
            {
                if (_windowClosed) return;
                AddStatus($"Could not select/start capture: {exception.Message}");
                _captureService.ReportError($"Could not select/start capture: {exception.Message}");
                UpdateCaptureControls();
            }
            finally
            {
                _desktopInputSuspended = false;
                if (!_windowClosed) ChooseWindowButton.IsEnabled = _timer.IsEnabled;
            }
        }

        private void StopCaptureClicked(object sender, RoutedEventArgs e)
        {
            ReleaseDesktopOutput();
            _openXrService.SetCapturedScreen(null);
            _captureService.Stop();
            UpdateCaptureControls();
            AddStatus("Window capture stopped. Showing the test card.");
        }

        private void UpdateCaptureControls()
        {
            CaptureStatusText.Text = _captureService.Status;
            StopCaptureButton.IsEnabled = _captureService.IsCapturing;
            CapturePreviewImage.Source = _captureService.Preview;
            CapturePreviewPlaceholder.Visibility = _captureService.Preview is null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScreenDimensionsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // ValueChanged also runs while InitializeComponent is constructing the controls.
            if (_openXrService is null || ScreenWidthSlider is null || ScreenDistanceSlider is null) return;
            _openXrService.SetScreenDimensions((float)ScreenWidthSlider.Value, (float)ScreenDistanceSlider.Value);
            QueueConfigurationSave();
        }

        private void RecenterScreenClicked(object sender, RoutedEventArgs e)
        {
            if (!_timer.IsEnabled)
            {
                AddStatus("Recenter unavailable: VR is not running. Restart the viewer to retry.");
                return;
            }
            ReleaseDesktopOutput();
            _openXrService.RequestScreenRecenter();
            ScreenPlacementStatusText.Text = _openXrService.ScreenPlacementStatus;
            AddStatus("Screen recenter requested. Look forward in the headset in 3 seconds.");
        }

        private void AddStatus(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            StatusLogTextBox.AppendText(
                $"[{timestamp}] {message}{Environment.NewLine}");

            StatusLogTextBox.ScrollToEnd();
        }
    }
}
