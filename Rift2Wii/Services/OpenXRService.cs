using OpenXRInputExplorer.Models;
using Silk.NET.Core;
using Silk.NET.OpenXR;
using System.Text;
using Vortice.Direct3D11;

namespace OpenXRInputExplorer.Services
{
    /// <summary>
    /// Handles communication with OpenXR.
    /// This class should be the only place that talks directly to OpenXR.
    /// </summary>
    public unsafe partial class OpenXRService : IDisposable
    {
        private readonly XR _xr;

        private Instance _instance;
        private ulong _systemId;

        private bool _isInitialized;

        /// <summary>
        /// Returns true when OpenXR has been initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        public string DeviceName { get; private set; } = "Unknown";
        public ulong SystemId => _systemId;

        private Session _session;
        private bool _isSessionStarted;

        private readonly D3D11GraphicsService _graphicsService;
        private const string D3D11ExtensionName = "XR_KHR_D3D11_enable";

        private delegate* unmanaged[Stdcall]<Instance, ulong, GraphicsRequirementsD3D11KHR*, Result>
            _getD3D11GraphicsRequirements;

        private Space _localSpace;

        private ActionSet _actionSet;
        private Silk.NET.OpenXR.Action _gripPoseAction;
        private ulong _leftHandPath;
        private ulong _rightHandPath;
        private Space _leftGripSpace;
        private Space _rightGripSpace;

        private readonly ControllerState _leftControllerState = new();
        private readonly ControllerState _rightControllerState = new();

        private long _predictedDisplayTime;

        private SessionState _sessionState;
        private bool _isSessionRunning;
        public bool ControllerActionsAvailable { get; private set; }

        private Silk.NET.OpenXR.Action _gripValueAction;
        private Silk.NET.OpenXR.Action _triggerValueAction;

        private Silk.NET.OpenXR.Action _thumbstickAction;

        private Silk.NET.OpenXR.Action _xButtonAction;
        private Silk.NET.OpenXR.Action _yButtonAction;
        private Silk.NET.OpenXR.Action _aButtonAction;
        private Silk.NET.OpenXR.Action _bButtonAction;
        private Silk.NET.OpenXR.Action _thumbstickClickAction;

        public OpenXRService(D3D11GraphicsService graphicsService)
        {
            _graphicsService = graphicsService;
            _xr = XR.GetApi();
        }

        /// <summary>
        /// Initializes OpenXR and reads basic device information.
        /// This should only be called once during application startup.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            CreateInstance();
            LoadD3D11GraphicsRequirementsFunction();
            GetSystem();

            DeviceName = GetSystemProperties();

            _isInitialized = true;
        }

        /// <summary>
        /// Returns current OpenXR status.
        /// </summary>
        public string GetStatus()
        {
            if (!_isInitialized)
            {
                return "OpenXR not initialized.";
            }

            return $"SystemId: {_systemId} | Device: {DeviceName}";
        }

        private unsafe void GetSystem()
        {
            var systemInfo = new SystemGetInfo
            {
                Type = StructureType.SystemGetInfo,
                FormFactor = FormFactor.HeadMountedDisplay
            };

            fixed (ulong* systemIdPtr = &_systemId)
            {
                Result result =
                    _xr.GetSystem(_instance, &systemInfo, systemIdPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrGetSystem failed: {result}");
                }
            }
        }

        private void CreateInstance()
        {
            if (_instance.Handle != 0)
            {
                return;
            }

            var applicationInfo = new ApplicationInfo
            {
                ApplicationVersion = 1,
                EngineVersion = 1,
                ApiVersion = MakeOpenXRVersion(1, 0, 0)
            };

            WriteFixedString(applicationInfo.ApplicationName, "Rift2Wii");
            WriteFixedString(applicationInfo.EngineName, "Rift2Wii");

            byte[] d3d11ExtensionBytes =
                Encoding.UTF8.GetBytes(D3D11ExtensionName + "\0");

            fixed (byte* d3d11ExtensionNamePtr = d3d11ExtensionBytes)
            {
                byte** enabledExtensions = stackalloc byte*[1];
                enabledExtensions[0] = d3d11ExtensionNamePtr;

                var createInfo = new InstanceCreateInfo
                {
                    Type = StructureType.InstanceCreateInfo,
                    ApplicationInfo = applicationInfo,
                    EnabledExtensionCount = 1,
                    EnabledExtensionNames = enabledExtensions
                };

                fixed (Instance* instancePtr = &_instance)
                {
                    Result result = _xr.CreateInstance(&createInfo, instancePtr);

                    if (result != Result.Success)
                    {
                        throw new InvalidOperationException(
                            $"xrCreateInstance failed: {result}");
                    }
                }
            }
        }

