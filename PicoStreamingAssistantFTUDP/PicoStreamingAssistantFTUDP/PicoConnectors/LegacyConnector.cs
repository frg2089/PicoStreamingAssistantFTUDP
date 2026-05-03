using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

namespace Pico4SAFTExtTrackingModule.PicoConnectors;

/// <summary>
/// Connector class for Streaming Assitant & Business Streaming.
/// Also used for PICO Connect on `mergetype=2`
/// </summary>
public sealed partial class LegacyConnector : IPicoConnector
{
    private const string IP_ADDRESS = "127.0.0.1";
    private const int PORT_NUMBER = 29765;

    private static readonly int s_pxrHeaderSize = Unsafe.SizeOf<TrackingDataHeader>();
    private static readonly int s_pxrFtInfoSize = Unsafe.SizeOf<PxrFTInfo>();
    private static readonly int s_packetIndex = s_pxrHeaderSize;
    private static readonly int s_packetSize = s_pxrHeaderSize + s_pxrFtInfoSize;

    private bool _disposedValue;
    private bool _connecting;
    private readonly Lock _socketLock = new Lock();
    private readonly ILogger _logger;
    private UdpClient? _udpClient;
    private IPEndPoint? _endPoint;
    private PxrFTInfo _data;
    private Thread? _tryReinitializeThread;

    private readonly string _processName;

    public LegacyConnector(ILogger Logger, PicoPrograms program_using)
    {
        _logger = Logger;

        _processName = program_using switch
        {
            PicoPrograms.StreamingAssistant => "Streaming Assistant",
            PicoPrograms.BusinessStreamingV1 or PicoPrograms.BusinessStreaming => "Business Streaming",
            PicoPrograms.PicoConnect => "PICO Connect",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(_processName))
        {
            // shouldn't reach this
            LogWarningUnknownProcess(program_using);
            _processName = "[?]";
        }
    }

