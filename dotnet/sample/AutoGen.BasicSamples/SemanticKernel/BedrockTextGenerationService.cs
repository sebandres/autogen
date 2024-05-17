// Copyright (c) Microsoft Corporation. All rights reserved.
// BedrockTextGenerationService.cs

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AutoGen.AWS;
using AutoGen.AWS.Agent;
using AutoGen.Core;
using Azure.AI.OpenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;

namespace AutoGen.BasicSample.SemanticKernel
{
    public class BedrockTextGenerationService : BedrockAgent, ITextGenerationService, IChatCompletionService
    {
        public BedrockTextGenerationService(string name, string systemMessage, float temperature, BedrockConfig config) : base(name, systemMessage, temperature, config)
        {
        }

        public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

        private async Task<string> ProcessLlama3Response(string response, Kernel? kernel = null)
        {
            try
            {
                var function = JsonSerializer.Deserialize<FunctionDefinition>(response.Split('\n').Last());

                foreach (var plugin in kernel.Plugins)
                {
                    foreach (var item in plugin)
                    {
                        if ($"{item.PluginName}-{item.Name}" == function.Name)
                        {
                            var functionResponse = await item.InvokeAsync(kernel);

                            if (functionResponse.ValueType == typeof(string))
                            {
                                return
                                    "Called function: " +
                                    function.Name +
                                    Environment.NewLine +
                                    "Function description: " +
                                    function.Description +
                                    Environment.NewLine +
                                    $"Result: {functionResponse.GetValue<string>()}";
                            }
                        }
                    }
                }
            }
            catch
            {

            }

            return response;
        }

        private string BuildLlama3Prompt(ChatHistory chatHistory, Kernel? kernel = null)
        {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            //var prompt = string.Join(Environment.NewLine, chatHistory.Where(x => x.Role != AuthorRole.System).Select(m => $"{m.Metadata?["AuthorName"] ?? "user"}:{m.Content}"));
            var prompt = string.Join(Environment.NewLine, chatHistory
                .Where(x => x.Role != AuthorRole.System)
                .Select(m => $"<|start_header_id|>{m.Metadata?["AuthorName"] ?? "user"}<|end_header_id|>" +
                $"{m.Content}<|eot_id|>"));
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            StringBuilder functionsPrompt = new StringBuilder();

            // Provide all functions from the kernel.
            IList<KernelFunctionMetadata> functions = kernel.Plugins.GetFunctionsMetadata();
            if (functions.Count > 0)
            {
                //options.ToolChoice = ChatCompletionsToolChoice.Auto;
                for (int i = 0; i < functions.Count; i++)
                {
                    var functionDefinition = new ChatCompletionsFunctionToolDefinition(functions[i].ToOpenAIFunction().ToFunctionDefinition());
                    var serializedFunction = JsonSerializer.Serialize(functionDefinition);
                    var deserialised = JsonSerializer.Deserialize<FunctionDetails>(serializedFunction);
                    //deserialised.Parameters = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(deserialised.Parameters));
                    var reserializedFunction = JsonSerializer.Serialize(deserialised);

                    functionsPrompt.AppendLine(reserializedFunction);
                    //options.Tools.Add(new ChatCompletionsFunctionToolDefinition(functions[i].ToOpenAIFunction().ToFunctionDefinition()));
                }
            }

            var fullPrompt =
                "<|begin_of_text|><|start_header_id|>system<|end_header_id|>" +
                Environment.NewLine +
                chatHistory.First(x => x.Role == AuthorRole.System).Content +
                Environment.NewLine +
                "If you can, use one of the available functions listed in the <functions> tag. Each function is a JSON payload with a Name and a Description which indicates when to use the tool." +
                Environment.NewLine +
                "<functions>" +
                Environment.NewLine +
                functionsPrompt.ToString() +
                "</functions>" +
                Environment.NewLine +
                "If you use one of the functions then only reply with the same JSON payload of the function." +
                "<|eot_id|>" +
                Environment.NewLine +
                "<|begin_of_text|><|start_header_id|>user<|end_header_id|>" +
                Environment.NewLine +
                prompt +
                "<|eot_id|><|start_header_id|>assistant<|end_header_id|>";

            return fullPrompt;
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            var fullPrompt = BuildLlama3Prompt(chatHistory, kernel);

            var response = await GenerateReplyAsync(fullPrompt, cancellationToken: cancellationToken).ConfigureAwait(false) as TextMessage;

            var finalResponse = await ProcessLlama3Response(response.Content, kernel);

            return new List<ChatMessageContent> { new ChatMessageContent(AuthorRole.Assistant, finalResponse, metadata: new Dictionary<string, object?>
            {{ "AuthorName", this.Name } }) };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var fullPrompt = BuildLlama3Prompt(chatHistory, kernel);

            await foreach (TextMessageUpdate response in GenerateStreamingReplyAsync(fullPrompt, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                //var finalResponse = await ProcessLlama3Response(response.Content, kernel);

                yield return new StreamingChatMessageContent(AuthorRole.Assistant, response.Content, new Dictionary<string, object?>
            {{ "AuthorName", this.Name } });
            }

            yield break;
        }

        public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(string prompt, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (TextMessageUpdate response in GenerateStreamingReplyAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return new StreamingTextContent(response.Content);
            }

            yield break;
        }

        public async Task<IReadOnlyList<TextContent>> GetTextContentsAsync(string prompt, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            var response = await GenerateReplyAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false) as TextMessage;

            return new List<TextContent> { new TextContent(response.Content) };
        }
    }
}
