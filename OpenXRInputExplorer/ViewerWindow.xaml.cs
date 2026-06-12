using System.Windows;
using System.Windows.Threading;
using OpenXRInputExplorer.Services;

namespace OpenXRInputExplorer
{
    /// <summary>
    /// Interaction logic for ViewerWindow.xaml
    /// </summary>
    public partial class ViewerWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly Random _random = new();

        private readonly D3D11GraphicsService _graphicsService = new();
        private readonly OpenXRService _openXrService;

        public ViewerWindow()
        {
            InitializeComponent();

            _openXrService = new OpenXRService(_graphicsService);

            Title = "OpenXRInputExplorer Viewer";

            AddStatus("Application started.");

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

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            AddStatus("UI update timer started.");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _openXrService.Update();

            var left = _openXrService.GetLeftController();
            var right = _openXrService.GetRightController();

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

        private void AddStatus(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            StatusLogTextBox.AppendText(
                $"[{timestamp}] {message}{Environment.NewLine}");

            StatusLogTextBox.ScrollToEnd();
        }
    }
}
