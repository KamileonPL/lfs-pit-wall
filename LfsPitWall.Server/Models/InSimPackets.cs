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
    TINY_VTC = 5,      // Vote cancel
    TINY_SCP = 6,      // Send camera pos
    TINY_SST = 7,      // Send state info
    TINY_GTM = 8,      // Get time in ms
    TINY_MPE = 9,      // Multi player end
    TINY_ISM = 10,     // Get multiplayer info
    TINY_REN = 11,     // Race end
    TINY_CLR = 12,     // All players cleared
    TINY_NCN = 13,     // Get NCN for all connections
    TINY_NPL = 14,     // Get all players
    TINY_RES = 15,     // Get all results
    TINY_RST = 19,     // Get race start info
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
            Flags = (ushort)InSimFlags.ISF_MCI,                  // MCI already includes node, lap, position and speed
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
/// Multiplayer notification packet - sent when a host is started or joined.
/// Reply to TINY_ISM request.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_ISM
{
    public byte Size;                    // 40
    public byte Type;                    // ISP_ISM
    public byte ReqI;                    // 0 unless reply to TINY_ISM request
    public byte Zero;                    // 0

    public byte Host;                    // 0 = guest / 1 = host
    public byte Sp1;                     // Spare
    public byte Sp2;                     // Spare
    public byte Sp3;                     // Spare

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] HName;                 // Name of the host joined or started
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

    /// <summary>
    /// Creates a request TINY packet for specific subtype
    /// </summary>
    public static IS_TINY CreateRequest(TinyPacketType subType)
    {
        return new IS_TINY
        {
            Size = 1,
            Type = (byte)InSimPacketType.ISP_TINY,
            ReqI = 0,
            SubT = (byte)subType
        };
    }
}

/// <summary>
/// Session State packet - reports current session state (28 bytes)
/// Reply to TINY_SST request
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_STA
{
    public byte Size;                    // 28
    public byte Type;                    // ISP_STA
    public byte ReqI;                    // ReqI if replying to request
    public byte Zero;                    // 0

    public float ReplaySpeed;            // 1.0 is normal speed

    public ushort Flags;                 // ISS state flags
    public byte InGameCam;               // Which camera is selected
    public byte ViewPLID;                // Unique ID of viewed player (0 = none)

    public byte NumP;                    // Number of players in race
    public byte NumConns;                // Number of connections including host
    public byte NumFinished;             // Number finished or qualified
    public byte RaceInProg;              // 0 = no race / 1 = race / 2 = qualifying

    public byte QualMins;                // Qualifying minutes
    public byte RaceLaps;                // Race laps
    public byte Sp2;                     // Spare
    public byte ServerStatus;            // 0 = unknown / 1 = success / > 1 = fail

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Track;                 // Track name (6 chars, e.g. FE2R)

    public byte Weather;                 // 0, 1, 2...
    public byte Wind;                    // 0 = off / 1 = weak / 2 = strong
}

/// <summary>
/// Race Start packet - reports race/qualifying start info (28 bytes)
/// Reply to TINY_RST request
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_RST
{
    public byte Size;                    // 28
    public byte Type;                    // ISP_RST
    public byte ReqI;                    // 0 unless reply to TINY_RST request
    public byte Zero;                    // 0

    public byte RaceLaps;                // 0 if qualifying
    public byte QualMins;                // 0 if racing
    public byte NumP;                    // Number of players in race
    public byte Timing;                  // Lap timing mode (see bits 0-1 and 6-7)

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Track;                 // Track name (e.g., FE2R)

    public byte Weather;                 // 0, 1, 2...
    public byte Wind;                    // 0 = off / 1 = weak / 2 = strong

    public ushort Flags;                 // Race flags (HOSTF_x)
    public ushort NumNodes;              // Total number of nodes in track path
    public ushort Finish;                // Node index - finish line
    public ushort Split1;                // Node index - split 1
    public ushort Split2;                // Node index - split 2
    public ushort Split3;                // Node index - split 3
}

/// <summary>
/// New Player packet - sent when a player joins the race
/// Physical size: 76 bytes, but Size field = 76 / 4 = 19 (INSIM_VERSION 10+)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_NPL
{
    public byte Size;                    // 76
    public byte Type;                    // ISP_NPL
    public byte ReqI;                    // 0 unless this is a reply to an TINY_NPL request
    public byte PLID;                    // player's newly assigned unique id

    public byte UCID;                    // connection's unique id
    public byte PType;                   // bit 0: female / bit 1: AI / bit 2: remote
    public ushort Flags;                 // player flags

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
    public byte[] PName;                 // nickname

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Plate;                 // number plate - NO ZERO AT END!

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public byte[] CName;                 // car name

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] SName;                 // skin name

    public byte Tyres0;                  // Tyre compound for wheel 0 (FL)
    public byte Tyres1;                  // Tyre compound for wheel 1 (FR)
    public byte Tyres2;                  // Tyre compound for wheel 2 (RL)
    public byte Tyres3;                  // Tyre compound for wheel 3 (RR)

    public byte H_Mass;                  // Added mass (kg)
    public byte H_TRes;                  // Traction restrictions (%)
    public byte Model;                   // Driver model: 0=others view, 1=own view, 2=first person
    public byte Pass;                    // Passengers byte

    public byte RWAdj;                   // low 4 bits: tyre width reduction (rear)
    public byte FWAdj;                   // low 4 bits: tyre width reduction (front)
    public byte Sp2;                     // Spare
    public byte Sp3;                     // Spare

    public byte SetF;                    // Setup flags
    public byte NumP;                    // Number in race (0 if join request)
    public byte Config;                  // Configuration
    public byte Fuel;                    // Fuel percent or 255
}

