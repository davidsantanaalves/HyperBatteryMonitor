using HyperXBatteryTray.Devices;
using Xunit;

namespace HyperBatteryMonitor.Tests;

public sealed class CloudStingerCoreWirelessDeviceTests
{
    [Theory]
    [InlineData(@"\\?\hid#vid_03f0&pid_0a93&mi_03&col03#sample", true)]
    [InlineData(@"\\?\hid#vid_0951&pid_170b&mi_03&col03#sample", false)]
    [InlineData(null, false)]
    public void ShouldValidateBatteryResponse_IsLimitedToHpPlayStationReceiver(
        string? devicePath,
        bool expected)
    {
        Assert.Equal(
            expected,
            CloudStingerCoreWirelessDevice.ShouldValidateBatteryResponse(devicePath));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0x12, 0x05 }, true)]
    [InlineData(new byte[] { 0xFF, 0x01, 0x00 }, false)]
    [InlineData(new byte[] { 0xFF, 0x12 }, false)]
    public void IsBatteryResponse_RequiresObservedHpPlayStationPrefix(
        byte[] response,
        bool expected)
    {
        Assert.Equal(
            expected,
            CloudStingerCoreWirelessDevice.IsBatteryResponse(response));
    }
}
