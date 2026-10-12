using System.Collections.Generic;

namespace SaabDidTool.Models
{
    /// <summary>
    /// A GMLAN (single-wire CAN / I-bus, 33.333 kbps) ECU target.
    /// Header = the request CAN ID we transmit with (ATSH).
    /// ReplyFilter = the CAN ID we expect replies on (ATCRA).
    /// </summary>
    public sealed class EcuTarget
    {
        public string Name { get; }
        public string Header { get; }
        public string ReplyFilter { get; }
        public List<DidDefinition> KnownDids { get; }

        public EcuTarget(string name, string header, string replyFilter, List<DidDefinition> knownDids)
        {
            Name = name;
            Header = header;
            ReplyFilter = replyFilter;
            KnownDids = knownDids;
        }

        public override string ToString() => Name;

        public static EcuTarget Rec => new EcuTarget(
            "REC - Rear Exterior Control (0x249)",
            "249",
            "649",
            new List<DidDefinition>
            {
                new DidDefinition(0x40, "EOL Parameters for REC"),
                new DidDefinition(0x41, "Rear Exterior Light Timers & Settings"),
                new DidDefinition(0x43, "REC Body Functions Settings & Timers"),
                new DidDefinition(0x45, "Bulb Outage & Substitution Lighting"),
            });

        public static EcuTarget Uec => new EcuTarget(
            "UEC - Front/Upper Exterior Control (0x24F)",
            "24f",
            "64f",
            new List<DidDefinition>
            {
                new DidDefinition(0x40, "PWM Settings for Bulbs"),
                new DidDefinition(0x41, "EOL Parameters for UEC"),
                new DidDefinition(0x42, "Exterior Light Timers & Settings"),
                new DidDefinition(0x4D, "Bulb Outage & Substitution"),
            });
    }

    public sealed class DidDefinition
    {
        public byte Id { get; }
        public string Description { get; }

        public DidDefinition(byte id, string description)
        {
            Id = id;
            Description = description;
        }

        public override string ToString() => $"0x{Id:X2} - {Description}";
    }
}
