using System;
using System.Collections.Generic;
using System.Linq;

namespace SaabDidTool.Services
{
    /// <summary>
    /// Minimal ISO-TP (ISO 15765-2) implementation over the adapter's raw
    /// hex-frame command interface (headers off, CAF off): each command you
    /// send transmits one CAN frame's data bytes (as hex text), and the
    /// adapter's reply to that command is whatever frame(s) it captured on
    /// the bus in response before the next '>' prompt.
    ///
    /// This replaces the manual "send a text file with guessed millisecond
    /// delays" workaround with a real handshake: it reads whatever the
    /// adapter actually sends back rather than assuming fixed timing.
    /// </summary>
    public sealed class IsoTpTransport
    {
        private readonly SerialAdapter _adapter;

        public IsoTpTransport(SerialAdapter adapter)
        {
            _adapter = adapter;
        }

        /// <summary>
        /// Sends a UDS/GMLAN request payload (e.g. [0x1A, did] for a read,
        /// or [0x3B, did, ...data] for a write) and returns the assembled
        /// response payload with ISO-TP PCI bytes stripped
        /// (i.e. [responseSid, did, ...data]).
        ///
        /// If tolerateMissingAck is true, a write with no reply at all
        /// (some ECUs don't ack a WriteDataByIdentifier) returns an empty
        /// array instead of throwing - callers should verify via a
        /// follow-up read in that case, exactly like the manual process
        /// on the forum does.
        /// </summary>
        public byte[] SendReceive(byte[] requestPayload, bool tolerateMissingAck = false, int frameTimeoutMs = 2000)
        {
            string firstReplyText = SendPayload(requestPayload, frameTimeoutMs);
            return ReceivePayload(firstReplyText, tolerateMissingAck, frameTimeoutMs);
        }

        // ---- sending: returns the raw text reply to the LAST frame we transmitted ----

        private string SendPayload(byte[] payload, int frameTimeoutMs)
        {
            if (payload.Length <= 7)
            {
                // Single frame: PCI = 0x0L, L = data length.
                var frame = new List<byte> { (byte)payload.Length };
                frame.AddRange(payload);
                return _adapter.SendCommand(ToHex(frame), frameTimeoutMs);
            }

            // Multi-frame send: First Frame, wait for the ECU's Flow
            // Control frame, then send Consecutive Frames.
            int total = payload.Length;
            var ff = new List<byte>
            {
                (byte)(0x10 | ((total >> 8) & 0x0F)),
                (byte)(total & 0xFF),
            };
            ff.AddRange(payload.Take(6));

            string ffReply = _adapter.SendCommand(ToHex(ff), frameTimeoutMs);
            byte[] fc = FirstFrameOf(ffReply);
            if (fc.Length == 0 || (fc[0] & 0xF0) != 0x30)
            {
                throw new IsoTpException(
                    "Expected a Flow Control frame (0x3_) after the First Frame, but got: '" + ffReply + "'. " +
                    "The ECU may use a non-default block size/separation time, or the reply filter (ATCRA) is wrong.");
            }

            int sent = 6;
            byte sequence = 1;
            string lastCfReply = string.Empty;
            while (sent < total)
            {
                int chunkLen = Math.Min(7, total - sent);
                var cf = new List<byte> { (byte)(0x20 | (sequence & 0x0F)) };
                cf.AddRange(payload.Skip(sent).Take(chunkLen));
                lastCfReply = _adapter.SendCommand(ToHex(cf), frameTimeoutMs);

                sent += chunkLen;
                sequence = (byte)((sequence % 15) + 1); // ISO-TP sequence numbers cycle 1..15, never 0
            }

            return lastCfReply;
        }

        // ---- receiving: interprets the reply text captured after our last transmit ----

