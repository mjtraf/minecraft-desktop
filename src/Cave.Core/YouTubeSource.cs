using System.Text.RegularExpressions;
namespace Cave.Core;
public static class YouTubeSource
{
    public static string? Normalize(string input)
    {
        input=input.Trim();if(Regex.IsMatch(input,"^[A-Za-z0-9_-]{11}$"))return "https://www.youtube.com/watch?v="+input;
        if(!Uri.TryCreate(input,UriKind.Absolute,out var uri) || uri.Scheme!="https")return null;
        string host=uri.Host.ToLowerInvariant();if(host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be"))return null;
        var query=uri.Query.TrimStart('?').Split('&').Select(s=>s.Split('=',2)).Where(s=>s.Length==2).GroupBy(s=>s[0],StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First()[1],StringComparer.Ordinal);
        string id=host=="youtu.be"?uri.AbsolutePath.Trim('/') : uri.AbsolutePath.StartsWith("/shorts/") || uri.AbsolutePath.StartsWith("/embed/")?uri.Segments.Last().Trim('/'):query.GetValueOrDefault("v","");
        if(!Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$"))return null;
        string result="https://www.youtube.com/watch?v="+id;
        if(query.TryGetValue("t",out var t) && Regex.IsMatch(t,"^[0-9hms]{1,15}$"))result+="&t="+t;
        return result;
    }
}
