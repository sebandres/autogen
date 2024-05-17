using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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

namespace AutoGen.AWS;

public class BedrockAgent : IStreamingAgent
{
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

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        //        var chatHistory = string.Join(Environment.NewLine, messages.Select(m => $"{m.From ?? m.GetRole()!.Value.ToString()}:{((TextMessage)m).Content}"));
        var chatHistory = string.Join(Environment.NewLine, messages.Select(m => $"{((TextMessage)m).Content}"));

        return await GenerateReplyAsync(chatHistory, options, cancellationToken);
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
            return new TextMessage(Role.Assistant, responseBody, this.Name);
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
