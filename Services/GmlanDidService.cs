using System;
using System.Linq;
using SaabDidTool.Interfaces;
using SaabDidTool.Models;

namespace SaabDidTool.Services
{
    public sealed class DidReadResult
    {
        public byte ResponseSid { get; set; }
        public byte Did { get; set; }
        public byte[] Value { get; set; } = Array.Empty<byte>();
        public string RawHex => Convert.ToHexString(Value);
    }

    /// <summary>
    /// Implements ReadDataByIdentifier ($1A) and WriteDataByIdentifier ($3B)
    /// per GMW3110, against a REC or UEC target, over any IGmlanChannel
    /// (Serial/OBDLink-Vlinker, or J2534/MDI2).
    /// </summary>
    public sealed class GmlanDidService
    {
        private const byte ReadSid = 0x1A;
        private const byte ReadPositiveResponseSid = 0x5A;
        private const byte WriteSid = 0x3B;

        private readonly IGmlanChannel _channel;

        public EcuTarget CurrentTarget { get; private set; }

        public GmlanDidService(IGmlanChannel channel)
        {
            _channel = channel;
        }

        public void SelectTarget(EcuTarget target)
        {
            CurrentTarget = target;
            _channel.SelectAddressing(target.Header, target.ReplyFilter);
        }

        public DidReadResult ReadDid(byte did)
        {
            EnsureTargetSelected();

            byte[] request = { ReadSid, did };
            byte[] response = _channel.SendReceive(request, tolerateMissingAck: false, timeoutMs: 2000);

            if (response.Length < 2)
            {
                throw new InvalidOperationException($"Read response too short: {Convert.ToHexString(response)}");
            }
            if (response[0] != ReadPositiveResponseSid)
            {
                throw new InvalidOperationException(
                    $"Unexpected response SID 0x{response[0]:X2} (expected 0x{ReadPositiveResponseSid:X2}). " +
                    "This may be a negative response (e.g. 0x7F) - the DID might not exist on this ECU/model year.");
            }
            if (response[1] != did)
            {
                throw new InvalidOperationException($"Response DID 0x{response[1]:X2} does not match requested DID 0x{did:X2}.");
            }

            return new DidReadResult
            {
                ResponseSid = response[0],
                Did = response[1],
                Value = response.Skip(2).ToArray(),
            };
        }

        /// <summary>
        /// Writes newValue back to the given DID. newValue must be the same
        /// length as the value most recently read from that DID - GMLAN
        /// modules here expect the full data block back, not just the
        /// changed byte(s). No checksum is recomputed: the trailing bytes
        /// pass through unchanged in the traces this is based on.
        /// </summary>
        public void WriteDid(byte did, byte[] newValue)
        {
            EnsureTargetSelected();

            var request = new byte[2 + newValue.Length];
            request[0] = WriteSid;
            request[1] = did;
            Array.Copy(newValue, 0, request, 2, newValue.Length);

            // Some ECUs don't send an explicit positive-response ack to a
            // WriteDataByIdentifier over this framing - tolerate that and
            // let the caller verify with a follow-up ReadDid.
            byte[] response = _channel.SendReceive(request, tolerateMissingAck: true, timeoutMs: 2000);
            if (response.Length > 0)
            {
                if (response[0] == 0x7F)
                    throw new InvalidOperationException($"ECU rejected DID 0x{did:X2} write: {Convert.ToHexString(response)}");
                if (response[0] != 0x7B || response.Length < 2 || response[1] != did)
                    throw new InvalidOperationException($"Unexpected write response: {Convert.ToHexString(response)}");
            }
            // A missing positive response is not proof of success; caller must re-read.

        }

        private void EnsureTargetSelected()
        {
            if (CurrentTarget == null)
            {
                throw new InvalidOperationException("No ECU target selected. Call SelectTarget first.");
            }
        }
    }
}
