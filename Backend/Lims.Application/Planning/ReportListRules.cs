using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lims.Application.Planning;

public static class ReportListRules
{
    private static readonly string[] Patterns=["METALES","PVC TEST","NICKEL","CADMIUM LEAD","CHROMIUM VI","HEAVY METAL","EXTRACTABLE HEAVY METAL","X RAY","TOTAL HEAVY METAL","TIN CONTENT","TOXIC ELEMENT","ICP MS","ZINC METAL CONTENT TEST","TOTAL ANTIMONY","TOTAL ARSENIC","TOTAL MERCURY","TOTAL COPPER","TOTAL GOLD","TOTAL BORON","TOTAL COBALT","TOTAL ORGANIC FLUORINE","PHTHALATES","QUINOLINE","BISPHENOL","DISPERSE","SCCPS MCCPS","APEOS","PFCS PFAS","ORGANOTIN","COC","OPP","PAH","PCP","VOC","DMFA DMFU","AZO","PVC"];
    private static readonly string[] Excluded=["AM","PH","FORMALDEHYDE","FORMALDEHIDO","FIBRA","FIBER","COMPOSITION NON TEXTILE","COMPOSITE TEXTILE","COMPOSITE TEXTIL"];
    public static string Token(string value)=>Regex.Replace(new string(value.Normalize(NormalizationForm.FormKD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant(),@"[^A-Z0-9]+"," ").Trim();
    private static string CanonicalLaboratory(string value)
    {
        var token=Token(value).Replace(" ","",StringComparison.Ordinal);
        var alias=token switch{"ANALITICO" or "ANALITICA" or "ANALYTIC"=>"ANALYTICAL","QUIMICO" or "QUIMICA"=>"CHEMICAL","FISICO" or "FISICA"=>"PHYSICAL","SUBCONTRACT" or "SUBCONTRACTO"=>"SUBCONTRATO",_=>null};
        if(alias is not null)return alias;
        var candidates=new[]{"ANALYTICAL","CHEMICAL","PHYSICAL","SUBCONTRATO"};
        static int Distance(string left,string right)
        {
            var previous=Enumerable.Range(0,right.Length+1).ToArray();
            for(var i=0;i<left.Length;i++)
            {var current=new int[right.Length+1];current[0]=i+1;for(var j=0;j<right.Length;j++)current[j+1]=Math.Min(Math.Min(current[j]+1,previous[j+1]+1),previous[j]+(left[i]==right[j]?0:1));previous=current;}
            return previous[^1];
        }
        var nearest=candidates.MinBy(candidate=>Distance(token,candidate))!;
        return Distance(token,nearest)<=2?nearest:token;
    }
    public static string CheckMode(string? test,string? lab)
    {
        var token=Token(test??"");
        if(token.Length==0||Excluded.Contains(token,StringComparer.Ordinal))return "manual";
        return Patterns.Any(p=>token.Contains(p,StringComparison.Ordinal))||CanonicalLaboratory(lab??"")=="ANALYTICAL"?"scanner":"manual";
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
