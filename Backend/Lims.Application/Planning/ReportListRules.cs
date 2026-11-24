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
    public static long? BarcodeId(string raw)=>Lims.Contracts.Planning.ReportBarcode.BarcodeId(raw);
}
