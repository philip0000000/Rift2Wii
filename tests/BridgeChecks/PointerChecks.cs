using System.Data;
using System.Globalization;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

static class PointerChecks
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static double Evaluate(string expression, double u, double v)
        {
            // Evaluate the actual emitted arithmetic expressions against Dolphin's
            // half-axis inputs (negative raw input values are clamped at each leaf).
            foreach (var (axis, signed) in new[] { ("X", u * 2 - 1), ("Y", v * 2 - 1) })
                foreach (var (sign, value) in new[] { ("+", Math.Max(signed, 0)), ("-", Math.Max(-signed, 0)) })
                    expression = expression.Replace($"`{DolphinSetupService.KeyboardDevice}:Cursor {axis}{sign}`",
                        value.ToString("R", CultureInfo.InvariantCulture));
            return Convert.ToDouble(new DataTable().Compute(expression, ""), CultureInfo.InvariantCulture);
        }
        var configuration = new MappingConfiguration();
        var settings = DolphinSetupService.PointerSettings(configuration);
        Check(settings["IRPassthrough/Enabled"] == "True" && settings["IMUIR/Enabled"] == "False", "Direct mode enables IR passthrough without gyro pointing");
        foreach (var (u, v) in new[] { (0d, 0d), (1d, 0d), (0d, 1d), (1d, 1d), (.5, .5), (.25, .75) })
        {
            double x1 = Evaluate(settings["IRPassthrough/Object 1 X"], u, v);
            double x2 = Evaluate(settings["IRPassthrough/Object 2 X"], u, v);
            double y1 = Evaluate(settings["IRPassthrough/Object 1 Y"], u, v);
            double y2 = Evaluate(settings["IRPassthrough/Object 2 Y"], u, v);
            Check(Math.Abs((x1 + x2) / 2 - (.5 - (u * 2 - 1) * .4)) < .000001,
                "IR horizontal midpoint follows mirrored mouse coordinates");
            Check(Math.Abs(y1 - (.5 + (v * 2 - 1) * .4)) < .000001 && y1 == y2,
                "IR vertical position follows the mouse to both edges");
            Check(x1 >= 0 && x2 <= 1 && y1 >= 0 && y1 <= 1 && Math.Abs(x2 - x1 - .08) < .000001,
                "Default dot pair remains in camera bounds including all corners");
        }
        Check(settings["IRPassthrough/Object 3 Size"] == "" && settings["IRPassthrough/Object 4 Size"] == "", "Unused IR dots disabled");
        configuration.PointerPitch = 22;
        configuration.PointerVerticalOffset = -2;
        var adjusted = DolphinSetupService.PointerSettings(configuration);
        Check(Math.Abs(Evaluate(adjusted["IRPassthrough/Object 1 Y"], .5, 1) - .92) < .000001,
            "Direct mode range and offset alter transmitted coordinates");
        configuration.DirectPointerEnabled = false;
        var legacy = DolphinSetupService.PointerSettings(configuration);
        Check(legacy["IRPassthrough/Enabled"] == "False" && legacy["IRPassthrough/Object 1 X"] == "" &&
            legacy["IRPassthrough/Object 2 Size"] == "" && legacy["IR/Total Pitch"] == "22", "Legacy mode restores rotation-based pointing and clears synthetic dots");
        Console.WriteLine("PASS: direct mouse-to-IR expressions, centre/corners, axes, range/offset and legacy fallback.");
    }
}