/// <summary>
/// Player Leave packet - sent when a player leaves the race
/// Physical size: 4 bytes, but Size field = 4 / 4 = 1 (INSIM_VERSION 10+)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_PLL
{
    public byte Size;                    // 1 (4 / 4)
    public byte Type;                    // ISP_PLL
    public byte ReqI;                    // 0
    public byte PLID;                    // Player ID
}

/// <summary>
/// Lap Time packet - sent when a player completes a lap (20 bytes)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_LAP
{
    public byte Size;                    // 20
    public byte Type;                    // ISP_LAP
    public byte ReqI;                    // 0
    public byte PLID;                    // Player ID

    public uint LTime;                   // Lap time (ms)
    public uint ETime;                   // Total elapsed time (ms)

    public ushort LapsDone;              // Laps completed
    public ushort Flags;                 // Player flags

    public byte Sp0;                     // Spare
    public byte Penalty;                 // Current penalty value (PENALTY_x)
    public byte NumStops;                // Number of pit stops
    public byte Fuel200;                 // Fuel: if 255 then disabled, else actual_fuel = Fuel200 / 2
}

/// <summary>
/// Split Time packet - sent when a player crosses sector splits (16 bytes)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_SPX
{
    public byte Size;                    // 16
    public byte Type;                    // ISP_SPX
    public byte ReqI;                    // 0
    public byte PLID;                    // Player ID

    public uint STime;                   // Split time (ms)
    public uint ETime;                   // Total elapsed time (ms)

    public byte Split;                   // Split number (1, 2, 3)
    public byte Penalty;                 // Current penalty value (PENALTY_x)
    public byte NumStops;                // Number of pit stops
    public byte Fuel200;                 // Fuel: if 255 then disabled, else actual_fuel = Fuel200 / 2
}

/// <summary>
/// New Connection packet - sent when a player connects to the host
/// Physical size: 56 bytes (Size = 14)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_NCN
{
    public byte Size;                    // 14 (56 / 4)
    public byte Type;                    // ISP_NCN
    public byte ReqI;                    // 0 unless reply to TINY_NCN request
    public byte UCID;                    // Unique Connection ID (0 = host)

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
    public byte[] UName;                 // Username (24 bytes)

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
    public byte[] PName;                 // Nickname/Player name (24 bytes)

    public byte Admin;                   // 1 if admin
    public byte Total;                   // Total connections including host
    public byte Flags;                   // Connection flags
    public byte Sp3;                     // Spare
}

/// <summary>
/// Connection Leave packet - sent when a player disconnects from the host
/// Physical size: 8 bytes (Size = 2)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_CNL
{
    public byte Size;                    // 2 (8 / 4)
    public byte Type;                    // ISP_CNL
    public byte ReqI;                    // 0
    public byte UCID;                    // Connection's unique ID that left

    public byte Reason;                  // Leave reason
    public byte Total;                   // Total connections remaining
    public byte Sp2;                     // Spare
    public byte Sp3;                     // Spare
}

/// <summary>
/// Result packet - sent for race results or qualifying results
/// Physical size: 76 bytes (Size = 19)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_RES
{
    public byte Size;                    // 19 (76 / 4)
    public byte Type;                    // ISP_RES
    public byte ReqI;                    // 0 unless reply to TINY_RES request
    public byte PLID;                    // Player ID

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
    public byte[] PName;                 // Player name

    public byte Mode;                    // Mode (0=qualifying, 1=race)
    public byte Gear;                    // Final gear
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Spare;                 // Spare bytes
}

/// <summary>
/// Car state info structure (used in IS_MCI packet)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CompCar
{
    public ushort Node;                  // Current path node
    public ushort Lap;                   // Current lap
    public byte PLID;                    // Player ID
    public byte Position;                // Race position: 1 = leader
    public byte Info;                    // CCI_* flags
    public byte Sp3;                     // Spare
    public int X;                        // X map (65536 = 1 metre)
    public int Y;                        // Y map (65536 = 1 metre)
    public int Z;                        // Z altitude (65536 = 1 metre)
    public ushort Speed;                 // Speed (32768 = 100 m/s)
    public ushort Direction;             // Direction of motion
    public ushort Heading;               // Car heading
    public short AngVel;                 // Rate of change of heading
}

/// <summary>
/// Multi Car Info packet header for a variable-sized packet.
/// Physical size: 4 + (NumC * 28) bytes, Size field = packet_size / 4.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IS_MCI
{
    public byte Size;                    // Packet size / 4
    public byte Type;                    // ISP_MCI
    public byte ReqI;                    // 0
    public byte NumCars;                 // Number of valid CompCar structs in this packet
}
