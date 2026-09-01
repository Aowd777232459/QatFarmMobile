using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public static class ArabicVoiceText
{
    private static readonly Dictionary<string, decimal> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["صفر"]=0,["واحد"]=1,["واحده"]=1,["اثنين"]=2,["اثنان"]=2,["اثنتين"]=2,["ثنتين"]=2,
        ["ثلاث"]=3,["ثلاثه"]=3,["اربع"]=4,["اربعه"]=4,["خمس"]=5,["خمسه"]=5,["ست"]=6,["سته"]=6,
        ["سبع"]=7,["سبعه"]=7,["ثمان"]=8,["ثمانيه"]=8,["تسع"]=9,["تسعه"]=9,["عشر"]=10,["عشره"]=10,
        ["احدعشر"]=11,["اثناعشر"]=12,["اثنيعشر"]=12,["ثلاثعشر"]=13,["اربععشر"]=14,["خمسعشر"]=15,
        ["ستعشر"]=16,["سبععشر"]=17,["ثمانعشر"]=18,["تسععشر"]=19,["عشرين"]=20,["ثلاثين"]=30,
        ["اربعين"]=40,["خمسين"]=50,["ستين"]=60,["سبعين"]=70,["ثمانين"]=80,["تسعين"]=90,
        ["ميه"]=100,["مئه"]=100,["مائه"]=100,["مئتين"]=200,["ميتين"]=200,["ثلاثميه"]=300,
        ["اربعمئه"]=400,["خمسميه"]=500,["ستميه"]=600,["سبعميه"]=700,["ثمانميه"]=800,["تسعميه"]=900
    };

    private static readonly Dictionary<string,int> Months = new(StringComparer.OrdinalIgnoreCase)
    { ["يناير"]=1,["فبراير"]=2,["مارس"]=3,["ابريل"]=4,["مايو"]=5,["يونيو"]=6,["يوليو"]=7,["اغسطس"]=8,["سبتمبر"]=9,["اكتوبر"]=10,["نوفمبر"]=11,["ديسمبر"]=12 };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var b = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant()) b.Append(ch switch
        {
            '٠'=>'0','١'=>'1','٢'=>'2','٣'=>'3','٤'=>'4','٥'=>'5','٦'=>'6','٧'=>'7','٨'=>'8','٩'=>'9',
            'أ' or 'إ' or 'آ'=>'ا','ؤ'=>'و','ئ' or 'ى'=>'ي','ة'=>'ه','ـ'=>' ','،'=>' ',','=>' ','؛'=>' ',';'=>' ',':'=>' ',_=>ch
        });
        return Regex.Replace(b.ToString(), @"\s+", " ").Trim();
    }

    public static bool Has(string text, params string[] values)
    {
        var n = Normalize(text);
        return values.Any(x => n.Contains(Normalize(x), StringComparison.Ordinal));
    }

    public static decimal? MoneyAfter(string text, params string[] markers)
    {
        var n = Normalize(text);
        foreach (var raw in markers.OrderByDescending(x => x.Length))
        {
            var marker = Normalize(raw);
            var i = n.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) continue;
            var result = ParseLeadingNumber(n[(i + marker.Length)..].Trim());
            if (result.HasValue) return result;
        }
        return null;
    }

    public static decimal? AnyNumber(string text, decimal min = 0)
    {
        var n = Normalize(text);
        foreach (Match match in Regex.Matches(n, @"\d+(?:\.\d+)?(?:\s*(?:الف|الاف|مليون|ملايين))?"))
        {
            var v = ParseLeadingNumber(match.Value);
            if (v >= min) return v;
        }
        var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i=0;i<words.Length;i++)
        {
            var v = ParseLeadingNumber(string.Join(' ', words.Skip(i).Take(8)));
            if (v >= min) return v;
        }
        return null;
    }

    public static int? Quantity(string text)
    {
        var n = Normalize(text);
        var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i=1;i<words.Length;i++)
        {
            if (words[i] is not ("حبه" or "حبات" or "حزم" or "حزمه" or "كيلو" or "وحده" or "وحدات")) continue;
            for (var length=Math.Min(4,i);length>=1;length--)
            {
                var v = ParseLeadingNumber(string.Join(' ', words.Skip(i-length).Take(length)));
                if (v is > 0 and <= int.MaxValue) return (int)Math.Round(v.Value,0,MidpointRounding.AwayFromZero);
            }
        }
        var byMarker = MoneyAfter(n,"الكميه","كميه","كمية");
        return byMarker is >0 and <= int.MaxValue ? (int)Math.Round(byMarker.Value,0,MidpointRounding.AwayFromZero) : null;
    }

    public static long? IdAfter(string text, params string[] markers)
    {
        var v = MoneyAfter(text, markers);
        return v is >0 and <= long.MaxValue ? (long)Math.Round(v.Value,0,MidpointRounding.AwayFromZero) : null;
    }

    public static int? Month(string text)
    {
        var n=Normalize(text);
        foreach (var x in Months) if(n.Contains(x.Key,StringComparison.Ordinal)) return x.Value;
        return null;
    }

    public static int? Year(string text)
    {
        var m=Regex.Match(Normalize(text),@"\b20\d{2}\b");
        return m.Success && int.TryParse(m.Value,out var y) ? y : null;
    }

    public static LookupItem? Mentioned(string text, IEnumerable<LookupItem> values)
    {
        var n=Normalize(text); LookupItem? best=null; var bestLength=0;
        foreach(var value in values)
        {
            var baseName=value.Name.Split('—')[0].Trim();
            var name=Normalize(baseName);
            if(name.Length>=2 && n.Contains(name,StringComparison.Ordinal) && name.Length>bestLength){best=value;bestLength=name.Length;}
        }
        return best;
    }

    public static decimal? ParseLeadingNumber(string text)
    {
        var n=Compound(Normalize(text));
        if(string.IsNullOrWhiteSpace(n)) return null;
        var digit=Regex.Match(n,@"^(?<n>\d+(?:\.\d+)?)\s*(?<m>الف|الاف|مليون|ملايين)?");
        if(digit.Success && decimal.TryParse(digit.Groups["n"].Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var dv))
            return dv*(digit.Groups["m"].Value is "الف" or "الاف"?1000m:digit.Groups["m"].Value is "مليون" or "ملايين"?1000000m:1m);

        decimal total=0,current=0; var used=false;
        foreach(var raw in n.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(8))
        {
            var w=raw; if(w=="و") continue; if(w.StartsWith('و')&&w.Length>1) w=w[1..];
            if(w is "الف" or "الاف"){total+=(current==0?1:current)*1000;current=0;used=true;continue;}
            if(w=="الفين"){total+=2000;current=0;used=true;continue;}
            if(w is "مليون" or "ملايين"){total+=(current==0?1:current)*1000000;current=0;used=true;continue;}
            if(Numbers.TryGetValue(w,out var v)){current+=v;used=true;continue;}
            break;
        }
        return used?total+current:null;
    }

    private static string Compound(string text)
    {
        foreach(var pair in new Dictionary<string,string>{{"احد عشر","احدعشر"},{"اثنا عشر","اثناعشر"},{"اثني عشر","اثنيعشر"},{"ثلاثه عشر","ثلاثعشر"},{"ثلاث عشر","ثلاثعشر"},{"اربعه عشر","اربععشر"},{"اربع عشر","اربععشر"},{"خمسه عشر","خمسعشر"},{"خمس عشر","خمسعشر"},{"سته عشر","ستعشر"},{"ست عشر","ستعشر"},{"سبعه عشر","سبععشر"},{"سبع عشر","سبععشر"},{"ثمانيه عشر","ثمانعشر"},{"ثمان عشر","ثمانعشر"},{"تسعه عشر","تسععشر"},{"تسع عشر","تسععشر"}})
            text=text.Replace(pair.Key,pair.Value,StringComparison.Ordinal);
        return text;
    }
}
