using System.Runtime.InteropServices;

namespace LfsPitWall.Server.Models;

/// <summary>
/// InSim packet type enumeration (fourth byte of InSim packets)
/// </summary>
public enum InSimPacketType : byte
{
    ISP_NONE = 0,      // not used
    ISP_ISI = 1,       // InSim Init
    ISP_VER = 2,       // Version info
    ISP_TINY = 3,      // Multi purpose
    ISP_SMALL = 4,     // Multi purpose
    ISP_STA = 5,       // State info
    ISP_SCH = 6,       // Single character
    ISP_SFP = 7,       // State flags pack
    ISP_SCC = 8,       // Set car camera
    ISP_CPP = 9,       // Cam pos pack
    ISP_ISM = 10,      // Start multiplayer
    ISP_MSO = 11,      // Message out
    ISP_III = 12,      // Hidden /i message
    ISP_MST = 13,      // Type message or /command
    ISP_MTC = 14,      // Message to a connection
    ISP_MOD = 15,      // Set screen mode
    ISP_VTN = 16,      // Vote notification
    ISP_RST = 17,      // Race start
    ISP_NCN = 18,      // New connection
    ISP_CNL = 19,      // Connection left
    ISP_CPR = 20,      // Connection renamed
    ISP_NPL = 21,      // New player (joined race)
    ISP_PLP = 22,      // Player pit
    ISP_PLL = 23,      // Player leave
    ISP_LAP = 24,      // Lap time
    ISP_SPX = 25,      // Split x time
    ISP_PIT = 26,      // Pit stop start
    ISP_PSF = 27,      // Pit stop finish
    ISP_PLA = 28,      // Pit lane enter / leave
    ISP_CCH = 29,      // Camera changed
    ISP_PEN = 30,      // Penalty given or cleared
    ISP_TOC = 31,      // Take over car
    ISP_FLG = 32,      // Flag (yellow or blue)
    ISP_PFL = 33,      // Player flags (help flags)
    ISP_FIN = 34,      // Finished race
    ISP_RES = 35,      // Result confirmed
    ISP_REO = 36,      // Reorder
    ISP_NLP = 37,      // Node and lap packet
    ISP_MCI = 38,      // Multi car info
    ISP_MSX = 39,      // Type message
    ISP_MSL = 40,      // Message to local computer
    ISP_CRS = 41,      // Car reset
    ISP_BFN = 42,      // Delete buttons
    ISP_AXI = 43,      // Autocross layout information
    ISP_AXO = 44,      // Hit an autocross object
    ISP_BTN = 45,      // Show a button
    ISP_BTC = 46,      // Button click
    ISP_BTT = 47,      // Button text input
    ISP_RIP = 48,      // Replay information packet
    ISP_SSH = 49,      // Screenshot
    ISP_CON = 50,      // Contact between cars
    ISP_OBH = 51,      // Contact car + object
    ISP_HLV = 52,      // Report incidents
    ISP_PLC = 53,      // Player cars
    ISP_AXM = 54,      // Autocross multiple objects
    ISP_ACR = 55,      // Admin command report
    ISP_HCP = 56,      // Car handicaps
    ISP_NCI = 57,      // New connection - extra info for host
    ISP_JRR = 58,      // Reply to a join request
    ISP_UCO = 59,      // InSim checkpoint / circle
    ISP_OCO = 60,      // Object control
    ISP_TTC = 61,      // Multi purpose - target to connection
    ISP_SLC = 62,      // Connection selected a car
    ISP_CSC = 63,      // Car state changed
    ISP_CIM = 64,      // Connection's interface mode
    ISP_MAL = 65,      // Set mods allowed
    ISP_PLH = 66,      // Set player handicaps
    ISP_IPB = 67,      // Set IP bans
    ISP_AIC = 68,      // Set AI control value
    ISP_AII = 69,      // Info about AI car
}

/// <summary>
/// TINY subtype enumeration
/// </summary>
public enum TinyPacketType : byte
{
    TINY_NONE = 0,     // Keep alive
    TINY_VER = 1,      // Get version
    TINY_CLOSE = 2,    // Close InSim
    TINY_PING = 3,     // Ping request
    TINY_REPLY = 4,    // Ping reply
}

/// <summary>
/// InSim flags for IS_ISI packet
/// </summary>
[Flags]
public enum InSimFlags : ushort
{
    ISF_RES_0 = 1,         // Bit 0: spare
    ISF_RES_1 = 2,         // Bit 1: spare
    ISF_LOCAL = 4,         // Bit 2: guest or single player
    ISF_MSO_COLS = 8,      // Bit 3: keep colours in MSO text
    ISF_NLP = 16,          // Bit 4: receive NLP packets
    ISF_MCI = 32,          // Bit 5: receive MCI packets
    ISF_CON = 64,          // Bit 6: receive CON packets
    ISF_OBH = 128,         // Bit 7: receive OBH packets
    ISF_HLV = 256,         // Bit 8: receive HLV packets
    ISF_AXM_LOAD = 512,    // Bit 9: receive AXM when loading
    ISF_AXM_EDIT = 1024,   // Bit 10: receive AXM when editing
    ISF_REQ_JOIN = 2048,   // Bit 11: process join requests
}

