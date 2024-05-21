using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.EventStreams;
using Amazon.Util;
using AutoGen.AWS.Agent;
using AutoGen.Core;
using Json.Schema;
using Json.Schema.Generation;
using static AutoGen.AWS.BedrockAgent;

namespace AutoGen.AWS;

public static class FunctionContractExtension
{
    /// <summary>
    /// Convert a <see cref="FunctionContract"/> to a <see cref="FunctionDefinition"/> that can be used in funciton call.
    /// </summary>
    /// <param name="functionContract">function contract</param>
    /// <returns><see cref="FunctionDefinition"/></returns>
    public static FunctionDefinition ToLlama3FunctionDefinition(this FunctionContract functionContract)
    {
        var functionDefinition = new FunctionDefinition(functionContract.Name ?? throw new Exception("Function name cannot be null"), functionContract.Description ?? throw new Exception("Function description cannot be null"));
        var requiredParameterNames = new List<string>();
        var propertiesSchemas = new Dictionary<string, JsonSchema>();
        var propertySchemaBuilder = new JsonSchemaBuilder().Type(SchemaValueType.Object);
        foreach (var param in functionContract.Parameters ?? [])
        {
            if (param.Name is null)
            {
                throw new InvalidOperationException("Parameter name cannot be null");
            }

            var schemaBuilder = new JsonSchemaBuilder().FromType(param.ParameterType ?? throw new ArgumentNullException(nameof(param.ParameterType)));
            if (param.Description != null)
            {
                schemaBuilder = schemaBuilder.Description(param.Description);
            }

            if (param.IsRequired)
            {
                requiredParameterNames.Add(param.Name);
            }

            var schema = schemaBuilder.Build();
            propertiesSchemas[param.Name] = schema;

        }
        propertySchemaBuilder = propertySchemaBuilder.Properties(propertiesSchemas);
        propertySchemaBuilder = propertySchemaBuilder.Required(requiredParameterNames);

        var option = new System.Text.Json.JsonSerializerOptions()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        functionDefinition.Parameters = propertySchemaBuilder.Build();

        return functionDefinition;
    }
}

public class BedrockAgent : IStreamingAgent
{
    public class FunctionDefinition
    {
        public FunctionDefinition(string name, string description, JsonSchema? parameters = default)
        {
            Name = name;
            Description = description;
            Parameters = parameters;
        }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public JsonSchema? Parameters { get; set; }
    }

