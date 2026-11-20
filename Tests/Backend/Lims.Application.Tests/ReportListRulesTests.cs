using Lims.Application.Planning;

namespace Lims.Application.Tests;

public sealed class ReportListRulesTests
{
    [Theory]
    [InlineData("PH","ANALYTICAL","manual")]
    [InlineData("Formaldehído","ANALYTICAL","manual")]
    [InlineData("AZO","CHEMICAL","scanner")]
    [InlineData("BISPHENOL","ANALYTICAL","scanner")]
    [InlineData("CROCKING","PHYSICAL","manual")]
    [InlineData("Otra","Analítico","scanner")]
    [InlineData("Otra","ANALYTICLA","scanner")]
    [InlineData("PH","ANALYTICLA","manual")]
    public void ModesFollowLegacyExclusionsAndAnalyticalRules(string test,string lab,string expected)=>Assert.Equal(expected,ReportListRules.CheckMode(test,lab));
    [Theory]
    [InlineData("LIMS1-AR-1-G0",1)]
    [InlineData("AR-1-G0",1)]
    [InlineData("\u0002]A0*AR10TC*\r",10)]
    [InlineData("*LIMS1AR1G0*",1)]
    [InlineData("]C1LIMS1-AR-1-G0",1)]
    [InlineData("LIMS1-AR-1-XX",null)]
    [InlineData("GUAT-PRUEBA-001",null)]
    public void BarcodeChecksumAndFormatsAreVerified(string code,int? expected)=>Assert.Equal(expected is {} value?(long?)value:null,ReportListRules.BarcodeId(code));
}
