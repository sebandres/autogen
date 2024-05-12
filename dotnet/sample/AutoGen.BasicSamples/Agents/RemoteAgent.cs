// Copyright (c) Microsoft Corporation. All rights reserved.
// RemoteAgent.cs

using System.Text.Json;

namespace AutoGen.BasicSample.Agents
{
    public class RemoteAgent : IAgent
    {
        private readonly HttpClient _httpClient;

        public string Name { get; }

        public string ChatEndpoint { get; }

        public RemoteAgent(string name, HttpClient httpClient, string chatEndpoint = "chat")
        {
            Name = name;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ChatEndpoint = chatEndpoint;
        }

        public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
        {
            var requestData = new TextMessageRequest { Messages = messages };

            var response = await _httpClient.PostAsJsonAsync($"{_httpClient.BaseAddress}{ChatEndpoint}", requestData, cancellationToken);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var textMessage = JsonSerializer.Deserialize<TextMessage>(apiResponse);

            return textMessage ?? new TextMessage(Role.Assistant, "Failed to retrieve the response from the remote agent.", Name);
        }
    }

}
