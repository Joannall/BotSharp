using BotSharp.Abstraction.Files;
using BotSharp.Abstraction.Files.Models;
using BotSharp.Abstraction.Files.Utilities;
using BotSharp.Abstraction.Hooks;
using GenerativeAI;
using GenerativeAI.Core;
using GenerativeAI.Types;

namespace BotSharp.Plugin.GoogleAi.Providers.Chat;

public class ChatCompletionProvider : IChatCompletion
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ChatCompletionProvider> _logger;
    private List<string> renderedInstructions = [];

    private string _model;

    public string Provider => "google-ai";
    public string Model => _model;

    private GoogleAiSettings _settings;
    public ChatCompletionProvider(
        IServiceProvider services,
        GoogleAiSettings googleSettings,
        ILogger<ChatCompletionProvider> logger)
    {
        _settings = googleSettings;
        _services = services;
        _logger = logger;
    }

    public async Task<RoleDialogModel> GetChatCompletions(Agent agent, List<RoleDialogModel> conversations)
    {
        var contentHooks = _services.GetHooks<IContentGeneratingHook>(agent.Id);

        // Before chat completion hook
        foreach (var hook in contentHooks)
        {
            await hook.BeforeGenerating(agent, conversations);
        }

        var client = ProviderHelper.GetGeminiClient(Provider, _model, _services);
        var aiModel = client.CreateGenerativeModel(_model.ToModelId());
        var (prompt, request) = PrepareOptions(aiModel, agent, conversations);

        var response = await aiModel.GenerateContentAsync(request);
        var candidate = response.Candidates?.First();
        var part = candidate?.Content?.Parts?.FirstOrDefault();
        var text = part?.Text ?? string.Empty;

        RoleDialogModel responseMessage;
        if (response.GetFunction() != null)
        {
            responseMessage = new RoleDialogModel(AgentRole.Function, text)
            {
                CurrentAgentId = agent.Id,
                MessageId = conversations.LastOrDefault()?.MessageId ?? string.Empty,
                ToolCallId = part.FunctionCall.Name,
                FunctionName = part.FunctionCall.Name,
                FunctionArgs = part.FunctionCall.Args?.ToJsonString(),
                RenderedInstruction = string.Join("\r\n", renderedInstructions)
            };
        }
        else
        {
            responseMessage = new RoleDialogModel(AgentRole.Assistant, text)
            {
                CurrentAgentId = agent.Id,
                MessageId = conversations.LastOrDefault()?.MessageId ?? string.Empty,
                RenderedInstruction = string.Join("\r\n", renderedInstructions)
            };
        }

        // After chat completion hook
        foreach (var hook in contentHooks)
        {
            await hook.AfterGenerated(responseMessage, new TokenStatsModel
            {
                Prompt = prompt,
                Provider = Provider,
                Model = _model,
                TextInputTokens = response?.UsageMetadata?.PromptTokenCount ?? 0,
                TextOutputTokens = response?.UsageMetadata?.CandidatesTokenCount ?? 0
            });
        }

        return responseMessage;
    }

    public async Task<bool> GetChatCompletionsAsync(Agent agent, List<RoleDialogModel> conversations, Func<RoleDialogModel, Task> onMessageReceived, Func<RoleDialogModel, Task> onFunctionExecuting)
    {
        var hooks = _services.GetHooks<IContentGeneratingHook>(agent.Id);

        // Before chat completion hook
        foreach (var hook in hooks)
        {
            await hook.BeforeGenerating(agent, conversations);
        }

        var client = ProviderHelper.GetGeminiClient(Provider, _model, _services);
        var chatClient = client.CreateGenerativeModel(_model.ToModelId());
        var (prompt, messages) = PrepareOptions(chatClient, agent, conversations);

        var response = await chatClient.GenerateContentAsync(messages);

        var candidate = response.Candidates?.First();
        var part = candidate?.Content?.Parts?.FirstOrDefault();
        var text = part?.Text ?? string.Empty;

        var msg = new RoleDialogModel(AgentRole.Assistant, text)
        {
            CurrentAgentId = agent.Id,
            RenderedInstruction = string.Join("\r\n", renderedInstructions)
        };

        // After chat completion hook
        foreach (var hook in hooks)
        {
            await hook.AfterGenerated(msg, new TokenStatsModel
            {
                Prompt = prompt,
                Provider = Provider,
                Model = _model,
                TextInputTokens = response?.UsageMetadata?.PromptTokenCount ?? 0,
                TextOutputTokens = response?.UsageMetadata?.CandidatesTokenCount ?? 0
            });
        }

        if (response.GetFunction() != null)
        {
            var toolCall = response.GetFunction();
            _logger.LogInformation($"[{agent.Name}]: {toolCall?.Name}({toolCall?.Args?.ToJsonString()})");

            var funcContextIn = new RoleDialogModel(AgentRole.Function, text)
            {
                CurrentAgentId = agent.Id,
                MessageId = conversations.LastOrDefault()?.MessageId ?? string.Empty,
                ToolCallId = toolCall?.Id,
                FunctionName = toolCall?.Name,
                FunctionArgs = toolCall?.Args?.ToJsonString(),
                RenderedInstruction = string.Join("\r\n", renderedInstructions)
            };

            // Somethings LLM will generate a function name with agent name.
            if (!string.IsNullOrEmpty(funcContextIn.FunctionName))
            {
                funcContextIn.FunctionName = funcContextIn.FunctionName.Split('.').Last();
            }

            // Execute functions
            await onFunctionExecuting(funcContextIn);
        }
        else
        {
            // Text response received
            await onMessageReceived(msg);
        }

        return true;
    }

    public Task<RoleDialogModel> GetChatCompletionsStreamingAsync(Agent agent, List<RoleDialogModel> conversations)
    {
        throw new NotImplementedException();
    }

    public void SetModelName(string model)
    {
        _model = model;
    }

    private (string, GenerateContentRequest) PrepareOptions(GenerativeModel aiModel, Agent agent, List<RoleDialogModel> conversations)
    {
        var agentService = _services.GetRequiredService<IAgentService>();
        var googleSettings = _services.GetRequiredService<GoogleAiSettings>();
        var fileStorage = _services.GetRequiredService<IFileStorageService>();
        var settingsService = _services.GetRequiredService<ILlmProviderService>();
        var settings = settingsService.GetSetting(Provider, _model);
        var allowMultiModal = settings != null && settings.MultiModal;
        renderedInstructions = [];

        // Add settings
        aiModel.UseGoogleSearch = googleSettings.Gemini.UseGoogleSearch;
        aiModel.UseGrounding = googleSettings.Gemini.UseGrounding;

        aiModel.FunctionCallingBehaviour = new FunctionCallingBehaviour()
        {
            AutoCallFunction = false
        };

        // Assemble messages
        var contents = new List<Content>();
        var tools = new List<Tool>();
        var funcDeclarations = new List<FunctionDeclaration>();
        var systemPrompts = new List<string>();
        var funcPrompts = new List<string>();

        // Prepare instruction and functions
        var renderData = agentService.CollectRenderData(agent);
        var (instruction, functions) = agentService.PrepareInstructionAndFunctions(agent, renderData);
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            renderedInstructions.Add(instruction);
            systemPrompts.Add(instruction);
        }

        foreach (var function in functions)
        {
            if (!agentService.RenderFunction(agent, function, renderData))
            {
                continue;
            }

            var def = agentService.RenderFunctionProperty(agent, function, renderData);
            var props = JsonSerializer.Serialize(def?.Properties);
            var parameters = !string.IsNullOrWhiteSpace(props) && props != "{}" ? new Schema()
            {
                Type = "object",
                Properties = JsonSerializer.Deserialize<Dictionary<string, Schema>>(props),
                Required = def?.Required ?? []
            } : null;

            funcDeclarations.Add(new FunctionDeclaration
            {
                Name = function.Name,
                Description = function.Description,
                Parameters = parameters
            });

            funcPrompts.Add($"{function.Name}: {function.Description} {def}");
        }

        if (!funcDeclarations.IsNullOrEmpty())
        {
            tools.Add(new Tool { FunctionDeclarations = funcDeclarations });
        }

        var convPrompts = new List<string>();
        foreach (var message in conversations)
        {
            if (message.Role == AgentRole.Function)
            {
                contents.Add(new Content([
                    new Part()
                    {
                        FunctionCall = new FunctionCall
                        {
                            Id = message.ToolCallId,
                            Name = message.FunctionName,
                            Args = JsonNode.Parse(message.FunctionArgs ?? "{}")
                        }
                    }
                ], AgentRole.Model));

                contents.Add(new Content([
                    new Part()
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Id = message.ToolCallId,
                            Name = message.FunctionName,
                            Response = new JsonObject()
                            {
                                ["result"] = message.LlmContent ?? string.Empty
                            }
                        }
                    }
                ], AgentRole.Function));

                convPrompts.Add($"{AgentRole.Assistant}: Call function {message.FunctionName}({message.FunctionArgs}) => {message.LlmContent}");
            }
            else if (message.Role == AgentRole.User)
            {
                var text = message.LlmContent;
                var contentParts = new List<Part> { new() { Text = text } };

                if (allowMultiModal && !message.Files.IsNullOrEmpty())
                {
                    CollectMessageContentParts(contentParts, message.Files);
                }
                contents.Add(new Content(contentParts, AgentRole.User));
                convPrompts.Add($"{AgentRole.User}: {text}");
            }
            else if (message.Role == AgentRole.Assistant)
            {
                var text = message.LlmContent;
                var contentParts = new List<Part> { new() { Text = text } };

                if (allowMultiModal && !message.Files.IsNullOrEmpty())
                {
                    CollectMessageContentParts(contentParts, message.Files);
                }

                contents.Add(new Content(contentParts, AgentRole.Model));
                convPrompts.Add($"{AgentRole.Assistant}: {text}");
            }
        }

        var state = _services.GetRequiredService<IConversationStateService>();
        var temperature = float.Parse(state.GetState("temperature", "0.0"));
        var maxTokens = int.TryParse(state.GetState("max_tokens"), out var tokens)
                            ? tokens
                            : agent.LlmConfig?.MaxOutputTokens ?? LlmConstant.DEFAULT_MAX_OUTPUT_TOKEN;
        var request = new GenerateContentRequest
        {
            SystemInstruction = !systemPrompts.IsNullOrEmpty() ? new Content(systemPrompts[0], AgentRole.System) : null,
            Contents = contents,
            Tools = tools,
            GenerationConfig = new()
            {
                Temperature = temperature,
                MaxOutputTokens = maxTokens
            }
        };

        var prompt = GetPrompt(systemPrompts, funcPrompts, convPrompts);
        return (prompt, request);
    }

    private void CollectMessageContentParts(List<Part> contentParts, List<BotSharpFile> files)
    {
        var fileStorage = _services.GetRequiredService<IFileStorageService>();

        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(file.FileData))
            {
                var (contentType, binary) = FileUtility.GetFileInfoFromData(file.FileData);
                contentParts.Add(new Part()
                {
                    InlineData = new()
                    {
                        MimeType = contentType.IfNullOrEmptyAs(file.ContentType),
                        Data = Convert.ToBase64String(binary.ToArray())
                    }
                });
            }
            else if (!string.IsNullOrEmpty(file.FileStorageUrl))
            {
                var contentType = FileUtility.GetFileContentType(file.FileStorageUrl);
                var binary = fileStorage.GetFileBytes(file.FileStorageUrl);
                contentParts.Add(new Part()
                {
                    InlineData = new()
                    {
                        MimeType = contentType.IfNullOrEmptyAs(file.ContentType),
                        Data = Convert.ToBase64String(binary.ToArray())
                    }
                });
            }
            else if (!string.IsNullOrEmpty(file.FileUrl))
            {
                contentParts.Add(new Part()
                {
                    FileData = new()
                    {
                        FileUri = file.FileUrl
                    }
                });
            }
        }
    }

    private string GetPrompt(IEnumerable<string> systemPrompts, IEnumerable<string> funcPrompts, IEnumerable<string> convPrompts)
    {
        var prompt = string.Empty;

        prompt = string.Join("\r\n\r\n", systemPrompts);

        if (!funcPrompts.IsNullOrEmpty())
        {
            prompt += "\r\n\r\n[FUNCTIONS]\r\n";
            prompt += string.Join("\r\n", funcPrompts);
        }

        if (!convPrompts.IsNullOrEmpty())
        {
            prompt += "\r\n\r\n[CONVERSATION]\r\n";
            prompt += string.Join("\r\n", convPrompts);
        }

        return prompt;
    }
}
