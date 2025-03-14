namespace BotSharp.Plugin.MongoStorage.Models;

[BsonIgnoreExtraElements(Inherited = true)]
public class BreakpointMongoElement
{
    public string? MessageId { get; set; }
    public DateTime Breakpoint { get; set; }
    public DateTime CreatedTime { get; set; }
    public string? Reason { get; set; }
}
