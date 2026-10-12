namespace SaabDidTool.Interfaces
{
    /// <summary>
    /// Abstracts "send a UDS/GMLAN request, get the full reassembled
    /// response payload back" - whether that's done by our own hand-rolled
    /// ISO-TP over a serial AT/ST adapter, or by a J2534 device's firmware
    /// doing ISO15765 segmentation natively.
    /// </summary>
    public interface IGmlanChannel
    {
        void SelectAddressing(string requestHeaderHex, string replyFilterHex);

        /// <summary>
        /// Sends requestPayload (e.g. [0x1A, did] or [0x3B, did, ...data])
        /// and returns the full response payload (e.g. [0x5A, did, ...data]).
        /// If tolerateMissingAck is true, a timeout returns an empty array
        /// instead of throwing (used for writes some ECUs don't ack).
        /// </summary>
        byte[] SendReceive(byte[] requestPayload, bool tolerateMissingAck, int timeoutMs);
    }
}
