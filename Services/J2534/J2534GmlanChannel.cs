using System;
using System.Collections.Generic;
using System.Linq;
using SaabDidTool.Interfaces;

namespace SaabDidTool.Services.J2534
{
    /// <summary>
    /// GMLAN single-wire CAN (33.333 kbps, pin 1) over J2534, using the
    /// plain raw CAN protocol (SW_CAN_PS) with our own ISO-TP segmentation
    /// in software - mirroring the Serial path exactly.
    ///
    /// Earlier this used SW_ISO15765_PS expecting the device firmware to
    /// handle segmentation automatically, but that failed against GM's own
    /// MDI2 DLL (ERR_PIN_INVALID at the filter step). A known-working
    /// open-source implementation (gocan, which explicitly lists "GM MDI"
    /// as verified) does its own ISO-TP on top of raw CAN for this same
    /// GMLAN/SWCAN case, which is the approach used here instead.
    /// </summary>
    public sealed class J2534GmlanChannel : IGmlanChannel, IDisposable
    {
        private readonly uint _protocolId;
        private const uint BaudRate = 33333;
        private const int DefaultTimeoutMs = 2000;
        private const int PollIntervalMs = 20;

        private readonly J2534Native _api;
        private uint _deviceId;
        private uint _channelId;
        private uint _filterId;
        private bool _connected;
        private uint _requestId;
        private byte[] _lastSentFrame = Array.Empty<byte>();

        public J2534GmlanChannel(string dllPath, uint protocolId = J2534Protocol.SW_CAN_PS)
        {
            _api = new J2534Native(dllPath);
            _protocolId = protocolId;
        }

        public void Open()
        {
            _deviceId = _api.Open();
        }

        /// <summary>Device-level sanity check, independent of channel/pin config.</summary>
        public double ReadBatteryVoltage()
        {
            uint mv = _api.ReadBatteryVoltageMillivolts(_deviceId);
            return mv / 1000.0;
        }

        public void SelectAddressing(string requestHeaderHex, string replyFilterHex)
        {
            uint requestId = Convert.ToUInt32(requestHeaderHex, 16);
            uint replyId = Convert.ToUInt32(replyFilterHex, 16);

            if (_connected)
            {
                try { _api.StopFilter(_channelId, _filterId); } catch { /* best effort */ }
                try { _api.Disconnect(_channelId); } catch { /* best effort */ }
                _connected = false;
            }

            // Flags=0: standard 11-bit CAN ID, physical addressing, raw CAN
            // frames (no automatic ISO-TP from the device).
            _channelId = _api.Connect(_deviceId, _protocolId, flags: 0, baudRate: BaudRate);

            // Route the transceiver to pin 1 (GMLAN single-wire CAN).
            // Confirmed by disassembling a working competing tool's IL:
            // this DLL uses vendor-specific parameter 0x8001 (not the
            // generic SAE J1962_PINS/0x0B, which is what caused
            // ERR_PIN_INVALID before) with value 256. Not every vendor DLL
            // recognizes this vendor-specific parameter, so don't treat a
            // failure here as fatal.
            try
            {
                _api.SetConfig(_channelId, (J2534ConfigParam.GM_PIN_SWITCH, 256));
            }
            catch (J2534Exception)
            {
                // Not supported on this DLL - some devices don't need it.
            }

            // Disable loopback: without this, some devices echo our own
            // transmitted frames back through PassThruReadMsgs, which looks
            // exactly like a malformed reply (we'd see our own request SID
            // 0x1A instead of the ECU's 0x5A response).
            try
            {
                _api.SetConfig(_channelId, (J2534ConfigParam.LOOPBACK, 0));
            }
            catch (J2534Exception)
            {
                // Not supported / not needed on this DLL.
            }

            _api.ClearRxBuffer(_channelId);
            _api.ClearTxBuffer(_channelId);

            // Plain pass filter: only deliver frames with this reply ID to
            // ReadMsg. We build and send our own Flow Control frames, so no
            // FLOW_CONTROL_FILTER/ISO15765 automation is needed here.
            _filterId = _api.StartPassFilter(_channelId, _protocolId, replyId);

            _requestId = requestId;
            _connected = true;
        }

        public byte[] SendReceive(byte[] requestPayload, bool tolerateMissingAck, int timeoutMs)
        {
            if (!_connected)
            {
                throw new InvalidOperationException("Call SelectAddressing before sending.");
            }

            int timeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;

            SendIsoTpPayload(requestPayload, timeout);
            return ReceiveIsoTpPayload(tolerateMissingAck, timeout);
        }

