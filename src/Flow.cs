using System;using System.IO;using System.Linq;using System.Collections.Generic;using SessionProbe;
namespace CnDirect {
    public static class Flow {
        public static string Profiles(string root){string prior=Path.GetFullPath(Path.Combine(root,@"..\BattleLogin-验证工具\private-data\browser"));if(Directory.Exists(Path.Combine(prior,"CN-A"))||Directory.Exists(Path.Combine(prior,"CN-B")))return prior;return Path.Combine(root,@"private-data\browser");}
        public static bool Allowed(string value){
            Uri u;if(value==null||value.Length>=32767||value.Any(c=>char.IsControl(c)||c=='"')||!BrowserProfiles.Allowed(value)||!Uri.TryCreate(value,UriKind.Absolute,out u))return false;
            if(u.Host=="oauth.g.mkey.163.com")return true;
            // The selected official client can first open its own login endpoint.
            // Its opaque query is owned by Battle.net: do not assume app=app or edit it.
            return u.Host=="account.battlenet.com.cn"&&u.AbsolutePath.StartsWith("/login/",StringComparison.OrdinalIgnoreCase);
        }
        // Fixed labels only: never display a nonce, proof, user identifier or full URL.
        public static string RejectionHint(string value){
            try{Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u))return "请求格式未识别";
                if(u.Host=="account.battlenet.com.cn")return "战网网页入口格式不同";
                if(u.Host!="oauth.g.mkey.163.com")return "入口未识别";
                var fields=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var part in u.Query.TrimStart('?').Split('&')){var pair=part.Split(new[]{'='},2);string key=Uri.UnescapeDataString(pair[0].Replace("+"," "));if(fields.ContainsKey(key))return "网易请求有重复参数";fields.Add(key,pair.Length==2?Uri.UnescapeDataString(pair[1].Replace("+"," ")):"");}
                string from,client,callback;Uri r;string source=fields.TryGetValue("fromApp",out from)&&(from=="app"||from=="apph")?from:"未识别";
                bool knownClient=fields.TryGetValue("client_id",out client)&&client=="ld12";
                bool knownReturn=fields.TryGetValue("redirect_uri",out callback)&&Uri.TryCreate(callback,UriKind.Absolute,out r)&&r.Scheme=="https"&&r.IsDefaultPort&&r.UserInfo.Length==0&&r.Host=="account.battlenet.com.cn"&&r.AbsolutePath=="/login/signin/ntes"&&r.Query.Length==0&&r.Fragment.Length==0;
                return "网易入口；来源="+source+"；应用="+(knownClient?"已确认":"未识别")+"；返回地址="+(knownReturn?"已确认":"不同");
            }catch{return "请求格式未识别";}
        }
    }
}
