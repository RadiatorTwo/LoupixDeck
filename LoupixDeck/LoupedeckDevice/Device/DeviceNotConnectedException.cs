namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Thrown when a command that needs a real answer from the device is issued while the serial
/// link is down. Write-only commands (draws, LED colours, brightness) are silently skipped in
/// that state so the app keeps running with a dead link, but a caller that reads the reply
/// payload must be able to tell "no device" from "device answered" — an empty result would be
/// indistinguishable from success and would be parsed as if it held data.
/// </summary>
public sealed class DeviceNotConnectedException(string message = "The device is not connected.")
    : Exception(message);