        public void Update()
        {
            RightInputAvailable = false;
            ControllerActionsAvailable = false;
            _leftControllerState.IsTracked = false;
            _rightControllerState.IsTracked = false;
            if (!_isSessionStarted)
            {
                return;
            }

            PollEvents();

            if (!_isSessionRunning)
            {
                return;
            }

            var frameWaitInfo = new FrameWaitInfo
            {
                Type = StructureType.FrameWaitInfo
            };

            var frameState = new FrameState
            {
                Type = StructureType.FrameState
            };

            Result result = _xr.WaitFrame(
                _session,
                &frameWaitInfo,
                &frameState);

            CheckXr(result, "xrWaitFrame");

            _predictedDisplayTime =
                (long)frameState.PredictedDisplayTime;

            var frameBeginInfo = new FrameBeginInfo
            {
                Type = StructureType.FrameBeginInfo
            };

            result = _xr.BeginFrame(
                _session,
                &frameBeginInfo);

            // FrameDiscarded is a success code and still requires a matching EndFrame.
            CheckXr(result, "xrBeginFrame");

            var frameEndInfo = new FrameEndInfo
            {
                Type = StructureType.FrameEndInfo,
                DisplayTime = frameState.PredictedDisplayTime,
                EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                LayerCount = 0,
                Layers = null
            };

            try
            {
                if (SyncActions())
                {
                    UpdateControllerPoses();
                    ControllerActionsAvailable = frameState.ShouldRender != 0;
                    if (frameState.ShouldRender != 0) UpdateRightAim();
                }

                if (frameState.ShouldRender != 0)
                {
                    UpdateScreenPlacement();
                    RenderTestScreen();
                    var quad = CreateTestScreenLayer();
                    CompositionLayerBaseHeader* layer = (CompositionLayerBaseHeader*)&quad;
                    frameEndInfo.LayerCount = 1;
                    frameEndInfo.Layers = &layer;
                    // End here while the quad and pointer array are still in scope.
                    CheckXr(_xr.EndFrame(_session, &frameEndInfo), "xrEndFrame");
                }
            }
            finally
            {
                // No rendering requested, or preparation failed: finish with no layers.
                if (frameEndInfo.LayerCount == 0)
                    _xr.EndFrame(_session, &frameEndInfo);
            }
        }

        private static ulong MakeOpenXRVersion(uint major, uint minor, uint patch)
        {
            return ((ulong)major << 48) | ((ulong)minor << 32) | patch;
        }

        private static void WriteFixedString(byte* destination, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);

            for (int i = 0; i < bytes.Length; i++)
            {
                destination[i] = bytes[i];
            }

