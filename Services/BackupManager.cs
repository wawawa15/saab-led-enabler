using System;
using System.IO;
using System.Text.Json;

namespace SaabDidTool.Services
{
    public sealed class BackupEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string Ecu { get; set; } = string.Empty;
        public string Did { get; set; } = string.Empty;
        public string ValueHex { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Appends every read/write to a local JSON-lines log so an original
    /// value is never lost, matching the "backup your original value or
    /// take a screenshot" step from the manual process - except automatic.
    /// </summary>
    public sealed class BackupManager
    {
        public string LogFilePath { get; }

        public BackupManager()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SaabDidTool");
            Directory.CreateDirectory(dir);
            LogFilePath = Path.Combine(dir, "backups.jsonl");
        }

        public void Log(string ecuName, byte did, byte[] value, string note)
        {
            var entry = new BackupEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Ecu = ecuName,
                Did = $"0x{did:X2}",
                ValueHex = Convert.ToHexString(value),
                Note = note,
            };

            string json = JsonSerializer.Serialize(entry);
            File.AppendAllText(LogFilePath, json + Environment.NewLine);
        }

        public void OpenContainingFolder()
        {
            string dir = Path.GetDirectoryName(LogFilePath)!;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
    }
}
