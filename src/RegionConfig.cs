using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace BrowserManager {
    // Records token boundaries so edits never reserialize login credentials or unrelated settings.
    internal sealed class JsonField {public string Key;public int Start;public JsonPart Value;}
    internal sealed class JsonPart {public int Start,End;public List<JsonField> Fields;}
    internal sealed class ConfigDocument {
        internal readonly string Text;internal readonly JsonPart Root;int cursor;readonly JavaScriptSerializer json=new JavaScriptSerializer();
        internal ConfigDocument(byte[] bytes){if(bytes==null||bytes.Length==0||bytes.Length>4*1024*1024)throw new InvalidDataException();Text=new UTF8Encoding(false,true).GetString(bytes);cursor=Text.Length>0&&Text[0]=='\uFEFF'?1:0;Root=Value(0);White();if(cursor!=Text.Length||Root.Fields==null)throw new InvalidDataException();}
        void White(){while(cursor<Text.Length&&" \r\n\t".IndexOf(Text[cursor])>=0)cursor++;}
        void Expect(char value){White();if(cursor>=Text.Length||Text[cursor++]!=value)throw new InvalidDataException();}
        string String(){White();int start=cursor;if(cursor>=Text.Length||Text[cursor++]!='"')throw new InvalidDataException();while(cursor<Text.Length){char c=Text[cursor++];if(c=='"')return json.Deserialize<string>(Text.Substring(start,cursor-start));if(c<' ')throw new InvalidDataException();if(c=='\\'){if(cursor>=Text.Length)throw new InvalidDataException();char escape=Text[cursor++];if(escape=='u'){if(cursor+4>Text.Length||!Text.Substring(cursor,4).All(x=>Uri.IsHexDigit(x)))throw new InvalidDataException();cursor+=4;}else if("\"\\/bfnrt".IndexOf(escape)<0)throw new InvalidDataException();}}throw new InvalidDataException();}
        JsonPart Value(int depth){if(depth>32)throw new InvalidDataException();White();int start=cursor;if(cursor>=Text.Length)throw new InvalidDataException();var part=new JsonPart{Start=start};char c=Text[cursor];
            if(c=='{'){cursor++;part.Fields=new List<JsonField>();White();if(cursor<Text.Length&&Text[cursor]=='}'){cursor++;}else while(true){White();int keyStart=cursor;string key=String();if(part.Fields.Any(f=>f.Key==key))throw new InvalidDataException();Expect(':');part.Fields.Add(new JsonField{Key=key,Start=keyStart,Value=Value(depth+1)});White();if(cursor>=Text.Length)throw new InvalidDataException();if(Text[cursor]=='}'){cursor++;break;}Expect(',');}}
            else if(c=='['){cursor++;White();if(cursor<Text.Length&&Text[cursor]==']'){cursor++;}else while(true){Value(depth+1);White();if(cursor>=Text.Length)throw new InvalidDataException();if(Text[cursor]==']'){cursor++;break;}Expect(',');}}
            else if(c=='"')String();
            else {while(cursor<Text.Length&&",]} \r\n\t".IndexOf(Text[cursor])<0)cursor++;string token=Text.Substring(start,cursor-start);if(token!="true"&&token!="false"&&token!="null"&&!System.Text.RegularExpressions.Regex.IsMatch(token,@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"))throw new InvalidDataException();}
            part.End=cursor;return part;
        }
        internal JsonPart Member(JsonPart parent,string key){if(parent==null||parent.Fields==null)throw new InvalidDataException();var field=parent.Fields.SingleOrDefault(f=>f.Key==key);return field==null?null:field.Value;}
        internal JsonPart Client(string profile){var parent=profile==null?Root:Member(Root,profile);return Member(parent,"Client");}
        internal string ReadString(JsonPart parent,string key){var value=Member(parent,key);if(value==null)return null;if(Text[value.Start]!='"')throw new InvalidDataException();return json.Deserialize<string>(Text.Substring(value.Start,value.End-value.Start));}
        internal byte[] Set(JsonPart parent,string key,string raw){if(parent==null||parent.Fields==null)throw new InvalidDataException();var value=Member(parent,key);int position=parent.Fields.Count>0?parent.Fields.Last().Value.End:parent.Start+1;return value==null?Replace(position,position,(parent.Fields.Count>0?",":"")+json.Serialize(key)+":"+raw):Replace(value.Start,value.End,raw);}
        internal byte[] Remove(JsonPart parent,string key){var item=parent.Fields.SingleOrDefault(f=>f.Key==key);if(item==null)return Bytes();int index=parent.Fields.IndexOf(item);if(parent.Fields.Count==1)return Replace(item.Start,item.Value.End,"");if(index<parent.Fields.Count-1)return Replace(item.Start,parent.Fields[index+1].Start,"");return Replace(parent.Fields[index-1].Value.End,item.Value.End,"");}
        internal byte[] Replace(int start,int end,string content){return new UTF8Encoding(false,true).GetBytes(Text.Substring(0,start)+content+Text.Substring(end));}
        internal byte[] Bytes(){return new UTF8Encoding(false,true).GetBytes(Text);}
    }
    public sealed class RegionEntry {
        public string Profile {get;set;}public bool HadSettings {get;set;}public string RegionsBefore {get;set;}public string LocalesBefore {get;set;}public string RegionsAfter {get;set;}public string LocalesAfter {get;set;}
    }
    public sealed class RegionRecord {public int Format {get;set;}public DateTime CreatedUtc {get;set;}public List<RegionEntry> Entries {get;set;}}
    public static class RegionConfig {
        static readonly string[] AllRegions={"CN","US","EU","KR"},AllLocales={"zhCN","enUS","zhTW"};
        static string Add(string original,string[] required){var words=(original??"").Split(';').Where(s=>s.Length>0).ToList();foreach(string item in required)if(!words.Any(s=>string.Equals(s,item,StringComparison.OrdinalIgnoreCase)))words.Add(item);return words.Count==0?"":string.Join(";",words);}
        static IEnumerable<string> Profiles(ConfigDocument doc){if(doc.Member(doc.Root,"Client")!=null)yield return null;foreach(var field in doc.Root.Fields)if(field.Key!="Client"&&field.Value.Fields!=null&&doc.Member(field.Value,"Client")!=null)yield return field.Key;}
        public static RegionRecord Plan(byte[] bytes){try{var doc=new ConfigDocument(bytes);var record=new RegionRecord{Format=1,CreatedUtc=DateTime.UtcNow,Entries=new List<RegionEntry>()};foreach(string profile in Profiles(doc)){var client=doc.Client(profile);if(client.Fields==null)throw new InvalidDataException();var settings=doc.Member(client,"LoginSettings");string regions=settings==null?null:doc.ReadString(settings,"AllowedRegions"),locales=settings==null?null:doc.ReadString(settings,"AllowedLocales");record.Entries.Add(new RegionEntry{Profile=profile,HadSettings=settings!=null,RegionsBefore=regions,LocalesBefore=locales,RegionsAfter=Add(regions,AllRegions),LocalesAfter=Add(locales,AllLocales)});}if(record.Entries.Count==0)throw new InvalidDataException();return record;}catch(Exception error){throw new InvalidOperationException("战网配置结构无法识别。本次未修改，请先正常打开官方战网，再退出后重试。",error);}}
        public static bool Needed(RegionRecord record){return record.Entries.Any(e=>e.RegionsBefore!=e.RegionsAfter||e.LocalesBefore!=e.LocalesAfter);}
        static void Validate(RegionRecord record){if(record==null||record.Format!=1||record.Entries==null||record.Entries.Count==0||record.Entries.Count>128||record.Entries.Select(e=>e.Profile??"\0").Distinct().Count()!=record.Entries.Count||record.Entries.Any(e=>e.RegionsAfter==null||e.LocalesAfter==null||(!e.HadSettings&&(e.RegionsBefore!=null||e.LocalesBefore!=null))))throw new InvalidDataException();}
        public static byte[] Apply(byte[] before,RegionRecord record){Validate(record);return Edit(before,record,false);}
        public static byte[] Undo(byte[] current,RegionRecord record){Validate(record);return Edit(current,record,true);}
        static byte[] Edit(byte[] input,RegionRecord record,bool undo){
            var initial=Plan(input);if(initial.Entries.Count!=record.Entries.Count||!initial.Entries.Select(e=>e.Profile).OrderBy(s=>s).SequenceEqual(record.Entries.Select(e=>e.Profile).OrderBy(s=>s)))throw new InvalidOperationException("战网配置层级已变化，未覆盖当前设置。请重新检查客户端。");
            foreach(var entry in record.Entries){var now=initial.Entries.Single(e=>e.Profile==entry.Profile);if(undo?(now.RegionsBefore!=entry.RegionsAfter||now.LocalesBefore!=entry.LocalesAfter):(now.HadSettings!=entry.HadSettings||now.RegionsBefore!=entry.RegionsBefore||now.LocalesBefore!=entry.LocalesBefore))throw new InvalidOperationException("区服入口设置已被其他操作更改，本次未覆盖。请重新检查客户端。");}
            byte[] bytes=(byte[])input.Clone();var json=new JavaScriptSerializer();
            foreach(var entry in record.Entries){var doc=new ConfigDocument(bytes);var client=doc.Client(entry.Profile);var settings=doc.Member(client,"LoginSettings");if(settings==null){bytes=doc.Set(client,"LoginSettings","{}");}
                foreach(string field in new[]{"AllowedRegions","AllowedLocales"}){doc=new ConfigDocument(bytes);settings=doc.Member(doc.Client(entry.Profile),"LoginSettings");string value=field=="AllowedRegions"?(undo?entry.RegionsBefore:entry.RegionsAfter):(undo?entry.LocalesBefore:entry.LocalesAfter);bytes=value==null?doc.Remove(settings,field):doc.Set(settings,field,json.Serialize(value));}
                if(undo&&!entry.HadSettings){doc=new ConfigDocument(bytes);client=doc.Client(entry.Profile);settings=doc.Member(client,"LoginSettings");if(settings.Fields.Count==0)bytes=doc.Remove(client,"LoginSettings");}
            }
            new ConfigDocument(bytes);return bytes;
        }
        public static string Describe(byte[] bytes){var plan=Plan(bytes);return Needed(plan)?"已识别 "+plan.Entries.Count+" 层战网设置。可以补齐中国、美洲、欧洲、亚洲的登录入口。":"配置已包含国服和外服入口。请在官方战网登录页的地区选项中选择；实际显示结果仍以客户端为准。";}
    }
}
