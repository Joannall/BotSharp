using BotSharp.Abstraction.Infrastructures;
using BotSharp.Abstraction.Statistics.Settings;

namespace BotSharp.Core.Statistics.Services;

public class BotSharpStatsService : IBotSharpStatsService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BotSharpStatsService> _logger;
    private readonly StatisticsSettings _settings;

    private const int TIMEOUT_SECONDS = 5;

    public BotSharpStatsService(
        IServiceProvider services,
        ILogger<BotSharpStatsService> logger,
        StatisticsSettings settings)
    {
        _services = services;
        _logger = logger;
        _settings = settings;
    }


    public bool UpdateStats(string resourceKey, BotSharpStatsInput input)
    {
        try
        {
            if (!_settings.Enabled
                || string.IsNullOrEmpty(resourceKey)
                || input == null
                || string.IsNullOrEmpty(input.Category)
                || string.IsNullOrEmpty(input.Group))
            {
                return false;
            }
          
            var locker = _services.GetRequiredService<IDistributedLocker>();
            var res = locker.Lock(resourceKey, () =>
            {
                var db = _services.GetRequiredService<IBotSharpRepository>();
                var body = db.GetGlobalStats(input.Category, input.Group, input.RecordTime);
                if (body == null)
                {
                    var stats = new BotSharpStats
                    {
                        Category = input.Category,
                        Group = input.Group,
                        RecordTime = input.RecordTime,
                        Data = input.Data.ToDictionary(x => x.Key, x => x.Value)
                    };
                    db.SaveGlobalStats(stats);
                    return;
                }

                foreach (var item in input.Data)
                {
                    var curValue = item.Value;
                    if (body.Data.TryGetValue(item.Key, out var preValue))
                    {
                        switch (item.Operation)
                        {
                            case StatsOperation.Add:
                                preValue += curValue;
                                break;
                            case StatsOperation.Subtract:
                                preValue -= curValue;
                                break;
                            case StatsOperation.Reset:
                                preValue = 0;
                                break;
                        }
                        body.Data[item.Key] = preValue;
                    }
                    else
                    {
                        body.Data[item.Key] = curValue;
                    }
                }

                db.SaveGlobalStats(body);
            }, TIMEOUT_SECONDS);

            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error when updating global stats {input.Category}-{input.Group}. {ex.Message}\r\n{ex.InnerException}");
            return false;
        }
    }
}