        // ---- ISO-TP over raw CAN frames (same shape as IsoTpTransport, byte-array based) ----

        private void SendFrame(byte[] frame, int timeoutMs)
        {
            _lastSentFrame = frame;
            _api.WriteMsg(_channelId, _protocolId, _requestId, frame, (uint)timeoutMs);
        }

        private void SendIsoTpPayload(byte[] payload, int timeoutMs)
        {
            if (payload.Length <= 7)
            {
                var frame = new List<byte> { (byte)payload.Length };
                frame.AddRange(payload);
                SendFrame(frame.ToArray(), timeoutMs);
                return;
            }

            int total = payload.Length;
            var ff = new List<byte>
            {
                (byte)(0x10 | ((total >> 8) & 0x0F)),
                (byte)(total & 0xFF),
            };
            ff.AddRange(payload.Take(6));
            SendFrame(ff.ToArray(), timeoutMs);

            byte[] fc = ReadFrameBlocking(timeoutMs);
            if (fc.Length == 0 || (fc[0] & 0xF0) != 0x30)
            {
                throw new J2534Exception(
                    "Expected a Flow Control frame (0x3_) after the First Frame, but got: " +
                    (fc.Length == 0 ? "(timeout)" : Convert.ToHexString(fc)));
            }

            int sent = 6;
            byte sequence = 1;
            while (sent < total)
            {
                int chunkLen = Math.Min(7, total - sent);
                var cf = new List<byte> { (byte)(0x20 | (sequence & 0x0F)) };
                cf.AddRange(payload.Skip(sent).Take(chunkLen));
                SendFrame(cf.ToArray(), timeoutMs);

                sent += chunkLen;
                sequence = (byte)((sequence % 15) + 1);
            }
        }

        private byte[] ReceiveIsoTpPayload(bool tolerateMissingAck, int timeoutMs)
        {
            byte[] first = ReadFrameBlocking(timeoutMs);
            if (first.Length == 0)
            {
                if (tolerateMissingAck) return Array.Empty<byte>();
                throw new J2534Exception("No response received (timeout).");
            }

            int pciType = (first[0] & 0xF0) >> 4;

            if (pciType == 0x0)
            {
                int len = first[0] & 0x0F;
                return first.Skip(1).Take(len).ToArray();
            }

            if (pciType != 0x1)
            {
                throw new J2534Exception($"Unexpected PCI type 0x{pciType:X} in response: {Convert.ToHexString(first)}");
            }

            int total = ((first[0] & 0x0F) << 8) | first[1];
            var data = new List<byte>();
            data.AddRange(first.Skip(2).Take(Math.Min(6, first.Length - 2)));

            // Send Flow Control: continue, block size 0, separation time 0.
            var fcFrame = new byte[] { 0x30, 0x00, 0x00 };
            SendFrame(fcFrame, timeoutMs);

            while (data.Count < total)
            {
                byte[] cf = ReadFrameBlocking(timeoutMs);
                if (cf.Length == 0 || (cf[0] & 0xF0) != 0x20)
                {
                    throw new J2534Exception("Expected a Consecutive Frame (0x2_) but got: " +
                        (cf.Length == 0 ? "(timeout)" : Convert.ToHexString(cf)));
                }
                int take = Math.Min(total - data.Count, cf.Length - 1);
                data.AddRange(cf.Skip(1).Take(take));
            }

            return data.ToArray();
        }

        /// <summary>
        /// Polls ReadMsg in short slices until data arrives or the overall
        /// timeout elapses. Frames identical to the one we just sent are
        /// silently skipped - some devices echo transmitted frames back
        /// into the receive queue (loopback) even after SetConfig(LOOPBACK,
        /// 0) is requested, so this is a second line of defense against
        /// mistaking our own request for the ECU's reply.
        /// </summary>
        private byte[] ReadFrameBlocking(int overallTimeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(overallTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                byte[] msg = _api.ReadMsg(_channelId, PollIntervalMs);
                if (msg.Length == 0) continue;
                if (_lastSentFrame.Length > 0 && msg.AsSpan().SequenceEqual(_lastSentFrame))
                {
                    continue; // TX echo - not a real reply, keep waiting
                }
                return msg;
            }
            return Array.Empty<byte>();
        }

        public void Close()
        {
            if (_connected)
            {
                try { _api.StopFilter(_channelId, _filterId); } catch { /* best effort */ }
                try { _api.Disconnect(_channelId); } catch { /* best effort */ }
                _connected = false;
            }
            try { _api.Close(_deviceId); } catch { /* best effort */ }
        }

        public void Dispose()
        {
            Close();
            _api.Dispose();
        }
    }
}
