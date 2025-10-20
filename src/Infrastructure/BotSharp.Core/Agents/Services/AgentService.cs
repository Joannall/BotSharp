using BotSharp.Abstraction.Options;
using BotSharp.Abstraction.Repositories.Settings;
using System.IO;

namespace BotSharp.Core.Agents.Services;

public partial class AgentService : IAgentService
{
    private readonly IServiceProvider _services;
    private readonly IBotSharpRepository _db;
    private readonly ILogger _logger;
    private readonly AgentSettings _agentSettings;
    private readonly BotSharpOptions _options;

    public AgentService(IServiceProvider services,
        IBotSharpRepository db,
        ILogger<AgentService> logger, 
        AgentSettings agentSettings,
        BotSharpOptions options)
    {
        _services = services;
        _db = db;
        _logger = logger;
        _agentSettings = agentSettings;
        _options = options;
    }

    public string GetDataDir()
    {
        var dbSettings = _services.GetRequiredService<BotSharpDatabaseSettings>();
        return Path.Combine(dbSettings.FileRepository);
    }

    public string GetAgentDataDir(string agentId)
    {
        var dbSettings = _services.GetRequiredService<BotSharpDatabaseSettings>();
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbSettings.FileRepository, _agentSettings.DataDir, agentId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public async Task<List<UserAgent>> GetUserAgents(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return [];
        }

        var userAgents = _db.GetUserAgents(userId);
        return userAgents;
    }
}
