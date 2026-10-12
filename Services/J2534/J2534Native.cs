using System;
using System.Runtime.InteropServices;

namespace SaabDidTool.Services.J2534
{
    // ---- J2534-1 v04.04 constants ----
    public static class J2534Protocol
    {
        public const uint CAN = 0x05;
        public const uint ISO15765 = 0x06;

        // J2534-2 "pin switched" protocol IDs (needed for GMLAN single-wire
        // CAN, which lives on pin 1 rather than the standard J1962 pins).
        public const uint SW_ISO15765_PS = 0x8007;
        public const uint SW_CAN_PS = 0x8008;
    }

    public static class J2534Ioctl
    {
        public const uint GET_CONFIG = 0x01;
        public const uint SET_CONFIG = 0x02;
        public const uint READ_VBATT = 0x03;
        public const uint CLEAR_TX_BUFFER = 0x07;
        public const uint CLEAR_RX_BUFFER = 0x08;
    }

    public static class J2534ConfigParam
    {
        public const uint DATA_RATE = 0x01;
        public const uint LOOPBACK = 0x03;
        public const uint NODE_ADDRESS = 0x04;
        public const uint J1962_PINS = 0x0B; // (PinNumber2 << 8) | PinNumber1
        public const uint ISO15765_BS = 0x1E;
        public const uint ISO15765_STMIN = 0x1F;

        // Vendor-specific (GM/Bosch BVTX) pin-switch parameter, confirmed by
        // disassembling a working competing tool's IL: SetConfig(0x8001, 256)
        // is what actually routes the transceiver to pin 1 on this DLL -
        // the generic SAE J1962_PINS (0x0B) is not what this vendor uses.
        public const uint GM_PIN_SWITCH = 0x8001;
    }

    public static class J2534FilterType
    {
        public const uint PASS_FILTER = 0x01;
        public const uint FLOW_CONTROL_FILTER = 0x03;
    }

