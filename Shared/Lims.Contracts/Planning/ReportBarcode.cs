using System.Globalization;
using System.Text.RegularExpressions;

namespace Lims.Contracts.Planning;

/// <summary>Canonical AR identity emitted by LIMS React assay report workbooks.</summary>
public static class ReportBarcode
{
    public static bool IsReportCandidate(string raw)
    {
        var token=Regex.Replace(raw??"",@"[\x00-\x1F\x7F\uFEFF\s]","").ToUpperInvariant();
        token=Regex.Replace(token,@"^\][A-Z][0-9]","").Trim('*');
        return BarcodeId(raw??"") is not null||Regex.IsMatch(token,@"^(?:LIMS1(?:[|-]?AR)?|AR|JP)[|-]?[0-9]");
    }
    public static long? BarcodeId(string raw)
    {
        var token=Regex.Replace(raw??"",@"[\x00-\x1F\x7F\uFEFF\s]","").ToUpperInvariant();
        token=Regex.Replace(token,@"^\][A-Z][0-9]","").Trim('*');
        token=Regex.Replace(token,@"^LIMS1AR([|-])","LIMS1-AR$1");
        string idToken,checksum;bool decimalId;
        var compact=Regex.Match(token,@"^(?:LIMS1)?AR([0-9A-Z]+)([0-9A-Z]{2})$");
        if(compact.Success){idToken=compact.Groups[1].Value;checksum=compact.Groups[2].Value;decimalId=idToken.All(char.IsAsciiDigit);}
        else
        {
            var parts=token.Split(['|','-'],StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length==4&&parts[0]=="LIMS1"&&parts[1]=="AR"){idToken=parts[2];checksum=parts[3];decimalId=true;}
            else if(parts.Length==3&&parts[0]=="AR"){idToken=parts[1];checksum=parts[2];decimalId=false;}
            else return null;
        }
        long id=0;
        try{foreach(var ch in idToken){var digit=ch is >= '0' and <= '9'?ch-'0':ch-'A'+10;if(digit<0||digit>=(decimalId?10:36))return null;id=checked(id*(decimalId?10:36)+digit);}}catch(OverflowException){return null;}
        if(id<=0||id>9007199254740991)return null;
        var acc=17;foreach(var ch in id.ToString(CultureInfo.InvariantCulture))acc=(acc*31+ch)%1296;
        const string digits="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return checksum==$"{digits[acc/36]}{digits[acc%36]}"?id:null;
    }
}
