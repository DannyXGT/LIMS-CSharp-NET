using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class ReportOperationalClockTests
{
    [Fact]
    public void ScannerUsesCurrentMinuteWithoutManualSetupAndHonorsAnOptionalFixedClock()
    {
        var clock=new ReportOperationalClock();
        var now=new DateTimeOffset(2026,11,24,18,32,57,TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026,11,24,12,32,0,TimeSpan.FromHours(-6)),clock.ForScan(now));
        clock.Fix(now.AddHours(-1));Assert.Equal(clock.FixedAt,clock.ForScan(now.AddHours(2)));
        clock.Clear();Assert.Equal(14,clock.ForScan(now.AddHours(2)).Hour);
    }
    [Fact]
    public void FixedMinuteStaysUntilExplicitlyReplacedAndGroupsAcrossTimeZoneRepresentations()
    {
        var clock=new ReportOperationalClock();Assert.Null(clock.FixedAt);
        clock.Fix(new(2026,11,23,20,35,49,TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026,11,23,14,35,0,TimeSpan.FromHours(-6)),clock.FixedAt);
        Assert.Equal("2026-11-23 14:35",ReportOperationalClock.GroupKey(clock.FixedAt));
        Assert.Equal(ReportOperationalClock.GroupKey(clock.FixedAt),ReportOperationalClock.GroupKey(clock.FixedAt!.Value.ToUniversalTime()));
        clock.Fix(new(2026,11,24,0,2,1,TimeSpan.Zero));
        Assert.Equal("2026-11-23 18:02",ReportOperationalClock.GroupKey(clock.FixedAt));
    }
    [Fact]
    public void DirectCheckRequiresTheCorrectCaptureModeAndCorrectionPermission()
    {
        var execution=new PlanningRow(Guid.NewGuid(),Guid.NewGuid(),"JOB",null,null,"Technical","APEOS","Analytical",null,null,null,"Sin información",null,null,"ReportList",null,null,false);
        var item=new ReportListItem(execution,0,false,null,"scanner");
        Assert.False(ReportListTableModel.CanCheck(item,true,false,false));
        Assert.True(ReportListTableModel.CanCheck(item,true,false,true));
        item=item with{CheckMode="manual"};Assert.True(ReportListTableModel.CanCheck(item,true,false,false));
        item=item with{Execution=item.Execution with{DigitizedAt=DateTimeOffset.UtcNow,TestedOn=new(2026,11,23)}};
        Assert.False(ReportListTableModel.CanCheck(item,true,false,false));
        Assert.True(ReportListTableModel.CanCheck(item,false,true,false));
    }
    [Fact]
    public void OperationalColumnsFitOneViewportAndKeepRemainsVisible()
    {
        var model=new ReportListTableModel();model.Fit(1080);
        Assert.InRange(model.Columns.Sum(c=>c.Width),1079.99,1080.01);
        Assert.Contains(model.Columns,c=>c.Key=="Keep");Assert.Contains(model.Columns,c=>c.Key=="Check");
        Assert.DoesNotContain(model.Columns,c=>c.Key=="Selection");
        model.Fit(1440);Assert.InRange(model.Columns.Sum(c=>c.Width),1439.99,1440.01);
    }
}