    public class FunctionCall
    {
        public FunctionCall(string name, string arguments)
        {
            this.Name = name;
            this.Arguments = arguments;
        }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; }
    }

    private readonly string _systemMessage;
    private readonly string _modelId;
    private readonly IAmazonBedrockRuntime _client;
    private readonly JsonObject _config;

    public BedrockAgent(
        string name,
        string systemMessage,
        float temperature,
        BedrockConfig config)
    {
        Name = name;
        _systemMessage = systemMessage;
        _modelId = config.ModelId;
        _client = new AmazonBedrockRuntimeClient(new EnvironmentVariablesAWSCredentials(), Amazon.RegionEndpoint.USEast1);
        //_client = client;

        _config = new JsonObject()
             {
                 { "temperature", temperature },
             };
    }

    public string Name { get; }

    private string BuildLlama3Prompt(IEnumerable<IMessage> chatHistory, FunctionContract[]? functions)
    {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        //var prompt = string.Join(Environment.NewLine, chatHistory.Where(x => x.Role != AuthorRole.System).Select(m => $"{m.Metadata?["AuthorName"] ?? "user"}:{m.Content}"));
        var prompt = string.Join(Environment.NewLine, chatHistory
            .Where(x => ((x is TextMessage tmsg) && tmsg != null && tmsg.Role != Role.System) || (x is AggregateMessage<ToolCallMessage, ToolCallResultMessage>))
            .Select(m => $"<|start_header_id|>{m.From ?? "user"}<|end_header_id|>" +
            $"{m switch
            {
                TextMessage tm => tm.Content,
                AggregateMessage<ToolCallMessage, ToolCallResultMessage> tm => $"Executed tool to {tm.Message1.ToolCalls.First().FunctionName} with a result of: {tm.Message2.ToolCalls.First().Result}",
                _ => throw new ArgumentException("Invalid message type")
            }}<|eot_id|>"));
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        var fullPrompt = "<|begin_of_text|><|start_header_id|>system<|end_header_id|>" +
                Environment.NewLine +
                ((TextMessage)chatHistory.First(x => ((TextMessage)x).Role == Role.System)).Content +
                Environment.NewLine +
                prompt +
                "<|start_header_id|>assistant<|end_header_id|>";

        // Provide all functions from the kernel.
        if (functions != null && functions.Length > 0)
        {
            var exampleFunctionCall = new FunctionCall("FunctionToCall", @"{""ParameterName"": ""ParameterValue""}");
            var exampleFunctionCallSerialized = JsonSerializer.Serialize(exampleFunctionCall);

            StringBuilder functionsPrompt = new StringBuilder();

            //options.ToolChoice = ChatCompletionsToolChoice.Auto;
            for (int i = 0; i < functions.Length; i++)
            {
                var functionDefinition = functions[i].ToLlama3FunctionDefinition();

                if (functionDefinition == null)
                    continue;

                var serializedFunction = JsonSerializer.Serialize(functionDefinition);

                functionsPrompt.AppendLine(serializedFunction);
            }

            fullPrompt =
                "<|begin_of_text|><|start_header_id|>system<|end_header_id|>" +
                Environment.NewLine +
                ((TextMessage)chatHistory.First(x => ((TextMessage)x).Role == Role.System)).Content +
                Environment.NewLine +
                "If you can, use one of the available functions listed in the <functions> tag. Each function is a JSON payload with a Name and a Description which indicates when to use the tool." +
                Environment.NewLine +
                "<functions>" +
                Environment.NewLine +
                functionsPrompt.ToString() +
                "</functions>" +
                Environment.NewLine +
                "If you need to use use one of the functions then wrap it into a Markdown code block named 'function' only reply with the same JSON payload of the function." +
                Environment.NewLine +
                "This is an example of a function code block:" +
                Environment.NewLine +
    @$"``` function
{{""name"":""FunctionToCall"",""arguments"": ""{{\""ParameterName\"": \""ParameterValue\""}}""}}
```" +
                "<|eot_id|>" +
                Environment.NewLine +
                prompt +
                "<|eot_id|><|start_header_id|>assistant<|end_header_id|>";
        }

        return fullPrompt;
    }

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        //        var chatHistory = string.Join(Environment.NewLine, messages.Select(m => $"{m.From ?? m.GetRole()!.Value.ToString()}:{((TextMessage)m).Content}"));
        var chatHistory = BuildLlama3Prompt(messages, options?.Functions);// string.Join(Environment.NewLine, messages.Select(m => $"{((TextMessage)m).Content}"));

        return await GenerateReplyAsync(chatHistory, options, cancellationToken);
    }

    private ToolCallMessage[] ExtractFunctionCalls(string message, FunctionContract[]? functions)
    {
        var response = new List<ToolCallMessage>();
        string pattern = @"```[^`]*\n([\s\S]*?)\n```";

        // Create a regex object with the pattern
        Regex regex = new Regex(pattern);

        // Find matches in the input string
        MatchCollection matches = regex.Matches(message);

        // Iterate through each match and extract the content
        foreach (Match match in matches)
        {
            if (match.Success)
            {
                try
                {
                    var function = JsonSerializer.Deserialize<FunctionCall>(match.Groups[1].Value);

                    if (function != null)
                    {
                        foreach (var plugin in functions!)
                        {
                            if (plugin.Name == function.Name)
                            {
                                response.Add(new ToolCallMessage(function.Name, function.Arguments, this.Name));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        return response.ToArray();
    }

    private async Task<IMessage> ProcessLlama3Response(string response, FunctionContract[]? functions)
    {
        if (functions != null)
        {
            var functionsToBeCalled = ExtractFunctionCalls(response, functions);
            if (functionsToBeCalled.Any())
                return functionsToBeCalled.FirstOrDefault();
        }

        return new TextMessage(Role.Assistant, response, this.Name);
    }

    public async Task<IMessage> GenerateReplyAsync(string enclosedPrompt, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        //string enclosedPrompt = "Human:" + chatHistory + "\n\nAssistant:";

        //AmazonBedrockRuntimeClient client = new(RegionEndpoint.USEast1);

        var jsonConfig = JsonNode.Parse(_config.ToJsonString())!.AsObject();
        jsonConfig.Add("prompt", enclosedPrompt);

        string payload = jsonConfig.ToJsonString();

        InvokeModelResponse? response = null;

        try
        {
            response = await _client.InvokeModelAsync(new InvokeModelRequest()
            {
                ModelId = _modelId,
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
                ContentType = "application/json",
                Accept = "application/json"
            }, cancellationToken);
        }
        catch (AmazonBedrockRuntimeException e)
        {
            Console.WriteLine(e.Message);
            throw;
        }

        if (response is not null && response.HttpStatusCode == System.Net.HttpStatusCode.OK)
        {
            var result = await JsonNode.ParseAsync(response.Body);

            var responseBody = result?["generation"]?.GetValue<string>() ?? "";

            return await ProcessLlama3Response(responseBody, options?.Functions);
        }
        else if (response is not null)
        {
            throw new Exception("InvokeModelAsync failed with status code " + response.HttpStatusCode);
        }

        throw new Exception("response is null");
    }

    public IAsyncEnumerable<IStreamingMessage> GenerateStreamingReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = string.Join(Environment.NewLine, messages.Select(m => m switch
        {
            TextMessageUpdate tmu => $"{tmu.From}: {tmu.Content}",
            TextMessage tm => $"{tm.From}: {tm.Content}",
            _ => throw new ArgumentException("Invalid message type")
        }));

        return GenerateStreamingReplyAsync(prompt, options, cancellationToken);
    }

    public async IAsyncEnumerable<IStreamingMessage> GenerateStreamingReplyAsync(string enclosedPrompt, GenerateReplyOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {

        //        var prompt = string.Join(Environment.NewLine, messages.Select(m => $"{m.From}: {((TextMessage)m).Content}"));

        // string claudeModelId = "anthropic.claude-v2";

        // Claude requires you to enclose the prompt as follows:
        //string enclosedPrompt = "Human: " + prompt + "\n\nAssistant:";

        //AmazonBedrockRuntimeClient client = new(RegionEndpoint.USEast1);

        var jsonConfig = JsonNode.Parse(_config.ToJsonString())!.AsObject();
        jsonConfig.Add("prompt", enclosedPrompt);
        string payload = jsonConfig.ToJsonString();

        InvokeModelWithResponseStreamResponse? response = null;

        try
        {
            response = await _client.InvokeModelWithResponseStreamAsync(new InvokeModelWithResponseStreamRequest()
            {
                ModelId = _modelId,
                Body = AWSSDKUtils.GenerateMemoryStreamFromString(payload),
                ContentType = "application/json",
                Accept = "application/json"
            });
        }
        catch (AmazonBedrockRuntimeException e)
        {
            Console.WriteLine(e.Message);
        }

        if (response is not null && response.HttpStatusCode == System.Net.HttpStatusCode.OK)
        {
            // create a buffer to write the event in to move from a push mode to a pull mode
            Channel<string> buffer = Channel.CreateUnbounded<string>();
            bool isStreaming = true;

            response.Body.ChunkReceived += BodyOnChunkReceived;
            response.Body.StartProcessing();

            while ((!cancellationToken.IsCancellationRequested && isStreaming) || (!cancellationToken.IsCancellationRequested && buffer.Reader.Count > 0))
            {
                // pull the completion from the buffer and add it to the IAsyncEnumerable collection
                var textResponse = new TextMessageUpdate(Role.Assistant, await buffer.Reader.ReadAsync(cancellationToken), this.Name);
                yield return textResponse;
            }
            response.Body.ChunkReceived -= BodyOnChunkReceived;

            yield break;

            // handle the ChunkReceived events
            async void BodyOnChunkReceived(object? sender, EventStreamEventReceivedArgs<PayloadPart> e)
            {
                var streamResponse = JsonSerializer.Deserialize<JsonObject>(e.EventStreamEvent.Bytes) ?? throw new NullReferenceException($"Unable to deserialize {nameof(e.EventStreamEvent.Bytes)}");

                if (streamResponse["stop_reason"]?.GetValue<string?>() != null)
                {
                    isStreaming = false;
                }

                // write the received completion chunk into the buffer
                await buffer.Writer.WriteAsync(streamResponse["generation"]?.GetValue<string>() ?? "", cancellationToken);
            }
        }
        else if (response is not null)
        {
            Console.WriteLine("InvokeModelAsync failed with status code " + response.HttpStatusCode);
        }

        yield break;
    }
}
