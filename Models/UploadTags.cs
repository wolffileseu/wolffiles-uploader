namespace WolffilesUploader.Models;

public static class UploadTags
{
    public static readonly Dictionary<string, string[]> Groups = new()
    {
        ["map_type"] = ["objective", "frag", "trickjump", "deathmatch", "ctf", "lms", "single_player"],
        ["style"]    = ["sniper", "panzer", "rifle", "smg", "cqb", "vehicle", "indoor", "outdoor"],
        ["size"]     = ["small", "medium", "large", "xl"],
        ["theme"]    = ["ww2", "desert", "snow", "urban", "forest", "beach", "night", "custom"],
        ["quality"]  = ["final", "beta", "alpha", "fun", "competitive", "tournament"],
    };

    public static readonly Dictionary<string, string> GroupLabels = new()
    {
        ["map_type"] = "MAP TYPE",
        ["style"]    = "STYLE",
        ["size"]     = "SIZE",
        ["theme"]    = "THEME",
        ["quality"]  = "QUALITY",
    };

    // Exposed as static properties for x:Bind in XAML DataTemplates
    public static string[] MapType => Groups["map_type"];
    public static string[] Style   => Groups["style"];
    public static string[] Size    => Groups["size"];
    public static string[] Theme   => Groups["theme"];
    public static string[] Quality => Groups["quality"];

    public static readonly HashSet<string> AllPredefined =
        Groups.Values.SelectMany(v => v).ToHashSet();
}
