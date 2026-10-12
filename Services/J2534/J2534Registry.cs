using System.Collections.Generic;
using Microsoft.Win32;

namespace SaabDidTool.Services.J2534
{
    public sealed class J2534DeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string DllPath { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({Vendor})";
    }

    /// <summary>
    /// J2534 vendors are required to register their device under
    /// HKLM\SOFTWARE\PassThruSupport.04.04\{subkey}, with "Name",
    /// "Vendor" and "FunctionLibrary" values. GM's MDI/MDI2 driver install
    /// registers itself here the same as any other J2534 device.
    /// </summary>
    public static class J2534Registry
    {
        private const string RegistryRoot = @"SOFTWARE\PassThruSupport.04.04";

        public static List<J2534DeviceInfo> FindInstalledDevices()
        {
            var results = new List<J2534DeviceInfo>();

            // 32-bit registry view first (most J2534 vendors register here
            // even on 64-bit Windows), then the native 64-bit view.
            ScanView(RegistryView.Registry32, results);
            ScanView(RegistryView.Registry64, results);

            return results;
        }

        private static void ScanView(RegistryView view, List<J2534DeviceInfo> results)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey root = baseKey.OpenSubKey(RegistryRoot);
            if (root == null) return;

            foreach (string subKeyName in root.GetSubKeyNames())
            {
                using RegistryKey sub = root.OpenSubKey(subKeyName);
                if (sub == null) continue;

                string dllPath = sub.GetValue("FunctionLibrary") as string;
                if (string.IsNullOrWhiteSpace(dllPath)) continue;

                var info = new J2534DeviceInfo
                {
                    Name = sub.GetValue("Name") as string ?? subKeyName,
                    Vendor = sub.GetValue("Vendor") as string ?? string.Empty,
                    DllPath = dllPath,
                };

                bool alreadyPresent = results.Exists(r => r.DllPath.Equals(dllPath, System.StringComparison.OrdinalIgnoreCase));
                if (!alreadyPresent)
                {
                    results.Add(info);
                }
            }
        }
    }
}
