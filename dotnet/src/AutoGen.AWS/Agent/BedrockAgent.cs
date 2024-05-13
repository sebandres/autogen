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
using Amazon.Runtime.EventStreams;
using Amazon.Util;
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
        string modelId,
        IAmazonBedrockRuntime client)
    {
        Name = name;
        _systemMessage = systemMessage;
        _modelId = modelId;
        _client = client;

        _config = new JsonObject()
             {
                 { "max_tokens_to_sample", 200 },
                 { "temperature", 0.5 },
                 { "stop_sequences", new JsonArray("\n\nHuman:") }
             };
    }

    public string Name { get; }

    public Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<IStreamingMessage> GenerateStreamingReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatHistory = string.Join(Environment.NewLine, messages.Select(m => $"{m.From}: {((TextMessage)m).Content}"));

        yield return new MessageEnvelope<IAsyncEnumerable<string>>(InvokeClaudeWithResponseStreamAsync(chatHistory, cancellationToken), from: this.Name);
    }

    /// <summary>
    /// Asynchronously invokes the Anthropic Claude 2 model to run an inference based on the provided input and process the response stream.
    /// </summary>
    /// <param name="prompt">The prompt that you want Claude to complete.</param>
    /// <returns>The inference response from the model</returns>
    /// <remarks>
    /// The different model providers have individual request and response formats.
    /// For the format, ranges, and default values for Anthropic Claude, refer to:
    ///     https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-claude.html
    /// </remarks>
    public async IAsyncEnumerable<string> InvokeClaudeWithResponseStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // string claudeModelId = "anthropic.claude-v2";

        // Claude requires you to enclose the prompt as follows:
        string enclosedPrompt = "Human: " + prompt + "\n\nAssistant:";

        //AmazonBedrockRuntimeClient client = new(RegionEndpoint.USEast1);

        string payload = new JsonObject(_config)
             {
                 { "prompt", enclosedPrompt },
             }.ToJsonString();

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
                yield return await buffer.Reader.ReadAsync(cancellationToken);
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
                await buffer.Writer.WriteAsync(streamResponse["completion"]?.GetValue<string>() ?? "", cancellationToken);
            }
        }
        else if (response is not null)
        {
            Console.WriteLine("InvokeModelAsync failed with status code " + response.HttpStatusCode);
        }

        yield break;
    }
}
