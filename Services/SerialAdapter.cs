using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace SaabDidTool.Services
{
    /// <summary>
    /// Thin wrapper around System.IO.Ports.SerialPort for talking to an
    /// OBDLink MX+ / Vlinker MC+ / other STN11xx-STN21xx based adapter that
    /// exposes a Bluetooth SPP or USB virtual COM port.
    ///
    /// This is deliberately "dumb": it sends a text command terminated with
    /// CR and reads back raw text until it sees the '>' prompt (standard
    /// ELM/STN behaviour) or a timeout elapses. All CAN/ISO-TP framing
    /// lives in IsoTpTransport - this class knows nothing about that.
    /// </summary>
    public sealed class SerialAdapter : IDisposable
    {
        private SerialPort _port;

        public bool IsOpen => _port != null && _port.IsOpen;

        public event Action<string> RawTx;
        public event Action<string> RawRx;

        public void Open(string portName, int baudRate)
        {
            Close();

            _port = new SerialPort(portName, baudRate)
            {
                NewLine = "\r",
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true,
            };
            _port.Open();

            // Give the adapter a moment after the port opens (common with
            // Bluetooth SPP adapters that need to finish their handshake).
            Thread.Sleep(300);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public void Close()
        {
            if (_port != null)
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        _port.Close();
                    }
                }
                catch
                {
                    // best-effort close
                }
                _port.Dispose();
                _port = null;
            }
        }

        /// <summary>
        /// Runs the standard init sequence used on the forum: echo off,
        /// spaces off, then the STN-specific commands that put the adapter
        /// into SW-CAN (single-wire / GMLAN "I-bus") mode at 33.333 kbps.
        /// </summary>
        public void RunInitSequence(int perCommandTimeoutMs = 1500)
        {
            SendCommand("ATE0", perCommandTimeoutMs);   // echo off
            SendCommand("ATS0", perCommandTimeoutMs);   // spaces off
            SendCommand("STP61", perCommandTimeoutMs);  // protocol: SW-CAN (GMLAN)
            SendCommand("STCSWM2", perCommandTimeoutMs); // single-wire CAN mode 2
        }

        public void SetHeader(string headerHex, int timeoutMs = 1500)
        {
            SendCommand("ATSH" + headerHex, timeoutMs);
        }

        public void SetReplyFilter(string filterHex, int timeoutMs = 1500)
        {
            SendCommand("ATCRA" + filterHex, timeoutMs);
        }

        /// <summary>
        /// Sends one line (a command, or a raw hex frame string) and reads
        /// back everything up to the next '>' prompt. Returns the raw text
        /// response with the prompt character stripped.
        /// </summary>
        public string SendCommand(string command, int timeoutMs = 1500)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Serial port is not open.");
            }

            _port.DiscardInBuffer();
            RawTx?.Invoke(command);
            _port.Write(command + "\r");

            string response = ReadUntilPrompt(timeoutMs);
            RawRx?.Invoke(response);
            return response;
        }

        private string ReadUntilPrompt(int timeoutMs)
        {
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int originalTimeout = _port.ReadTimeout;
            _port.ReadTimeout = 100;

            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        int b = _port.ReadByte();
                        if (b < 0)
                        {
                            continue;
                        }

                        char c = (char)b;
                        if (c == '>')
                        {
                            break;
                        }
                        sb.Append(c);
                    }
                    catch (TimeoutException)
                    {
                        // keep polling until the outer deadline
                    }
                }
            }
            finally
            {
                _port.ReadTimeout = originalTimeout;
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            Close();
        }
    }
}