        private byte[] ReceivePayload(string firstReplyText, bool tolerateMissingAck, int frameTimeoutMs)
        {
            byte[] first = FirstFrameOf(firstReplyText);

            if (first.Length == 0)
            {
                if (tolerateMissingAck)
                {
                    return Array.Empty<byte>();
                }
                throw new IsoTpException("No response received (NO DATA / timeout): '" + firstReplyText + "'");
            }

            int pciType = (first[0] & 0xF0) >> 4;

            if (pciType == 0x0)
            {
                int len = first[0] & 0x0F;
                if (first.Length - 1 < len)
                {
                    throw new IsoTpException("Single frame shorter than its declared length: '" + firstReplyText + "'");
                }
                return first.Skip(1).Take(len).ToArray();
            }

            if (pciType != 0x1)
            {
                throw new IsoTpException($"Unexpected PCI type 0x{pciType:X} in response: '{firstReplyText}'");
            }

            // Multi-frame response: First Frame gives total length + first 6 bytes.
            int total = ((first[0] & 0x0F) << 8) | first[1];
            var data = new List<byte>();
            data.AddRange(first.Skip(2).Take(Math.Min(6, first.Length - 2)));

            // Any further Consecutive Frames may already be sitting in the
            // same buffered reply (some STN firmware returns multi-line
            // blocks); check those before sending Flow Control.
            foreach (byte[] extra in AllFramesOf(firstReplyText).Skip(1))
            {
                if (data.Count >= total) break;
                if ((extra[0] & 0xF0) == 0x20)
                {
                    int take = Math.Min(total - data.Count, extra.Length - 1);
                    data.AddRange(extra.Skip(1).Take(take));
                }
            }

            if (data.Count < total)
            {
                // Send Flow Control: continue, block size 0 (send all
                // remaining CFs without waiting for further FC), min
                // separation time 0.
                string fcReplyText = _adapter.SendCommand("300000", frameTimeoutMs);

                foreach (byte[] cf in AllFramesOf(fcReplyText))
                {
                    if (data.Count >= total) break;
                    if ((cf[0] & 0xF0) != 0x20)
                    {
                        throw new IsoTpException("Expected a Consecutive Frame (0x2_) but got: '" + fcReplyText + "'");
                    }
                    int take = Math.Min(total - data.Count, cf.Length - 1);
                    data.AddRange(cf.Skip(1).Take(take));
                }

                // Some adapters only return one buffered line per command;
                // if we still need more, keep asking - sending a bare CR
                // repeats the last command on ELM/STN adapters, which
                // re-listens without re-transmitting anything new.
                int guard = 0;
                while (data.Count < total && guard < 16)
                {
                    string more = _adapter.SendCommand("", frameTimeoutMs);
                    byte[] cf = FirstFrameOf(more);
                    if (cf.Length == 0)
                    {
                        break;
                    }
                    if ((cf[0] & 0xF0) == 0x20)
                    {
                        int take = Math.Min(total - data.Count, cf.Length - 1);
                        data.AddRange(cf.Skip(1).Take(take));
                    }
                    guard++;
                }
            }

            if (data.Count < total)
            {
                throw new IsoTpException(
                    $"Only received {data.Count} of {total} expected bytes in multi-frame response.");
            }

            return data.ToArray();
        }

        // ---- helpers ----

        private static string ToHex(IEnumerable<byte> bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("X2")));
        }

        /// <summary>Splits a raw adapter reply into one or more CAN frames (one per line).</summary>
        private static List<byte[]> AllFramesOf(string raw)
        {
            var frames = new List<byte[]>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return frames;
            }

            string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string hex = new string(line.Where(Uri.IsHexDigit).ToArray());
                if (hex.Length % 2 != 0)
                {
                    hex = hex.Substring(0, hex.Length - 1);
                }
                if (hex.Length == 0)
                {
                    continue;
                }

                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }
                frames.Add(bytes);
            }
            return frames;
        }

        private static byte[] FirstFrameOf(string raw)
        {
            List<byte[]> frames = AllFramesOf(raw);
            return frames.Count > 0 ? frames[0] : Array.Empty<byte>();
        }
    }

    public sealed class IsoTpException : Exception
    {
        public IsoTpException(string message) : base(message) { }
    }
}
