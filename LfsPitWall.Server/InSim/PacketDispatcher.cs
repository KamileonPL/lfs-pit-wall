using LfsPitWall.Server.Models;

namespace LfsPitWall.Server.InSim;

/// <summary>
/// Routes InSim packets to registered handlers using a typed binding pattern.
/// Usage:
///   dispatcher.Bind&lt;IS_MCI&gt;(InSimPacketType.ISP_MCI, HandleMultiCarInfo);
///   dispatcher.Bind&lt;IS_STA&gt;(InSimPacketType.ISP_STA, HandleSessionState);
/// </summary>
public class PacketDispatcher
{
    private readonly Dictionary<InSimPacketType, Action<byte[]>> _handlers = new();
    private readonly HashSet<InSimPacketType> _suppressed = new();
    private readonly ILogger _logger;

    public PacketDispatcher(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a typed handler for a specific packet type.
    /// The raw byte array is automatically deserialized to the target struct.
    /// </summary>
    public void Bind<T>(InSimPacketType packetType, Action<T> handler) where T : struct
    {
        _handlers[packetType] = data => handler(InSimConnection.BytesToStruct<T>(data));
    }

    /// <summary>
    /// Marks packet types as known-but-ignored to suppress "unhandled" log noise.
    /// </summary>
    public void Suppress(params InSimPacketType[] types)
    {
        foreach (var type in types)
            _suppressed.Add(type);
    }

    /// <summary>
    /// Routes a packet to its registered handler, or logs if unhandled.
    /// </summary>
    public void Dispatch(InSimPacketType packetType, byte[] data)
    {
        if (_handlers.TryGetValue(packetType, out var handler))
        {
            try
            {
                handler(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet {PacketType}", packetType);
            }
        }
        else if (!_suppressed.Contains(packetType))
        {
            _logger.LogDebug("Unhandled packet: {PacketType}", packetType);
        }
    }
}
