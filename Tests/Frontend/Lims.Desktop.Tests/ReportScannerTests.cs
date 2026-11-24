using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class ReportScannerTests
{
    [Fact]
    public void SerialChunksAcceptCrLfAndIdleWithoutDuplicatingCrLfPackets()
    {
        var input=new ReportScannerInput();Assert.Empty(input.Append("AR1"));
        var packets=input.Append("G0\r\nAR10TC\n");Assert.Equal(2,packets.Count);Assert.Equal("AR1G0",packets[0]);Assert.Equal("AR10TC",packets[1]);
        Assert.Empty(input.Append("AR1G0"));Assert.Equal("AR1G0",input.Flush());Assert.Null(input.Flush());
    }
    [Theory]
    [InlineData("*AR1G0*\r",1)]
    [InlineData("\u0002]A0*AR10TC*\r",10)]
    [InlineData("LIMS1|AR|1|G0",1)]
    [InlineData("AR292892Z",29289)]
    [InlineData("JP1G0",null)]
    [InlineData("AR1XX",null)]
    public void UsesReactGeneratedAssayCodesAndRejectsSampleStickerOrBadChecksum(string value,int? expected)
        =>Assert.Equal(expected is{} id?(long?)id:null,ReportBarcode.BarcodeId(value));
    [Fact]
    public void AutomaticConnectionPrioritizesUsbScannersAndExcludesBluetoothAndUnknownPorts()
    {
        Assert.True(new ReportScannerPort("COM3","Honeywell barcode reader",@"USB\VID_1234").Score>0);
        Assert.True(new ReportScannerPort("COM6","Bluetooth serial",@"BTHENUM\DEVICE").Score<0);
        Assert.Equal(0,new ReportScannerPort("COM1","COM1","").Score);
    }
    [Theory]
    [InlineData("AR1XX",true)]
    [InlineData("*AR1G0*",true)]
    [InlineData("JP1234",true)]
    [InlineData("AROMATIC AMINES",false)]
    [InlineData("GUAT26011720",false)]
    public void MalformedReportAndSampleCodesAreReportedWithoutTreatingOrdinarySearchAsAScan(string text,bool expected)
        =>Assert.Equal(expected,ReportBarcode.IsReportCandidate(text));
}