    public bool Connect()
    {
        lock (_socketLock)
        {
            _disposedValue = false;
            _connecting = true;
        }

        try
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    lock (_socketLock)
                    {
                        _udpClient = new UdpClient(PORT_NUMBER);
                        _endPoint = new IPEndPoint(IPAddress.Parse(IP_ADDRESS), PORT_NUMBER);
                    }
                    // Since Streaming Assistant is already running,
                    // this module is indeed needed,
                    // so the timeout failure is unnecessary.
                    // udpClient.Client.ReceiveTimeout = 15000; // Initialization timeout.

                    LogDebugHostEndpoint(_endPoint);
                    LogDebugTimeout(_udpClient.Client.ReceiveTimeout);
                    LogDebugEstablished();

                    LogWaiting(_processName);


                    if (ReceivePxrData(ref _data, reinit: false))
                    {
                        LogHandshakeSuccess(_processName);

                        _udpClient.Client.ReceiveTimeout = 5000;

                        return true;
                    }

                    return false;
                }
                catch (SocketException ex) when (ex.ErrorCode is 10048)
                {
                    // Magic
                    // Close the pico_et_ft_bt_bridge.exe process and reinitialize it.
                    // It will listen to UDP port before pico_et_ft_bt_bridge.exe runs.
                    // Note: exclusively to simplify older versions of the FT bridge,
                    // the bridge now works without any need for process killing.
                    Process proc = new()
                    {
                        StartInfo =
                    {
                        FileName = "taskkill.exe",
                        ArgumentList =
                        {
                            "/f",
                            "/t",
                            "/im",
                            "pico_et_ft_bt_bridge.exe"
                        },
                        CreateNoWindow = true
                    }
                    };
                    proc.Start();
                    proc.WaitForExit();
                }
                catch (Exception e)
                {
                    LogWarning(e);
                    return false;
                }
            }

            return false;
        }
        finally
        {
            lock (_socketLock)
            {
                _connecting = false;
            }
        }
    }

    public ReadOnlySpan<float> GetBlendShapes()
    {
        lock (_socketLock)
        {
            if (_connecting)
                return [];
        }

        if (!ReceivePxrData(ref _data))
            return [];

        return _data.blendShapeWeight;
    }

    public void Teardown()
    {
        lock (_socketLock)
        {
            bool needsTeardown = !_disposedValue;
            if (!needsTeardown)
                return;

            _disposedValue = true;
        }

        LogTeardown();
        lock (_socketLock)
        {
            if (_udpClient is not null)
            {
                _udpClient.Client.Blocking = false;
                _udpClient.Client.Shutdown(SocketShutdown.Receive);
                _udpClient.Client.Close();
                _udpClient.Dispose();
            }
        }

        _tryReinitializeThread?.Join();

        lock (_socketLock)
        {
            _udpClient = null;
            _endPoint = null;
        }
    }

    private bool ReceivePxrData(ref PxrFTInfo pData, bool reinit = true)
    {
        Debug.Assert(_udpClient is not null);
        if (IsDisposed())
            return false;

        try
        {
            Span<byte> span = _udpClient.Receive(ref _endPoint);
            if (span.IsEmpty)
                return false;

            var tdh = MemoryMarshal.Read<TrackingDataHeader>(span);
            if (tdh.tracking_type != 2)
                return false; // not facetracking packet

            pData = MemoryMarshal.Read<PxrFTInfo>(span[s_packetIndex..]);
            return true;
        }
        catch (SocketException ex) when (ex.ErrorCode is 10060)
        {
            // socket time out
            LogDebugReceivePxrDataError(ex);
            if (reinit)
            {
                LogReInitialize();

                // try to reinitialize
                Teardown();
                lock (_socketLock)
                {
                    _tryReinitializeThread = new(() =>
                    {
                        bool connected;
                        do
                        {
                            connected = Connect();
                            if (!connected)
                                Thread.Sleep(200); // try again; we have to set a low number because VRCFT won't call `Teardown()` until all the updates are done
                        } while (!IsDisposed() && !connected);
                    });
                    _tryReinitializeThread.Start();
                }
            }

            return false; // got byte failed
        }
        catch (SocketException ex) when (ex.ErrorCode is 10004)
        {
            // `Teardown()` called
            LogSocketClosed();
            return false; // got byte failed
        }
    }

    public bool IsDisposed()
    {
        lock (_socketLock)
        {
            return _disposedValue;
        }
    }

    public string GetProcessName() => _processName;


    [LoggerMessage(LogLevel.Warning, "Unhandled Exception")]
    private partial void LogWarning(Exception exception);


    [LoggerMessage(LogLevel.Warning, "Couldn't find the name for program {type}")]
    private partial void LogWarningUnknownProcess(PicoPrograms type);

    [LoggerMessage(LogLevel.Debug, "Host end-point: {endPoint}")]
    private partial void LogDebugHostEndpoint(EndPoint endPoint);
    [LoggerMessage(LogLevel.Debug, "Initialization Timeout: {timeout}ms")]
    private partial void LogDebugTimeout(int timeout);
    [LoggerMessage(LogLevel.Debug, "Client established: attempting to receive PxrFTInfo.")]
    private partial void LogDebugEstablished();

    [LoggerMessage(LogLevel.Debug, "Data was not sent within the timeout.")]
    private partial void LogDebugReceivePxrDataError(Exception exception);

    [LoggerMessage(LogLevel.Information, "Waiting for {process} data stream.")]
    private partial void LogWaiting(string process);

    [LoggerMessage(LogLevel.Information, "{process} handshake success.")]
    private partial void LogHandshakeSuccess(string process);

    [LoggerMessage(LogLevel.Information, "Disposing of PxrFaceTracking UDP Client.")]
    private partial void LogTeardown();

    [LoggerMessage(LogLevel.Information, "Data was not sent within the timeout (is headset hibernated?), reinitialize...")]
    private partial void LogReInitialize();

    [LoggerMessage(LogLevel.Information, "Socket closed")]
    private partial void LogSocketClosed();
}