/// <summary>
/// InSim Init packet - sent to initialize the InSim system
/// Physical size: 44 bytes, but Size field = 44 / 4 = 11 (INSIM_VERSION 10+)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_ISI
{
    public byte Size;                    // 11 (44 / 4 in INSIM_VERSION 10+)
    public byte Type;                    // ISP_ISI
    public byte ReqI;                    // Non-zero to request IS_VER packet in reply
    public byte Zero;                    // 0

    public ushort UDPPort;               // Port for UDP replies (0 for none on TCP)
    public ushort Flags;                 // Bit flags for options

    public byte InSimVer;                // INSIM_VERSION (10)
    public byte Prefix;                  // Special host message prefix character
    public ushort Interval;              // Time in ms between NLP or MCI (0 = none)

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] Admin;                 // Admin password (if set in LFS)

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] IName;                 // Program name

    /// <summary>
    /// Creates a default IS_ISI packet for initializing LFS connection
    /// </summary>
    /// <param name="programName">Program name (max 15 chars) - displayed in LFS InSim connections list</param>
    /// <param name="adminPassword">Admin password if required by LFS (max 15 chars), empty string for no password</param>
    public static IS_ISI CreateDefault(string programName = "LFS Pit Wall", string adminPassword = "")
    {
        var packet = new IS_ISI
        {
            Size = 44 / 4,                                         // INSIM_VERSION 10+: Size is packet_size / 4
            Type = (byte)InSimPacketType.ISP_ISI,
            ReqI = 1,                                              // Request IS_VER reply
            Zero = 0,
            UDPPort = 0,                                           // Use TCP for NLP/MCI
            Flags = (ushort)(InSimFlags.ISF_NLP | InSimFlags.ISF_MCI), // Receive NLP and MCI packets
            InSimVer = 10,                                         // INSIM_VERSION 10
            Prefix = 0,                                            // No special prefix
            Interval = 200,                                        // 200ms interval for NLP/MCI
            Admin = new byte[16],                                  // Will fill with admin password
            IName = new byte[16]                                   // Will fill with program name
        };

        // Copy program name into IName field (null-terminated)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(programName ?? "LFS Pit Wall");
        int nameLength = Math.Min(nameBytes.Length, 15); // Leave room for null terminator
        Array.Copy(nameBytes, packet.IName, nameLength);
        packet.IName[nameLength] = 0; // Null terminator

        // Copy admin password into Admin field (null-terminated)
        if (!string.IsNullOrEmpty(adminPassword))
        {
            var adminBytes = System.Text.Encoding.ASCII.GetBytes(adminPassword);
            int adminLength = Math.Min(adminBytes.Length, 15); // Leave room for null terminator
            Array.Copy(adminBytes, packet.Admin, adminLength);
            packet.Admin[adminLength] = 0; // Null terminator
        }
        // Admin field is already zeroed by default initialization

        return packet;
    }
}

/// <summary>
/// Version info packet - sent by LFS in reply to IS_ISI
/// Physical size: 20 bytes, but Size field = 20 / 4 = 5 (INSIM_VERSION 10+)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_VER
{
    public byte Size;                    // 5 (20 / 4 in INSIM_VERSION 10+)
    public byte Type;                    // ISP_VER
    public byte ReqI;                    // ReqI from request packet
    public byte Zero;                    // 0

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Version;               // LFS version, e.g. "0.3G"

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Product;               // Product: DEMO / S1 / S2 / S3

    public byte InSimVer;                // InSim version
    public byte Spare;                   // Spare

    /// <summary>
    /// Gets the LFS version as a string
    /// </summary>
    public string GetVersion()
    {
        if (Version == null) return "Unknown";
        return System.Text.Encoding.ASCII.GetString(Version).TrimEnd('\0');
    }

    /// <summary>
    /// Gets the product name as a string
    /// </summary>
    public string GetProduct()
    {
        if (Product == null) return "Unknown";
        return System.Text.Encoding.ASCII.GetString(Product).TrimEnd('\0');
    }
}

/// <summary>
/// Tiny packet - physical size: 4 bytes, but Size field = 4 / 4 = 1 (INSIM_VERSION 10+)
/// Used for simple messages
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_TINY
{
    public byte Size;                    // 1 (4 / 4 in INSIM_VERSION 10+)
    public byte Type;                    // ISP_TINY
    public byte ReqI;                    // 0 unless reply to request
    public byte SubT;                    // Subtype from TINY_ enumeration

    /// <summary>
    /// Creates a keep-alive TINY packet
    /// </summary>
    public static IS_TINY CreateKeepAlive()
    {
        return new IS_TINY
        {
            Size = 1,                                              // 4 bytes / 4 = 1
            Type = (byte)InSimPacketType.ISP_TINY,
            ReqI = 0,
            SubT = (byte)TinyPacketType.TINY_NONE
        };
    }
}
