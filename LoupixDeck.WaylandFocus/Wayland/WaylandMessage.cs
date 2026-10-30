namespace LoupixDeck.WaylandFocus.Wayland;

/// <summary>One received Wayland wire message: sender object, event opcode and raw payload.</summary>
public readonly record struct WaylandMessage(uint SenderId, ushort Opcode, byte[] Payload);