    public static class J2534TxFlags
    {
        public const uint ISO15765_FRAME_PAD = 0x40;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SCONFIG
    {
        public uint Parameter;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SCONFIG_LIST
    {
        public uint NumOfParams;
        public IntPtr ConfigPtr;
    }

    // Data is a fixed 4128-byte buffer per spec; DataSize says how much is used.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PASSTHRU_MSG
    {
        public uint ProtocolID;
        public uint RxStatus;
        public uint TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;

        public static PASSTHRU_MSG Create(uint protocolId, uint txFlags, byte[] payload)
        {
            var msg = new PASSTHRU_MSG
            {
                ProtocolID = protocolId,
                RxStatus = 0,
                TxFlags = txFlags,
                Timestamp = 0,
                ExtraDataIndex = 0,
                Data = new byte[4128],
            };
            if (payload != null)
            {
                Array.Copy(payload, msg.Data, payload.Length);
                msg.DataSize = (uint)payload.Length;
            }
            return msg;
        }

        public byte[] GetData()
        {
            var result = new byte[DataSize];
            Array.Copy(Data, result, DataSize);
            return result;
        }
    }

    /// <summary>
    /// Loads a vendor's J2534 DLL at runtime (path comes from the Windows
    /// registry or a manual browse - see J2534Registry) and exposes the
    /// handful of functions this app needs.
    ///
    /// IMPORTANT: most J2534 DLLs (including GM's for MDI/MDI2) are 32-bit
    /// only. This project is built with PlatformTarget=x86 for that reason
    /// - if your specific DLL ships a 64-bit build, that can be changed.
    /// </summary>
    public sealed class J2534Native : IDisposable
    {
        private delegate int PassThruOpenDelegate(IntPtr name, out uint deviceId);
        private delegate int PassThruCloseDelegate(uint deviceId);
        private delegate int PassThruConnectDelegate(uint deviceId, uint protocolId, uint flags, uint baudRate, out uint channelId);
        private delegate int PassThruDisconnectDelegate(uint channelId);
        private delegate int PassThruReadMsgsDelegate(uint channelId, IntPtr msgs, ref uint numMsgs, uint timeout);
        private delegate int PassThruWriteMsgsDelegate(uint channelId, IntPtr msgs, ref uint numMsgs, uint timeout);
        private delegate int PassThruStartMsgFilterDelegate(
            uint channelId, uint filterType, IntPtr maskMsg, IntPtr patternMsg, IntPtr flowControlMsg, out uint filterId);
        private delegate int PassThruStopMsgFilterDelegate(uint channelId, uint filterId);
        private delegate int PassThruIoctlDelegate(uint channelId, uint ioctlId, IntPtr input, IntPtr output);
        private delegate int PassThruGetLastErrorDelegate(IntPtr errorText256);

        private readonly IntPtr _libraryHandle;
        private readonly PassThruOpenDelegate _open;
        private readonly PassThruCloseDelegate _close;
        private readonly PassThruConnectDelegate _connect;
        private readonly PassThruDisconnectDelegate _disconnect;
        private readonly PassThruReadMsgsDelegate _readMsgs;
        private readonly PassThruWriteMsgsDelegate _writeMsgs;
        private readonly PassThruStartMsgFilterDelegate _startMsgFilter;
        private readonly PassThruStopMsgFilterDelegate _stopMsgFilter;
        private readonly PassThruIoctlDelegate _ioctl;
        private readonly PassThruGetLastErrorDelegate _getLastError;

        public J2534Native(string dllPath)
        {
            _libraryHandle = NativeLibrary.Load(dllPath);
            _open = Bind<PassThruOpenDelegate>("PassThruOpen");
            _close = Bind<PassThruCloseDelegate>("PassThruClose");
            _connect = Bind<PassThruConnectDelegate>("PassThruConnect");
            _disconnect = Bind<PassThruDisconnectDelegate>("PassThruDisconnect");
            _readMsgs = Bind<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _writeMsgs = Bind<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _startMsgFilter = Bind<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _stopMsgFilter = Bind<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _ioctl = Bind<PassThruIoctlDelegate>("PassThruIoctl");
            _getLastError = Bind<PassThruGetLastErrorDelegate>("PassThruGetLastError");
        }

        private T Bind<T>(string exportName) where T : Delegate
        {
            IntPtr ptr = NativeLibrary.GetExport(_libraryHandle, exportName);
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        private void ThrowIfError(int result, string action)
        {
            if (result != 0)
            {
                string detail = GetLastError();
                throw new J2534Exception($"{action} failed (code {result}): {detail}");
            }
        }

        public string GetLastError()
        {
            IntPtr buf = Marshal.AllocHGlobal(256);
            try
            {
                _getLastError(buf);
                return Marshal.PtrToStringAnsi(buf) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>
        /// Reads battery voltage in millivolts via the device-level
        /// READ_VBATT IOCTL. This doesn't depend on any channel/protocol
        /// setup, so it's a good first sanity check that the device is
        /// actually talking to the vehicle at all.
        /// </summary>
        public uint ReadBatteryVoltageMillivolts(uint deviceId)
        {
            IntPtr outPtr = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                int rc = _ioctl(deviceId, J2534Ioctl.READ_VBATT, IntPtr.Zero, outPtr);
                ThrowIfError(rc, "PassThruIoctl(READ_VBATT)");
                return (uint)Marshal.ReadInt32(outPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(outPtr);
            }
        }

        public uint Open()
        {
            int rc = _open(IntPtr.Zero, out uint deviceId);
            ThrowIfError(rc, "PassThruOpen");
            return deviceId;
        }

        public void Close(uint deviceId) => _close(deviceId);

        public uint Connect(uint deviceId, uint protocolId, uint flags, uint baudRate)
        {
            int rc = _connect(deviceId, protocolId, flags, baudRate, out uint channelId);
            ThrowIfError(rc, "PassThruConnect");
            return channelId;
        }

        public void Disconnect(uint channelId) => _disconnect(channelId);

        public void SetConfig(uint channelId, params (uint parameter, uint value)[] items)
        {
            int count = items.Length;
            int itemSize = Marshal.SizeOf<SCONFIG>();
            IntPtr arrayPtr = Marshal.AllocHGlobal(itemSize * count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var cfg = new SCONFIG { Parameter = items[i].parameter, Value = items[i].value };
                    Marshal.StructureToPtr(cfg, IntPtr.Add(arrayPtr, i * itemSize), false);
                }

                var list = new SCONFIG_LIST { NumOfParams = (uint)count, ConfigPtr = arrayPtr };
                IntPtr listPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SCONFIG_LIST>());
                try
                {
                    Marshal.StructureToPtr(list, listPtr, false);
                    int rc = _ioctl(channelId, J2534Ioctl.SET_CONFIG, listPtr, IntPtr.Zero);
                    ThrowIfError(rc, "PassThruIoctl(SET_CONFIG)");
                }
                finally
                {
                    Marshal.FreeHGlobal(listPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(arrayPtr);
            }
        }

        public void ClearRxBuffer(uint channelId) => _ioctl(channelId, J2534Ioctl.CLEAR_RX_BUFFER, IntPtr.Zero, IntPtr.Zero);
        public void ClearTxBuffer(uint channelId) => _ioctl(channelId, J2534Ioctl.CLEAR_TX_BUFFER, IntPtr.Zero, IntPtr.Zero);

        /// <summary>
        /// Sets up an ISO15765 flow-control filter: incoming frames matching
        /// replyId are treated as our diagnostic responses, and the device
        /// auto-answers First Frames from that ECU with a Flow Control
        /// frame addressed as requestId (so segmentation/reassembly happens
        /// in the adapter's firmware, not in our code).
        /// </summary>
        /// <summary>
        /// A plain pass filter: only frames matching (id &amp; mask) == pattern
        /// are delivered to ReadMsg. Used with raw CAN protocol, where we do
        /// our own ISO-TP segmentation instead of relying on ISO15765
        /// device automation.
        /// </summary>
        public uint StartPassFilter(uint channelId, uint protocolId, uint replyId)
        {
            byte[] idBytes4(uint id) => new byte[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            var mask = PASSTHRU_MSG.Create(protocolId, 0, idBytes4(0xFFFFFFFF));
            var pattern = PASSTHRU_MSG.Create(protocolId, 0, idBytes4(replyId));

            IntPtr maskPtr = StructToPtr(mask);
            IntPtr patternPtr = StructToPtr(pattern);
            try
            {
                int rc = _startMsgFilter(
                    channelId, J2534FilterType.PASS_FILTER, maskPtr, patternPtr, IntPtr.Zero, out uint filterId);
                ThrowIfError(rc, "PassThruStartMsgFilter(PASS_FILTER)");
                return filterId;
            }
            finally
            {
                Marshal.FreeHGlobal(maskPtr);
                Marshal.FreeHGlobal(patternPtr);
            }
        }

        public uint StartFlowControlFilter(uint channelId, uint protocolId, uint requestId, uint replyId)
        {
            byte[] idBytes4(uint id) => new byte[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            byte[] maskData = idBytes4(0xFFFFFFFF);
            byte[] patternData = idBytes4(replyId);
            byte[] fcData = idBytes4(requestId);

            var mask = PASSTHRU_MSG.Create(protocolId, 0, maskData);
            var pattern = PASSTHRU_MSG.Create(protocolId, 0, patternData);
            var flowControl = PASSTHRU_MSG.Create(protocolId, 0, fcData);

            IntPtr maskPtr = StructToPtr(mask);
            IntPtr patternPtr = StructToPtr(pattern);
            IntPtr fcPtr = StructToPtr(flowControl);
            try
            {
                int rc = _startMsgFilter(
                    channelId, J2534FilterType.FLOW_CONTROL_FILTER, maskPtr, patternPtr, fcPtr, out uint filterId);
                ThrowIfError(rc, "PassThruStartMsgFilter");
                return filterId;
            }
            finally
            {
                Marshal.FreeHGlobal(maskPtr);
                Marshal.FreeHGlobal(patternPtr);
                Marshal.FreeHGlobal(fcPtr);
            }
        }

        public void StopFilter(uint channelId, uint filterId) => _stopMsgFilter(channelId, filterId);

        public void WriteMsg(uint channelId, uint protocolId, uint requestId, byte[] payload, uint timeoutMs)
        {
            var data = new byte[4 + payload.Length];
            data[0] = (byte)(requestId >> 24);
            data[1] = (byte)(requestId >> 16);
            data[2] = (byte)(requestId >> 8);
            data[3] = (byte)requestId;
            Array.Copy(payload, 0, data, 4, payload.Length);

            var msg = PASSTHRU_MSG.Create(protocolId, J2534TxFlags.ISO15765_FRAME_PAD, data);
            IntPtr msgPtr = StructToPtr(msg);
            try
            {
                uint numMsgs = 1;
                int rc = _writeMsgs(channelId, msgPtr, ref numMsgs, timeoutMs);
                ThrowIfError(rc, "PassThruWriteMsgs");
            }
            finally
            {
                Marshal.FreeHGlobal(msgPtr);
            }
        }

        /// <summary>
        /// Reads one reassembled ISO15765 message (the device firmware
        /// already stripped the CAN ID and handled multi-frame
        /// segmentation/flow-control). Returns just the UDS payload bytes,
        /// or an empty array on timeout.
        /// </summary>
        public byte[] ReadMsg(uint channelId, uint timeoutMs)
        {
            int itemSize = Marshal.SizeOf<PASSTHRU_MSG>();
            IntPtr bufPtr = Marshal.AllocHGlobal(itemSize);
            try
            {
                uint numMsgs = 1;
                int rc = _readMsgs(channelId, bufPtr, ref numMsgs, timeoutMs);
                if (rc != 0 || numMsgs == 0)
                {
                    return Array.Empty<byte>(); // timeout / no data - caller decides if that's fatal
                }

                var msg = Marshal.PtrToStructure<PASSTHRU_MSG>(bufPtr);
                byte[] full = msg.GetData();
                // First 4 bytes are the CAN ID the device prefixes onto the
                // message; the UDS payload follows.
                if (full.Length <= 4)
                {
                    return Array.Empty<byte>();
                }
                var payload = new byte[full.Length - 4];
                Array.Copy(full, 4, payload, 0, payload.Length);
                return payload;
            }
            finally
            {
                Marshal.FreeHGlobal(bufPtr);
            }
        }

        private static IntPtr StructToPtr(PASSTHRU_MSG msg)
        {
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<PASSTHRU_MSG>());
            Marshal.StructureToPtr(msg, ptr, false);
            return ptr;
        }

        public void Dispose()
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_libraryHandle);
            }
        }
    }

    public sealed class J2534Exception : Exception
    {
        public J2534Exception(string message) : base(message) { }
    }
}
