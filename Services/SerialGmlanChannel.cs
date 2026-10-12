using SaabDidTool.Interfaces;

namespace SaabDidTool.Services
{
    /// <summary>
    /// The original path: OBDLink/Vlinker-style STN adapter over a virtual
    /// COM port, with our own ISO-TP framing (see IsoTpTransport).
    /// </summary>
    public sealed class SerialGmlanChannel : IGmlanChannel
    {
        private readonly SerialAdapter _adapter;
        private readonly IsoTpTransport _transport;

        public SerialGmlanChannel(SerialAdapter adapter)
        {
            _adapter = adapter;
            _transport = new IsoTpTransport(adapter);
        }

        public void SelectAddressing(string requestHeaderHex, string replyFilterHex)
        {
            _adapter.SetHeader(requestHeaderHex);
            _adapter.SetReplyFilter(replyFilterHex);
        }

        public byte[] SendReceive(byte[] requestPayload, bool tolerateMissingAck, int timeoutMs)
        {
            return _transport.SendReceive(requestPayload, tolerateMissingAck, timeoutMs);
        }
    }
}
