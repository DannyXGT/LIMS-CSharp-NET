namespace Lims.Desktop.Planning;

/// <summary>A fixed operational minute, independent of the server audit clock.</summary>
public sealed class ReportOperationalClock
{
    public DateTimeOffset? FixedAt {get;private set;}
    public void Clear()=>FixedAt=null;
    public DateTimeOffset ForScan(DateTimeOffset now)
    {
        if(FixedAt is{} at)return at;
        var local=now.ToOffset(TimeSpan.FromHours(-6));
        return new(local.Year,local.Month,local.Day,local.Hour,local.Minute,0,local.Offset);
    }
    public void Fix(DateTimeOffset now)
    {
        var local=now.ToOffset(TimeSpan.FromHours(-6));
        FixedAt=new(local.Year,local.Month,local.Day,local.Hour,local.Minute,0,local.Offset);
    }
    public static string GroupKey(DateTimeOffset? at)=>at?.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)??"";
}
