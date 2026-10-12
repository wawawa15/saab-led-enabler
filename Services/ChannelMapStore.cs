using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SaabDidTool.Services
{
    /// <summary>
    /// GM doesn't publicly document which bit of the bulb-outage byte maps
    /// to which physical lamp circuit, and it can vary by model year. This
    /// store lets the user name each bit after testing it once, and
    /// remembers that mapping for next time (per ECU + DID + byte index).
    /// </summary>
    public sealed class ChannelMapStore
    {
        private readonly string _filePath;
        private Dictionary<string, string[]> _map;

        public ChannelMapStore()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SaabDidTool");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "channel_map.json");
            Load();
        }

        private static string KeyFor(string ecuName, byte did, int byteIndex) =>
            $"{ecuName}|0x{did:X2}|{byteIndex}";

        /// <summary>Returns the 8 bit labels for this byte, or the given defaults if none saved yet.</summary>
        public string[] GetLabels(string ecuName, byte did, int byteIndex, string[] defaults)
        {
            string key = KeyFor(ecuName, did, byteIndex);
            if (_map.TryGetValue(key, out string[] labels) && labels.Length == 8)
            {
                return labels;
            }
            return defaults;
        }

        public void SaveLabels(string ecuName, byte did, int byteIndex, string[] labels)
        {
            if (labels.Length != 8)
            {
                throw new ArgumentException("Expected exactly 8 bit labels.");
            }
            _map[KeyFor(ecuName, did, byteIndex)] = labels;
            Persist();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _map = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                           ?? new Dictionary<string, string[]>();
                    return;
                }
            }
            catch
            {
                // fall through to empty map if the file is corrupt
            }
            _map = new Dictionary<string, string[]>();
        }

        private void Persist()
        {
            string json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
    }
}
