using System.Text.Json.Serialization;

namespace AngelDungeonTips;

public sealed class DungeonCatalog
{
    public string Version { get; set; } = "1";
    public List<DungeonGuide> Dungeons { get; set; } = new();
}

public sealed class DungeonGuide
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public List<DungeonStage> Stages { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class DungeonStage
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class AppSettings
{
    public int Width { get; set; } = 360;
    public int Height { get; set; } = 220;
    /// <summary>Background panel alpha only (0=invisible panel, 100=solid). Does not fade text.</summary>
    public int OpacityPercent { get; set; } = 70;
    /// <summary>Tip text alpha only (0–100).</summary>
    public int TextOpacityPercent { get; set; } = 100;
    public string? LastDungeonId { get; set; }
    public string? GameWindowTitle { get; set; }
    public int PosX { get; set; } = -1;
    public int PosY { get; set; } = -1;
    public int RelX { get; set; } = 16;
    public int RelY { get; set; } = 16;
    public bool FollowGameWindow { get; set; } = true;
    public bool ClickThrough { get; set; } = true;

    [JsonIgnore]
    public bool HasSavedPosition => PosX >= 0 && PosY >= 0;
}
