// Copyright (c) Microsoft Corporation. All rights reserved.
// ExampleXX_Multiple_Agents_DefaultReply.cs

//using System.Text;
using AutoGen;
using AutoGen.BasicSample.Agents;
//using FluentAssertions;

/// <summary>
/// This example shows the basic usage of <see cref="ConversableAgent"/> class.
/// </summary>
public static class ExampleXX_Multiple_Agents_DefaultReply
{
    private static IAgent ConfigureRemoteAgent(string name, string url)
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri(url); // Your API base URL
        client.DefaultRequestHeaders.Add("Accept", "application/json"); // Example header
        client.Timeout = TimeSpan.FromSeconds(30); // Set timeout if needed

        return new RemoteAgent(name, client);
    }

    public static async Task RunAsync()
    {
        var remoteAgent1 = ConfigureRemoteAgent("Agent 1", "http://localhost:5001")
            .RegisterPrintFormatMessageHook();
        var remoteAgent2 = ConfigureRemoteAgent("Agent 2", "http://localhost:5002")
            .RegisterPrintFormatMessageHook();

        var agent1ToAgent2Transition = Transition.Create(remoteAgent1, remoteAgent2);
        var agent2ToAgent1Transition = Transition.Create(remoteAgent2, remoteAgent1, (fromAgent, toAgent, messages) =>
        {
            if (messages.Count() > 5)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }
        );

        var workflow = new Graph(
            [
                agent1ToAgent2Transition,
                agent2ToAgent1Transition,
            ]);


        // create group chat
        var groupChat = new GroupChat(
            members: [remoteAgent1, remoteAgent2],
            workflow: workflow
        );

        // task 1: retrieve the most recent pr from mlnet and save it in result.txt
        var groupChatManager = new GroupChatManager(groupChat);
        await groupChatManager.SendAsync("Test");




        //// create assistant agent
        //var assistantAgent = new DefaultReplyAgent(
        //    name: "agent 1", defaultReply: "Hello")
        //    .RegisterPrintFormatMessageHook();

        //// talk to the assistant agent
        //var reply = await assistantAgent.SendAsync("hello world");
        //reply.Should().BeOfType<TextMessage>();
        //reply.GetContent().Should().Be($"Hello");

        //// to carry on the conversation, pass the previous conversation history to the next call
        //var conversationHistory = new List<IMessage>
        //{
        //    new TextMessage(Role.User, "Hello"), // first message
        //    reply, // reply from assistant agent
        //};

        //reply = await assistantAgent.SendAsync("hello world again", conversationHistory);
        //reply.Should().BeOfType<TextMessage>();
        //reply.GetContent().Should().Be($"Hello");
    }
}
