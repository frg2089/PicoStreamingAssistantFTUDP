using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pico4SAFTExtTrackingModule.PicoConnectors;

public enum PicoPrograms
{
    /**
     * Streaming Assitant (SA) was the first program used to connect your PICO
     * device to the computer.
     */
    StreamingAssistant,

    /**
     * PicoConnect is the successor of SA.
     * Currently PicoConnect lacks of proper API to get facetracking data, so
     * we force it to work like the old SA did and that way we can re-use the module.
     */
    PicoConnect,

    /**
     * Business Streaming (BS) is the successor of SA for business devices (PICO 4 enterprise).
     * Due to a internal change since one BS version we keep this (BusinessStreamingUw)
     * entry to refere to "old BS programs".
     * Internally works similar to how SA did.
     */
    BusinessStreamingUw,

    /**
     * Business Streaming (BS) is the successor of SA for business devices (PICO 4 enterprise).
     * Internally works similar to how PicoConnect do.
     */
    BusinessStreaming
}