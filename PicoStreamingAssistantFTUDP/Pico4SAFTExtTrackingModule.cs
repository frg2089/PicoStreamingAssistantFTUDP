﻿using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VRCFaceTracking;
using static Pxr;

namespace Pico4SAFTExtTrackingModule;

public class Pico4SAFTExtTrackingModule : ExtTrackingModule
{
    const string IP_ADDRESS = "127.0.0.1";
    const int PORT_NUMBER = 29763; // Temporary port as of current Pico 4 SA app.

    private static UdpClient? udpClient;
    private static IPEndPoint? endPoint;

    private static byte[] receiveBytes = new byte[4096];
    private static PxrFTInfo data = new PxrFTInfo();

    private static readonly bool hasHeader = false;
    private static readonly int pxrHeaderSize = Marshal.SizeOf(typeof(UInt16));
    private static readonly int pxrFtInfoSize = Marshal.SizeOf(typeof(PxrFTInfo));

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        Logger.LogInformation("Initializing UDP client. Accepting on port:" + PORT_NUMBER);
        try
        {
            udpClient = new UdpClient(PORT_NUMBER);
            endPoint = new IPEndPoint(IPAddress.Parse(IP_ADDRESS), PORT_NUMBER);
            Logger.LogInformation("Client success! Receiving PxrFTInfo.");
        }
        catch (Exception)
        {
            Logger.LogInformation("Client failed to create UDP instance.");
            return (false, false);
        }

        return (true, true);
    }

    private static int GetPacketSize() => 
        hasHeader ? pxrHeaderSize + pxrFtInfoSize : pxrFtInfoSize;
    private static int GetPacketIndex() =>
        hasHeader ? pxrHeaderSize : 0;

    private void RetrieveStreamData()
    {
        try
        {
            receiveBytes = udpClient?.Receive(ref endPoint);

            IntPtr ptr = Marshal.AllocHGlobal(GetPacketSize());
            Marshal.Copy(receiveBytes, GetPacketIndex(), ptr, GetPacketSize());
            data = (PxrFTInfo)Marshal.PtrToStructure(ptr, typeof(PxrFTInfo));

            Marshal.FreeHGlobal(ptr);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.ToString());
        }
    }

    public override void Update()
    {
        RetrieveStreamData();
        Logger.LogInformation("JawOpen: " + data.blendShapeWeight[(int)BlendShapeIndex.JawOpen].ToString());
    }

    public override void Teardown()
    {
        Logger.LogInformation("Disposing of PxrFaceTracking UDP Client.");
        udpClient?.Dispose();
    }
}