using BotSharp.Abstraction.Agents.Models;

namespace BotSharp.Plugin.MongoStorage.Models;

[BsonIgnoreExtraElements(Inherited = true)]
public class AgentMCPToolMongoElement
{
    public string Name { get; set; }
    public string ServerId { get; set; }
    public bool Disabled { get; set; }
    public List<McpFunctionMongoElement> Functions { get; set; } = [];

    public static AgentMCPToolMongoElement ToMongoElement(MCPTool tool)
    {
        return new AgentMCPToolMongoElement
        {
            Name = tool.Name,
            ServerId = tool.ServerId,
            Disabled = tool.Disabled,
            Functions = tool.Functions?.Select(x => new McpFunctionMongoElement(x.Name))?.ToList() ?? [],
        };
    }

    public static MCPTool ToDomainElement(AgentMCPToolMongoElement tool)
    {
        return new MCPTool
        {
            Name = tool.Name,
            ServerId = tool.ServerId,
            Disabled = tool.Disabled,
            Functions = tool.Functions?.Select(x => new MCPFunction(x.Name))?.ToList() ?? [],
        };
    }
}

[BsonIgnoreExtraElements(Inherited = true)]
public class McpFunctionMongoElement
{
    public string Name { get; set; }

    public McpFunctionMongoElement()
    {

    }

    public McpFunctionMongoElement(string name)
    {
        Name = name;
    }
}
