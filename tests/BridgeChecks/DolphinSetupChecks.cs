using System.Text;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

internal static class DolphinSetupChecks
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = Path.Combine(AppContext.BaseDirectory, "OpenXR-DolphinSetupChecks-" + Guid.NewGuid().ToString("N"));
        string config = Path.Combine(root, "Config");
        Directory.CreateDirectory(config);
        string wiimote = Path.Combine(config, "WiimoteNew.ini");
        string dsu = Path.Combine(config, "DSUClient.ini");
        string dolphin = Path.Combine(config, "Dolphin.ini");
        const string original = "[Wiimote1]\nSource = 2\nButtons/A = Old\nIMUIR/Enabled = True\n[Wiimote2]\nDevice = Other Controller\nButtons/A = KeepMe\n[BalanceBoard]\nDevice = Balance\n";
        File.WriteAllText(wiimote, original);
        File.WriteAllText(dsu, "[Server]\nEnabled = False\nEntries = Other:127.0.0.1:12345;OpenXR Input Explorer:127.0.0.1:4444;\n[Other]\nKeep = Yes\n");
        File.WriteAllText(dolphin, ";User settings\n[Display]\nFullscreen = True\n[Input]\nBackgroundInput = False\n[BluetoothPassthrough]\nEnabled = True\n");
        try
        {
            Check(DolphinSetupService.NormalizeConfigDirectory(root) == config, "User folder resolves to Config");
            Check(DolphinSetupService.NormalizeConfigDirectory(Path.Combine(root, "Missing")) is null, "Unknown paths not guessed");
            var settings = new MappingConfiguration();
            var profile = DolphinSetupService.Profile(settings);
            Check(profile["Buttons/A"] == "`DInput/0/Keyboard Mouse:A`", "Right trigger defaults to Wii A");
            Check(profile["Buttons/B"] == "`DInput/0/Keyboard Mouse:B`", "Right grip defaults to Wii B");
            Check(profile["Buttons/Home"] == "", "Wii Home stays unassigned by default");
            Check(profile["Buttons/1"] == "`DInput/0/Keyboard Mouse:1`" && profile["Buttons/2"] == "`DInput/0/Keyboard Mouse:2`", "Right face buttons target Wii 1/2");
            Check(profile["Buttons/-"] == "`DInput/0/Keyboard Mouse:Q`" && profile["Buttons/+"] == "`DInput/0/Keyboard Mouse:E`", "Only left Y/X target Wii minus/plus");
            var unmapped = new MappingConfiguration();
            foreach (string control in new[] { "RightTrigger", "RightGrip", "RightA", "RightB", "LeftX", "LeftY" }) unmapped.Bindings[control] = "None";
            var fallback = DolphinSetupService.Profile(unmapped);
            Check(fallback["Buttons/A"].Contains("DSUClient/1/Rift2Wii:L2") && fallback["Buttons/A"].Contains("1.3"), "Unmapped trigger uses DSU threshold");
            Check(fallback["Buttons/B"].Contains("DSUClient/1/Rift2Wii:L1") && fallback["Buttons/B"].Contains("1.3"), "Unmapped grip uses DSU threshold");
            Check(fallback["Buttons/1"] == "`DSUClient/1/Rift2Wii:Circle`" &&
                fallback["Buttons/2"] == "`DSUClient/1/Rift2Wii:Cross`", "Only right-hand face buttons feed Wii 1/2 through DSU");
            Check(fallback["Buttons/-"] == "`DSUClient/0/Rift2Wii:Circle`" && fallback["Buttons/+"] == "`DSUClient/0/Rift2Wii:Cross`", "Only left face buttons feed Wii minus/plus through DSU; no stick-click binding");
            Check(profile["Nunchuk/Buttons/C"].Contains("DSUClient/0/Rift2Wii:L1"), "Left grip maps Nunchuk C");
            var customGrip = new MappingConfiguration();
            customGrip.Bindings["LeftGrip"] = "LeftCtrl";
            Check(DolphinSetupService.Profile(customGrip)["Nunchuk/Buttons/C"] == "`DInput/0/Keyboard Mouse:LCONTROL`", "Custom left-grip key reaches Nunchuk C");
            Check(profile["Nunchuk/Buttons/Z"].Contains("DSUClient/0/Rift2Wii:L2"), "Left trigger maps Nunchuk Z");
            Check(profile["Nunchuk/Stick/Up"].Contains("DSUClient/0/") && profile["D-Pad/Up"].Contains("DSUClient/1/"), "Separate hand axes");
            Check(profile["IMUIR/Enabled"] == "False" && profile["IMUAccelerometer/Up"].Contains("DSUClient/1/") &&
                profile["IMUGyroscope/Pitch Up"].Contains("Gyro Pitch Up") && profile["Shake/X"] == "" &&
                profile["Nunchuk/IMUAccelerometer/Up"].Contains("DSUClient/0/"), "Hand-specific motion bindings; mouse pointing and no duplicate simulation");
            Check(DolphinSetupService.DolphinKey("LeftCtrl") == "LCONTROL" && DolphinSetupService.DolphinKey("1") == "1", "Keyboard names translated");
            Check(profile["IR/Total Pitch"] == "20" && profile["IR/Total Yaw"] == "25" &&
                profile["IR/Vertical Offset"] == "0" && profile["IR/Dead Zone"] == "0", "Pointer defaults preserve existing range");

            var plan = DolphinSetupService.Prepare(root, settings);
            bool blocked = false;
            try { DolphinSetupService.Apply(plan, () => true); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked && File.ReadAllText(wiimote) == original && !Directory.Exists(Path.Combine(config, "Rift2Wii Backups")), "Running Dolphin blocks all writes");
            var result = DolphinSetupService.Apply(plan, () => false);
            string installed = File.ReadAllText(wiimote);
            Check(installed.Contains("Source = 1") && installed.Contains("Extension = Nunchuk") &&
                installed.Contains("[Wiimote2]") && installed.Contains("Buttons/A = KeepMe") && installed.Contains("[BalanceBoard]"), "Wii Remote 1 configured; other controllers retained");
            Check(File.ReadAllText(Path.Combine(result.BackupDirectory, "WiimoteNew.ini")) == original, "Exact original backed up");
            Check(File.ReadAllText(dsu).Contains("Other:127.0.0.1:12345;") && File.ReadAllText(dsu).Contains("Rift2Wii:127.0.0.1:26760;") &&
                File.ReadAllText(dsu).Contains("Keep = Yes") && !File.ReadAllText(dsu).Contains("OpenXR Input Explorer:"),
                "Legacy server entry migrated; other DSU servers/settings preserved");
            Check(File.ReadAllText(dolphin).Contains("Fullscreen = True") && File.ReadAllText(dolphin).Contains("BackgroundInput = True") &&
                File.ReadAllText(dolphin).Contains("[BluetoothPassthrough]"), "Emulated Bluetooth/background input without resetting video settings");
            Check(File.ReadAllText(result.ProfilePath).Contains("[Profile]") && !File.ReadAllText(result.ProfilePath).Contains("Source = 1"), "Reusable profile generated separately from controller source");

            settings.PointerPitch = 22.5; settings.PointerYaw = 26; settings.PointerVerticalOffset = -2;
            string beforePointer = File.ReadAllText(wiimote), beforeProfile = File.ReadAllText(result.ProfilePath);
            byte[] dsuBeforePointer = File.ReadAllBytes(dsu), dolphinBeforePointer = File.ReadAllBytes(dolphin);
            var pointerPlan = DolphinSetupService.PreparePointerCalibration(config, settings);
            Check(pointerPlan.Files.Count == 2, "Pointer-only apply touches active controller and existing profile only");
            blocked = false;
            try { DolphinSetupService.Apply(pointerPlan, () => true); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked && File.ReadAllText(wiimote) == beforePointer, "Pointer changes also require Dolphin closed");
            var pointerResult = DolphinSetupService.Apply(pointerPlan, () => false);
            string pointerInstalled = File.ReadAllText(wiimote);
            Check(pointerInstalled.Contains("IR/Total Pitch = 22.5") && pointerInstalled.Contains("IR/Total Yaw = 26") &&
                pointerInstalled.Contains("IR/Vertical Offset = -2"), "Chosen calibration encoded using Dolphin INI names and invariant decimals");
            var managedPointerKeys = DolphinSetupService.PointerSettings(settings).Keys.ToHashSet();
            string WithoutPointerSettings(string text) => string.Join('\n', text.Split('\n')
                .Where(line => !managedPointerKeys.Contains(line.Split('=')[0].Trim())));
            Check(WithoutPointerSettings(pointerInstalled) == WithoutPointerSettings(beforePointer) &&
                WithoutPointerSettings(File.ReadAllText(result.ProfilePath)) == WithoutPointerSettings(beforeProfile),
                "Pointer-only apply preserves buttons, sticks, motion, mouse bindings and other settings");
            Check(File.ReadAllBytes(dsu).SequenceEqual(dsuBeforePointer) && File.ReadAllBytes(dolphin).SequenceEqual(dolphinBeforePointer),
                "Pointer calibration leaves DSU and global Dolphin settings untouched");
            Check(File.ReadAllText(Path.Combine(pointerResult.BackupDirectory, "WiimoteNew.ini")) == beforePointer,
                "Pointer calibration backs up previous settings");
            var invalidPointer = new MappingConfiguration { PointerPitch = double.NaN };
            blocked = false;
            try { DolphinSetupService.PreparePointerCalibration(config, invalidPointer); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "Non-finite pointer settings rejected before writes");

            settings.DsuPort = 26761;
            settings.Bindings["RightA"] = "1";
            DolphinSetupService.Apply(DolphinSetupService.Prepare(config, settings), () => false);
            string repeated = File.ReadAllText(dsu);
            Check(repeated.Split("Rift2Wii:").Length == 2 && repeated.Contains(":26761;"), "Repeated setup replaces only managed DSU entry");
            Check(File.ReadAllText(wiimote).Contains("DInput/0/Keyboard Mouse:1"), "Changed key choice reaches Dolphin");

            plan = DolphinSetupService.Prepare(config, settings);
            File.AppendAllText(wiimote, ";external change\n");
            blocked = false;
            try { DolphinSetupService.Apply(plan, () => false); } catch (IOException) { blocked = true; }
            Check(blocked && File.ReadAllText(wiimote).EndsWith(";external change\n"), "Concurrent edits are not overwritten");

            File.WriteAllText(dsu, File.ReadAllText(dsu).Replace("Enabled = True", "Enabled = False"));
            File.WriteAllText(dolphin, File.ReadAllText(dolphin).Replace("BackgroundInput = True", "BackgroundInput = False"));
            plan = DolphinSetupService.Prepare(config, settings);
            byte[] beforeWii = File.ReadAllBytes(wiimote), beforeDsu = File.ReadAllBytes(dsu), beforeDolphin = File.ReadAllBytes(dolphin);
            int writes = 0;
            blocked = false;
            try
            {
                DolphinSetupService.Apply(plan, () => false, (path, bytes) =>
                {
                    if (++writes == 4) throw new IOException("Simulated write failure");
                    File.WriteAllBytes(path, bytes);
                });
            }
            catch (AggregateException error)
            {
                blocked = true;
                Check(error.InnerExceptions.Count == 1 && error.InnerException!.Message == "Simulated write failure", "Unexpected rollback failure: " + error);
            }
            Check(blocked && writes == 4 && File.ReadAllBytes(wiimote).SequenceEqual(beforeWii) && File.ReadAllBytes(dsu).SequenceEqual(beforeDsu) &&
                File.ReadAllBytes(dolphin).SequenceEqual(beforeDolphin), "Partial setup rolls back previous writes");

            var outside = new DolphinSetupPlan(config, new[] { new DolphinSetupFile(Path.Combine(root, "outside.ini"), null, Encoding.UTF8.GetBytes("bad")) });
            blocked = false;
            try { DolphinSetupService.Apply(outside, () => false); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked && !File.Exists(Path.Combine(root, "outside.ini")), "Writes restricted to selected Config folder");
            Check(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0, "Atomic write temporaries removed");
            Console.WriteLine("PASS: Dolphin profile mappings, backups, repeat setup, unrelated-setting preservation, running-process guard and rollback.");
        }
        finally
        {
            string full = Path.GetFullPath(root);
            if (Directory.GetParent(full)!.FullName.TrimEnd(Path.DirectorySeparatorChar) != Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(full).StartsWith("OpenXR-DolphinSetupChecks-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected setup test cleanup path.");
            Directory.Delete(full, true);
        }
    }
}
