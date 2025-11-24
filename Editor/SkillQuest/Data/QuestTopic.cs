using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using T3.Serialization;

namespace T3.Editor.SkillQuest.Data;

[SuppressMessage("ReSharper", "MemberCanBeInternal")]
public sealed class QuestTopic
{
    // TODO: Color, style, etc. 

    public Guid Id = Guid.Empty;
    
    public string Title = string.Empty;
    public string Description = string.Empty;
    public List<QuestLevel> Levels = [];
    public Vector2 MapCoordinate;
    public Guid ZoneId;
    
    public List<Guid> UnlocksTopics = [];

    /// <summary>
    /// For linking to package levels
    /// </summary>
    public string Namespace;

    /// <summary>
    /// For formatting map
    /// </summary>
    public Type Type;

    public Statuses Status;

    [JsonConverter(typeof(SafeEnumConverter<Requirements>))]
    public Requirements Requirement = Requirements.None;

    [JsonIgnore]
    public List<SkillProgression.LevelResult> ResultsForTopic = [];
    
    public enum Requirements
    {
        None,
        IsValidStartPoint,
        AnyInputPath,
        AllInputPaths,
    }

    public enum Statuses
    {
        None,
        Locked,
        Unlocked,
        Completed,
    }

    [JsonIgnore]
    internal HexCanvas.Cell Cell
    {
        get => new((int)MapCoordinate.X, (int)MapCoordinate.Y);
        set => MapCoordinate = new Vector2(value.X, value.Y);
    }
}