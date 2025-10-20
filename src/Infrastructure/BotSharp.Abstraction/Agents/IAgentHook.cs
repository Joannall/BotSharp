using BotSharp.Abstraction.Functions.Models;
using BotSharp.Abstraction.Hooks;
using System.Collections.Concurrent;

namespace BotSharp.Abstraction.Agents;

public interface IAgentHook : IHookBase
{
    Agent Agent { get; }
    void SetAgent(Agent agent);

    /// <summary>
    /// Triggered when agent is loading.
    /// Return different agent for redirection purpose.
    /// </summary>
    /// <param name="id">Agent Id</param>
    /// <returns></returns>
    bool OnAgentLoading(ref string id);

    bool OnInstructionLoaded(string template, ConcurrentDictionary<string, object> dict);

    bool OnFunctionsLoaded(List<FunctionDef> functions);

    bool OnSamplesLoaded(List<string> samples);

    void OnAgentUtilityLoaded(Agent agent);

    void OnAgentMcpToolLoaded(Agent agent);

    /// <summary>
    /// Triggered when agent is loaded completely.
    /// </summary>
    /// <param name="agent"></param>
    /// <returns></returns>
    void OnAgentLoaded(Agent agent);
}
