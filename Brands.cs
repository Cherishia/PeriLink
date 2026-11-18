using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LinkScope
{
    internal static class Brands
    {
        private static readonly Dictionary<string,string> Usb = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"046D","LOGITECH"}, {"1532","RAZER"}, {"1B1C","CORSAIR"}, {"1038","STEELSERIES"},
            {"3434","KEYCHRON"}, {"31E3","WOOTING"}, {"2DC8","8BITDO"}, {"057E","NINTENDO"},
            {"0F0D","HORI"}, {"05AC","APPLE"}, {"0B05","ASUS"}, {"091E","GARMIN"},
            {"0B0E","JABRA"}, {"1395","SENNHEISER"}, {"0951","KINGSTON"},
            {"046A","CHERRY"}, {"1E7D","ROCCAT"}, {"03F0","HP"},
            {"413C","DELL"}, {"17EF","LENOVO"}, {"056E","ELECOM"},
            {"0738","MAD CATZ"}, {"06A3","SAITEK"}, {"044F","THRUSTMASTER"}, {"0E6F","PDP"},
            {"20D6","POWERA"}
        };
        private static readonly Dictionary<string,string> Words = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"LUNAFURY","LUNAFURY"},{"LUNA FURY","LUNAFURY"},{"怒喵","ANGRY MIAO"},{"ANGRY MIAO","ANGRY MIAO"},
            {"LOGITECH","LOGITECH"},{"LOGI","LOGITECH"},{"罗技","LOGITECH"},{"罗技G","LOGITECH"},
            {"ROG","ROG"},{"REPUBLIC OF GAMERS","ROG"},{"玩家国度","ROG"},{"ASUSTEK","ASUS"},{"华硕","ASUS"},
            {"RAZER","RAZER"},{"雷蛇","RAZER"},{"CORSAIR","CORSAIR"},{"海盗船","CORSAIR"},
            {"STEELSERIES","STEELSERIES"},{"赛睿","STEELSERIES"},{"HYPERX","HYPERX"},{"极度未知","HYPERX"},
            {"SONY","SONY"},{"索尼","SONY"},{"PLAYSTATION","PLAYSTATION"},{"XBOX","XBOX"},{"MICROSOFT","MICROSOFT"},
            {"KEYCHRON","KEYCHRON"},{"京造","京造"},{"优联","LOGITECH"},{"NUPHY","NUPHY"},{"WOOTING","WOOTING"},
            {"CHERRY","CHERRY"},{"樱桃","CHERRY"},{"Ducky","DUCKY"},{"IQUNIX","IQUNIX"},{"AKKO","AKKO"},
            {"EPOMAKER","EPOMAKER"},{"AJAZZ","AJAZZ"},{"黑爵","AJAZZ"},{"VGN","VGN"},{"ATK","ATK"},
            {"VXE","VXE"},{"LAMZU","LAMZU"},{"ENDGAME GEAR","ENDGAME GEAR"},{"PULSAR","PULSAR"},
            {"FINALMOUSE","FINALMOUSE"},{"GLORIOUS","GLORIOUS"},{"ZOWIE","ZOWIE"},{"卓威","ZOWIE"},
            {"ROCCAT","ROCCAT"},{"TURTLE BEACH","TURTLE BEACH"},{"RAPOO","RAPOO"},{"雷柏","RAPOO"},
            {"DAREU","DAREU"},{"达尔优","DAREU"},{"MCHOSE","MCHOSE"},{"迈从","MCHOSE"},
            {"DARMOSHARK","DARMOSHARK"},{"魔鲨","DARMOSHARK"},{"INCOTT","INCOTT"},{"英科特","INCOTT"},
            {"DELUX","DELUX"},{"多彩","DELUX"},{"REDRAGON","REDRAGON"},{"红龙","REDRAGON"},
            {"FUHLEN","FUHLEN"},{"富勒","FUHLEN"},{"AULA","AULA"},{"狼蛛","AULA"},
            {"FEKER","FEKER"},{"WOMIER","WOMIER"},{"WEIKAV","WEIKAV"},{"MONSGEEK","MONSGEEK"},
            {"珂芝","KZZI"},{"KZZI","KZZI"},{"腹灵","FL ESPORTS"},{"FL ESPORTS","FL ESPORTS"},
            {"LOFREE","LOFREE"},{"洛斐","LOFREE"},{"LEOBOG","LEOBOG"},
            {"狼途","LANGTU"},{"LANGTU","LANGTU"},{"雷神","THUNDEROBOT"},{"THUNDEROBOT","THUNDEROBOT"},
            {"SCYROX","SCYROX"},{"VALKYRIE","VALKYRIE"},{"瓦尔基里","VALKYRIE"},
            {"SIMGOT","SIMGOT"},{"兴戈","SIMGOT"},{"FIIO","FIIO"},{"飞傲","FIIO"},{"TRUTHEAR","TRUTHEAR"},{"MOONDROP","MOONDROP"},{"水月雨","MOONDROP"},
            {"EDIFIER","EDIFIER"},{"漫步者","EDIFIER"},{"SOUNDCORE","SOUNDCORE"},{"BOSE","BOSE"},{"JBL","JBL"},
            {"MARSHALL","MARSHALL"},{"SENNHEISER","SENNHEISER"},{"森海塞尔","SENNHEISER"},
            {"ULTIMATE EARS","ULTIMATE EARS"},{"HARMAN KARDON","HARMAN KARDON"},{"BANG & OLUFSEN","BANG & OLUFSEN"},
            {"JABRA","JABRA"},{"捷波朗","JABRA"},{"SHOKZ","SHOKZ"},{"韶音","SHOKZ"},{"BEATS","BEATS"},
            {"HUAWEI","HUAWEI"},{"华为","HUAWEI"},{"XIAOMI","XIAOMI"},{"小米","XIAOMI"},
            {"REDMI","REDMI"},{"红米","REDMI"},{"SAMSUNG","SAMSUNG"},{"三星","SAMSUNG"},
            {"CREATIVE","CREATIVE"},{"创新科技","CREATIVE"},{"PHILIPS","PHILIPS"},{"飞利浦","PHILIPS"},
            {"QCY","QCY"},{"SOUNDPEATS","SOUNDPEATS"},{"NOTHING","NOTHING"},{"OPPO","OPPO"},{"ONEPLUS","ONEPLUS"},
            {"NINTENDO","NINTENDO"},{"任天堂","NINTENDO"},{"8BITDO","8BITDO"},{"八位堂","8BITDO"},
            {"GAMESIR","GAMESIR"},{"盖世小鸡","GAMESIR"},{"FLYDIGI","FLYDIGI"},{"飞智","FLYDIGI"},
            {"BETOP","BETOP"},{"北通","BETOP"},{"GULIKIT","GULIKIT"},{"谷粒","GULIKIT"},{"EASYSMX","EASYSMX"}
        };
        private static readonly Regex NamedBrand=new Regex(@"(?<![A-Za-z0-9])(?:"+string.Join("|",
            Words.Keys.OrderByDescending(x=>x.Length).Select(Regex.Escape))+@")(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        private static readonly Regex SonyAudio=new Regex(@"^(?:SRS[- ]?[A-Z0-9]|XB\d|W[HF]-[A-Z0-9]|ULT (?:FIELD|WEAR))",RegexOptions.IgnoreCase);

        internal static int CatalogSize {get {return Words.Values.Concat(Usb.Values).Where(x=>x.Length>0).Distinct().Count();}}

        internal static string Identify(Device device)
        {
            string name=device.Name??"";
            if(LunaBattery.ModelName(device.Model)!=null) return "LUNAFURY";
            if(RogOmni.IsReceiver(device.Model)) return "ROG";
            if(Starts(name,"DualSense","DUALSHOCK")) return "PLAYSTATION";
            if(Starts(name,"AirPods","Magic Mouse","Magic Keyboard","Magic Trackpad")) return "APPLE";
            if(Starts(name,"MX Master","MX Anywhere","MX Keys","G PRO X SUPERLIGHT","G502","G305","G304")) return "LOGITECH";
            if(Starts(name,"DeathAdder","Basilisk","BlackWidow","Huntsman","Orochi","Viper V2","Viper V3","Barracuda","BlackShark")) return "RAZER";
            if(Starts(name,"Cloud III","Cloud II","Cloud Alpha","Cloud Flight","Cloud Stinger","Pulsefire Haste","Alloy Origins")) return "HYPERX";
            if(Starts(name,"ATK Blazing Sky","Blazing Sky")) return "ATK";
            if(Starts(name,"Galaxy Buds")) return "SAMSUNG";
            if(Starts(name,"FreeBuds")) return "HUAWEI";
            if(Starts(name,"WONDERBOOM","MEGABOOM")) return "ULTIMATE EARS";
            if(Starts(name,"Emberton","Middleton","Willen","Stockwell")) return "MARSHALL";
            if(device.Kind=="音频"&&SonyAudio.IsMatch(name)) return "SONY";
            var nameMatch=NamedBrand.Match(name);
            if(nameMatch.Success) return Words[nameMatch.Value];
            if(device.Model!=null&&device.Model.Length>=5&&device.Model[4]==':')
            {
                string vid=device.Model.Substring(0,4), vendor;
                if(vid=="054C") return device.Kind=="手柄"?"PLAYSTATION":"SONY";
                if(vid=="045E") return device.Kind=="手柄"?"XBOX":"MICROSOFT";
                if(Usb.TryGetValue(vid,out vendor)) return vendor;
            }
            var maker=NamedBrand.Match(device.Manufacturer??"");
            return maker.Success&&Words[maker.Value]!="MICROSOFT"?Words[maker.Value]:null;
        }

        private static bool Starts(string name,params string[] values)
        {
            return values.Any(value=>name.StartsWith(value,StringComparison.OrdinalIgnoreCase)&&
                (name.Length==value.Length||!char.IsLetterOrDigit(name[value.Length])));
        }
    }
}