            destination[bytes.Length] = 0;
        }

        private string GetSystemProperties()
        {
            var properties = new SystemProperties
            {
                Type = StructureType.SystemProperties
            };

            Result result = _xr.GetSystemProperties(_instance, _systemId, &properties);

            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"xrGetSystemProperties failed: {result}");
            }

            string systemName = ReadFixedString(properties.SystemName);

            return systemName;
        }

        private static string ReadFixedString(byte* source)
        {
            int length = 0;

            while (source[length] != 0)
            {
                length++;
            }

            return Encoding.UTF8.GetString(source, length);
        }

        private void CreateSession()
        {
            GetD3D11GraphicsRequirements();

            if (_graphicsService.Device == null)
            {
                throw new InvalidOperationException(
                    "D3D11 device is not initialized.");
            }

            nint nativeDevice = _graphicsService.Device.NativePointer;

            var d3d11Binding = new GraphicsBindingD3D11KHR
            {
                Type = StructureType.GraphicsBindingD3D11Khr,
                Device = (void*)nativeDevice
            };

            var sessionCreateInfo = new SessionCreateInfo
            {
                Type = StructureType.SessionCreateInfo,
                Next = &d3d11Binding,
                SystemId = _systemId
            };

            fixed (Session* sessionPtr = &_session)
            {
                Result result = _xr.CreateSession(
                    _instance,
                    &sessionCreateInfo,
                    sessionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateSession failed: {result}");
                }
            }
        }

        /// <summary>
        /// Starts an OpenXR session.
        /// This is required before we can read tracking data.
        /// </summary>
        public void StartSession()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("OpenXR must be initialized before starting a session.");
            }

            if (_isSessionStarted)
            {
                return;
            }

            CreateSession();
            CreateReferenceSpace();
            CreateViewSpace();

            CreateHandPaths();
            CreateActionSet();
            CreateGripPoseAction();
            CreateRightAimAction();

            CreateTriggerValueAction();
            CreateGripValueAction();

            CreateThumbstickAction();

            CreateButtonActions();

            SuggestOculusTouchBindings();
            AttachActionSet();
            CreateGripActionSpaces();
            CreateRightAimSpace();
            CreateTestScreen();

            _isSessionStarted = true;
        }

        public unsafe string ListExtensions()
        {
            uint extensionCount = 0;

            Result result = _xr.EnumerateInstanceExtensionProperties(
                (byte*)0,
                0,
                &extensionCount,
                null);

            if (result != Result.Success)
            {
                return $"Failed: {result}";
            }

            var extensions = new ExtensionProperties[extensionCount];

            for (int i = 0; i < extensions.Length; i++)
            {
                extensions[i].Type =
                    StructureType.ExtensionProperties;
            }

            fixed (ExtensionProperties* extensionsPtr = extensions)
            {
                result = _xr.EnumerateInstanceExtensionProperties(
                    (byte*)0,
                    extensionCount,
                    &extensionCount,
                    extensionsPtr);

                if (result != Result.Success)
                {
                    return $"Failed: {result}";
                }

                var builder = new StringBuilder();

                foreach (var extension in extensions)
                {
                    builder.AppendLine(
                        ReadFixedString(extension.ExtensionName));
                }

                return builder.ToString();
            }
        }

        private void LoadD3D11GraphicsRequirementsFunction()
        {
            byte[] functionNameBytes =
                Encoding.UTF8.GetBytes("xrGetD3D11GraphicsRequirementsKHR\0");

            fixed (byte* functionNamePtr = functionNameBytes)
            {
                PfnVoidFunction functionPointer;

                Result result = _xr.GetInstanceProcAddr(
                    _instance,
                    functionNamePtr,
                    &functionPointer);

                if (result != Result.Success || functionPointer.Handle == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load xrGetD3D11GraphicsRequirementsKHR: {result}");
                }

                _getD3D11GraphicsRequirements =
                    (delegate* unmanaged[Stdcall]<Instance, ulong, GraphicsRequirementsD3D11KHR*, Result>)
                    functionPointer.Handle;
            }
        }

        private void GetD3D11GraphicsRequirements()
        {
            var requirements = new GraphicsRequirementsD3D11KHR
            {
                Type = StructureType.GraphicsRequirementsD3D11Khr
            };

            Result result = _getD3D11GraphicsRequirements(
                _instance,
                _systemId,
                &requirements);

            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"xrGetD3D11GraphicsRequirementsKHR failed: {result}");
            }
        }

        private void CreateReferenceSpace()
        {
            var pose = new Posef
            {
                Orientation = new Quaternionf
                {
                    X = 0,
                    Y = 0,
                    Z = 0,
                    W = 1
                },
                Position = new Vector3f
                {
                    X = 0,
                    Y = 0,
                    Z = 0
                }
            };

            var createInfo = new ReferenceSpaceCreateInfo
            {
                Type = StructureType.ReferenceSpaceCreateInfo,
                ReferenceSpaceType = ReferenceSpaceType.Local,
                PoseInReferenceSpace = pose
            };

            fixed (Space* spacePtr = &_localSpace)
            {
                Result result = _xr.CreateReferenceSpace(
                    _session,
                    &createInfo,
                    spacePtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateReferenceSpace failed: {result}");
                }
            }
        }

        private void CreateActionSet()
        {
            var createInfo = new ActionSetCreateInfo
            {
                Type = StructureType.ActionSetCreateInfo,
                Priority = 0
            };

            WriteFixedString(createInfo.ActionSetName, "gameplay");
            WriteFixedString(createInfo.LocalizedActionSetName, "Gameplay");

            fixed (ActionSet* actionSetPtr = &_actionSet)
            {
                Result result = _xr.CreateActionSet(
                    _instance,
                    &createInfo,
                    actionSetPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateActionSet failed: {result}");
                }
            }
        }

        private void CreateHandPaths()
        {
            StringToPath("/user/hand/left", out _leftHandPath);
            StringToPath("/user/hand/right", out _rightHandPath);
        }

        private void StringToPath(string pathText, out ulong path)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(pathText + "\0");

            fixed (byte* pathPtr = pathBytes)
            fixed (ulong* resultPathPtr = &path)
            {
                Result result = _xr.StringToPath(
                    _instance,
                    pathPtr,
                    resultPathPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrStringToPath failed for '{pathText}': {result}");
                }
            }
        }

        private void CreateGripPoseAction()
        {
            ulong* subactionPaths = stackalloc ulong[2];
            subactionPaths[0] = _leftHandPath;
            subactionPaths[1] = _rightHandPath;

            var createInfo = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = ActionType.PoseInput,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths
            };

            WriteFixedString(createInfo.ActionName, "grip_pose");
            WriteFixedString(createInfo.LocalizedActionName, "Grip Pose");

            fixed (Silk.NET.OpenXR.Action* actionPtr = &_gripPoseAction)
            {
                Result result = _xr.CreateAction(
                    _actionSet,
                    &createInfo,
                    actionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateAction failed for grip_pose: {result}");
                }
            }
        }

        private void SuggestOculusTouchBindings()
        {
            StringToPath(
                "/interaction_profiles/oculus/touch_controller",
                out ulong interactionProfilePath);

            StringToPath(
                "/user/hand/left/input/grip/pose",
                out ulong leftGripPosePath);

            StringToPath(
                "/user/hand/right/input/grip/pose",
                out ulong rightGripPosePath);

            StringToPath(
                "/user/hand/left/input/trigger/value",
                out ulong leftTriggerValuePath);

            StringToPath(
                "/user/hand/right/input/trigger/value",
                out ulong rightTriggerValuePath);

            StringToPath(
                "/user/hand/left/input/squeeze/value",
                out ulong leftGripValuePath);

            StringToPath(
                "/user/hand/right/input/squeeze/value",
                out ulong rightGripValuePath);

            StringToPath(
                "/user/hand/left/input/thumbstick",
                out ulong leftThumbstickPath);

            StringToPath(
                "/user/hand/right/input/thumbstick",
                out ulong rightThumbstickPath);

            StringToPath(
                "/user/hand/left/input/x/click",
                out ulong leftXPath);

            StringToPath(
                "/user/hand/left/input/y/click",
                out ulong leftYPath);

            StringToPath(
                "/user/hand/right/input/a/click",
                out ulong rightAPath);

            StringToPath(
                "/user/hand/right/input/b/click",
                out ulong rightBPath);

            StringToPath(
                "/user/hand/left/input/thumbstick/click",
                out ulong leftThumbstickClickPath);

            StringToPath(
                "/user/hand/right/input/thumbstick/click",
                out ulong rightThumbstickClickPath);

            StringToPath("/user/hand/right/input/aim/pose", out ulong rightAimPath);
            var bindings = stackalloc ActionSuggestedBinding[15];

            bindings[0] = new ActionSuggestedBinding
            {
                Action = _gripPoseAction,
                Binding = leftGripPosePath
            };

            bindings[1] = new ActionSuggestedBinding
            {
                Action = _gripPoseAction,
                Binding = rightGripPosePath
            };

            bindings[2] = new ActionSuggestedBinding
            {
                Action = _triggerValueAction,
                Binding = leftTriggerValuePath
            };

            bindings[3] = new ActionSuggestedBinding
            {
                Action = _triggerValueAction,
                Binding = rightTriggerValuePath
            };

            bindings[4] = new ActionSuggestedBinding
            {
                Action = _gripValueAction,
                Binding = leftGripValuePath
            };

            bindings[5] = new ActionSuggestedBinding
            {
                Action = _gripValueAction,
                Binding = rightGripValuePath
            };

            bindings[6] = new ActionSuggestedBinding
            {
                Action = _thumbstickAction,
                Binding = leftThumbstickPath
            };

            bindings[7] = new ActionSuggestedBinding
            {
                Action = _thumbstickAction,
                Binding = rightThumbstickPath
            };

            bindings[8] = new ActionSuggestedBinding
            {
                Action = _xButtonAction,
                Binding = leftXPath
            };

            bindings[9] = new ActionSuggestedBinding
            {
                Action = _yButtonAction,
                Binding = leftYPath
            };

            bindings[10] = new ActionSuggestedBinding
            {
                Action = _aButtonAction,
                Binding = rightAPath
            };

            bindings[11] = new ActionSuggestedBinding
            {
                Action = _bButtonAction,
                Binding = rightBPath
            };

            bindings[12] = new ActionSuggestedBinding
            {
                Action = _thumbstickClickAction,
                Binding = leftThumbstickClickPath
            };

            bindings[13] = new ActionSuggestedBinding
            {
                Action = _thumbstickClickAction,
                Binding = rightThumbstickClickPath
            };

            bindings[14] = new ActionSuggestedBinding { Action = _rightAimAction, Binding = rightAimPath };

            var suggestedBindings = new InteractionProfileSuggestedBinding
            {
                Type = StructureType.InteractionProfileSuggestedBinding,
                InteractionProfile = interactionProfilePath,
                CountSuggestedBindings = 15,
                SuggestedBindings = bindings
            };

            Result result = _xr.SuggestInteractionProfileBinding(
                _instance,
                &suggestedBindings);

            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"xrSuggestInteractionProfileBindings failed: {result}");
            }
        }

        private void AttachActionSet()
        {
            ActionSet* actionSets = stackalloc ActionSet[1];
            actionSets[0] = _actionSet;

            var attachInfo = new SessionActionSetsAttachInfo
            {
                Type = StructureType.SessionActionSetsAttachInfo,
                CountActionSets = 1,
                ActionSets = actionSets
            };

            Result result = _xr.AttachSessionActionSets(
                _session,
                &attachInfo);

            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"xrAttachSessionActionSets failed: {result}");
            }
        }

        private void CreateGripActionSpaces()
        {
            CreateGripActionSpace(_leftHandPath, out _leftGripSpace);
            CreateGripActionSpace(_rightHandPath, out _rightGripSpace);
        }

        private void CreateGripActionSpace(ulong subactionPath, out Space space)
        {
            var pose = new Posef
            {
                Orientation = new Quaternionf
                {
                    X = 0,
                    Y = 0,
                    Z = 0,
                    W = 1
                },
                Position = new Vector3f
                {
                    X = 0,
                    Y = 0,
                    Z = 0
                }
            };

            var createInfo = new ActionSpaceCreateInfo
            {
                Type = StructureType.ActionSpaceCreateInfo,
                Action = _gripPoseAction,
                SubactionPath = subactionPath,
                PoseInActionSpace = pose
            };

            fixed (Space* spacePtr = &space)
            {
                Result result = _xr.CreateActionSpace(
                    _session,
                    &createInfo,
                    spacePtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateActionSpace failed: {result}");
                }
            }
        }

        private bool SyncActions()
        {
            if (_sessionState != SessionState.Focused)
            {
                return false;
            }

            var activeActionSet = new ActiveActionSet
            {
                ActionSet = _actionSet
            };

            var syncInfo = new ActionsSyncInfo
            {
                Type = StructureType.ActionsSyncInfo,
                CountActiveActionSets = 1,
                ActiveActionSets = &activeActionSet
            };

            Result result = _xr.SyncAction(
                _session,
                &syncInfo);

            if (result == Result.SessionNotFocused)
            {
                return false;
            }

            if (result != Result.Success)
            {
                return false;
            }

            return true;
        }

        private void UpdateControllerPoses()
        {
            UpdateControllerPose(
                _leftGripSpace,
                _leftControllerState);

            UpdateControllerPose(
                _rightGripSpace,
                _rightControllerState);

            _leftControllerState.Grip =
                GetFloatActionState(_gripValueAction, _leftHandPath);

            _rightControllerState.Grip =
                GetFloatActionState(_gripValueAction, _rightHandPath);

            _leftControllerState.Trigger =
                GetFloatActionState(_triggerValueAction, _leftHandPath);

            _rightControllerState.Trigger =
                GetFloatActionState(_triggerValueAction, _rightHandPath);

            var leftThumbstick =
                GetVector2ActionState(_thumbstickAction, _leftHandPath);

            var rightThumbstick =
                GetVector2ActionState(_thumbstickAction, _rightHandPath);

            _leftControllerState.ThumbstickX = leftThumbstick.X;
            _leftControllerState.ThumbstickY = leftThumbstick.Y;

            _rightControllerState.ThumbstickX = rightThumbstick.X;
            _rightControllerState.ThumbstickY = rightThumbstick.Y;

            _leftControllerState.X =
                GetBooleanActionState(
                    _xButtonAction,
                    _leftHandPath);

            _leftControllerState.Y =
                GetBooleanActionState(
                    _yButtonAction,
                    _leftHandPath);

            _rightControllerState.A =
                GetBooleanActionState(
                    _aButtonAction,
                    _rightHandPath);

            _rightControllerState.B =
                GetBooleanActionState(
                    _bButtonAction,
                    _rightHandPath);

            _leftControllerState.ThumbstickClick =
                GetBooleanActionState(
                    _thumbstickClickAction,
                    _leftHandPath);

            _rightControllerState.ThumbstickClick =
                GetBooleanActionState(
                    _thumbstickClickAction,
                    _rightHandPath);
        }

        private void UpdateControllerPose(Space controllerSpace, ControllerState state)
        {
            state.IsTracked = false;
            state.LinearVelocityValid = state.AngularVelocityValid = false;
            var velocity = new SpaceVelocity { Type = StructureType.SpaceVelocity };
            var location = new SpaceLocation
            {
                Next = &velocity,
                Type = StructureType.SpaceLocation
            };

            Result result = _xr.LocateSpace(
                controllerSpace,
                _localSpace,
                _predictedDisplayTime,
                &location);

            if (result != Result.Success)
            {
                return;
            }

            const SpaceLocationFlags required = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit |
                SpaceLocationFlags.PositionTrackedBit | SpaceLocationFlags.OrientationTrackedBit;
            if ((location.LocationFlags & required) != required) return;
            state.IsTracked = true;
            state.SampleTime = _predictedDisplayTime;
            state.LinearVelocityValid = (velocity.VelocityFlags & SpaceVelocityFlags.LinearValidBit) != 0;
            state.AngularVelocityValid = (velocity.VelocityFlags & SpaceVelocityFlags.AngularValidBit) != 0;
            state.LinearVelocity = new(velocity.LinearVelocity.X, velocity.LinearVelocity.Y, velocity.LinearVelocity.Z);
            state.AngularVelocity = new(velocity.AngularVelocity.X, velocity.AngularVelocity.Y, velocity.AngularVelocity.Z);

            QuaternionToEulerDegrees(
                location.Pose.Orientation,
                state);

            state.PositionX =
                location.Pose.Position.X;

            state.PositionY =
                location.Pose.Position.Y;

            state.PositionZ =
                location.Pose.Position.Z;

            state.RotationX =
                location.Pose.Orientation.X;

            state.RotationY =
                location.Pose.Orientation.Y;

            state.RotationZ =
                location.Pose.Orientation.Z;

            state.RotationW =
                location.Pose.Orientation.W;
        }

        public ControllerState GetLeftController()
        {
            return _leftControllerState;
        }

        public ControllerState GetRightController()
        {
            return _rightControllerState;
        }

        private void PollEvents()
        {
            var eventData = new EventDataBuffer
            {
                Type = StructureType.EventDataBuffer
            };

            while (_xr.PollEvent(_instance, &eventData) == Result.Success)
            {
                if (eventData.Type == StructureType.EventDataSessionStateChanged)
                {
                    EventDataSessionStateChanged* stateChanged =
                        (EventDataSessionStateChanged*)&eventData;

                    _sessionState = stateChanged->State;

                    if (_sessionState == SessionState.Ready)
                    {
                        BeginSession();
                    }
                    else if (_sessionState == SessionState.Stopping)
                    {
                        CheckXr(_xr.EndSession(_session), "xrEndSession");
                        _isSessionRunning = false;
                    }
                    else if (_sessionState is SessionState.Exiting or SessionState.LossPending)
                    {
                        _isSessionRunning = false;
                    }
                }

                eventData = new EventDataBuffer
                {
                    Type = StructureType.EventDataBuffer
                };
            }
        }

        private void BeginSession()
        {
            if (_isSessionRunning)
            {
                return;
            }

            var beginInfo = new SessionBeginInfo
            {
                Type = StructureType.SessionBeginInfo,
                PrimaryViewConfigurationType =
                    ViewConfigurationType.PrimaryStereo
            };

            Result result = _xr.BeginSession(
                _session,
                &beginInfo);

            if (result != Result.Success)
            {
                throw new InvalidOperationException(
                    $"xrBeginSession failed: {result}");
            }

            _isSessionRunning = true;
        }

        private void CreateTriggerValueAction()
        {
            ulong* subactionPaths = stackalloc ulong[2];
            subactionPaths[0] = _leftHandPath;
            subactionPaths[1] = _rightHandPath;

            var createInfo = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = ActionType.FloatInput,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths
            };

            WriteFixedString(createInfo.ActionName, "trigger_value");
            WriteFixedString(createInfo.LocalizedActionName, "Trigger Value");

            fixed (Silk.NET.OpenXR.Action* actionPtr = &_triggerValueAction)
            {
                Result result = _xr.CreateAction(
                    _actionSet,
                    &createInfo,
                    actionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateAction failed for trigger_value: {result}");
                }
            }
        }

        private void CreateGripValueAction()
        {
            ulong* subactionPaths = stackalloc ulong[2];
            subactionPaths[0] = _leftHandPath;
            subactionPaths[1] = _rightHandPath;

            var createInfo = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = ActionType.FloatInput,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths
            };

            WriteFixedString(createInfo.ActionName, "grip_value");
            WriteFixedString(createInfo.LocalizedActionName, "Grip Value");

            fixed (Silk.NET.OpenXR.Action* actionPtr = &_gripValueAction)
            {
                Result result = _xr.CreateAction(
                    _actionSet,
                    &createInfo,
                    actionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateAction failed for grip_value: {result}");
                }
            }
        }

        private void CreateThumbstickAction()
        {
            ulong* subactionPaths = stackalloc ulong[2];
            subactionPaths[0] = _leftHandPath;
            subactionPaths[1] = _rightHandPath;

            var createInfo = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = ActionType.Vector2fInput,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths
            };

            WriteFixedString(createInfo.ActionName, "thumbstick");
            WriteFixedString(createInfo.LocalizedActionName, "Thumbstick");

            fixed (Silk.NET.OpenXR.Action* actionPtr = &_thumbstickAction)
            {
                Result result = _xr.CreateAction(
                    _actionSet,
                    &createInfo,
                    actionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateAction failed for thumbstick: {result}");
                }
            }
        }

        private void CreateButtonActions()
        {
            CreateBooleanAction("x_click", "X Click", ref _xButtonAction);
            CreateBooleanAction("y_click", "Y Click", ref _yButtonAction);
            CreateBooleanAction("a_click", "A Click", ref _aButtonAction);
            CreateBooleanAction("b_click", "B Click", ref _bButtonAction);
            CreateBooleanAction("thumbstick_click", "Thumbstick Click", ref _thumbstickClickAction);
        }

        private void CreateBooleanAction(string actionName, string localizedName, ref Silk.NET.OpenXR.Action action)
        {
            ulong* subactionPaths = stackalloc ulong[2];
            subactionPaths[0] = _leftHandPath;
            subactionPaths[1] = _rightHandPath;

            var createInfo = new ActionCreateInfo
            {
                Type = StructureType.ActionCreateInfo,
                ActionType = ActionType.BooleanInput,
                CountSubactionPaths = 2,
                SubactionPaths = subactionPaths
            };

            WriteFixedString(createInfo.ActionName, actionName);
            WriteFixedString(createInfo.LocalizedActionName, localizedName);

            fixed (Silk.NET.OpenXR.Action* actionPtr = &action)
            {
                Result result = _xr.CreateAction(
                    _actionSet,
                    &createInfo,
                    actionPtr);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"xrCreateAction failed for {actionName}: {result}");
                }
            }
        }

        private bool GetBooleanActionState(Silk.NET.OpenXR.Action action, ulong subactionPath)
        {
            var getInfo = new ActionStateGetInfo
            {
                Type = StructureType.ActionStateGetInfo,
                Action = action,
                SubactionPath = subactionPath
            };

            var state = new ActionStateBoolean
            {
                Type = StructureType.ActionStateBoolean
            };

            Result result = _xr.GetActionStateBoolean(
                _session,
                &getInfo,
                &state);

            if (result != Result.Success)
            {
                return false;
            }

            return state.IsActive != 0 && state.CurrentState != 0;
        }

        private float GetFloatActionState(Silk.NET.OpenXR.Action action, ulong subactionPath)
        {
            var getInfo = new ActionStateGetInfo
            {
                Type = StructureType.ActionStateGetInfo,
                Action = action,
                SubactionPath = subactionPath
            };

            var state = new ActionStateFloat
            {
                Type = StructureType.ActionStateFloat
            };

            Result result = _xr.GetActionStateFloat(
                _session,
                &getInfo,
                &state);

            if (result != Result.Success)
            {
                return 0;
            }

            return state.CurrentState;
        }

        private Vector2f GetVector2ActionState(Silk.NET.OpenXR.Action action, ulong subactionPath)
        {
            var getInfo = new ActionStateGetInfo
            {
                Type = StructureType.ActionStateGetInfo,
                Action = action,
                SubactionPath = subactionPath
            };

            var state = new ActionStateVector2f
            {
                Type = StructureType.ActionStateVector2f
            };

            Result result = _xr.GetActionStateVector2(
                _session,
                &getInfo,
                &state);

            if (result != Result.Success)
            {
                return new Vector2f
                {
                    X = 0,
                    Y = 0
                };
            }

            return state.CurrentState;
        }

        private static void QuaternionToEulerDegrees(Quaternionf q, ControllerState state)
        {
            double sinrCosp = 2 * (q.W * q.X + q.Y * q.Z);
            double cosrCosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            double roll = Math.Atan2(sinrCosp, cosrCosp);

            double sinp = 2 * (q.W * q.Y - q.Z * q.X);
            double pitch;

            if (Math.Abs(sinp) >= 1)
            {
                pitch = Math.CopySign(Math.PI / 2, sinp);
            }
            else
            {
                pitch = Math.Asin(sinp);
            }

            double sinyCosp = 2 * (q.W * q.Z + q.X * q.Y);
            double cosyCosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            double yaw = Math.Atan2(sinyCosp, cosyCosp);

            state.Pitch = (float)(pitch * 180.0 / Math.PI);
            state.Yaw = (float)(yaw * 180.0 / Math.PI);
            state.Roll = (float)(roll * 180.0 / Math.PI);
        }
    }
}
