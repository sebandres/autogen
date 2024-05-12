// Copyright (c) Microsoft Corporation. All rights reserved.
// RemoteAgentDescriptor.cs

namespace AutoGen.BasicSample.Agents
{
    /// <summary>
    /// Describes the name and purpose of an agent
    /// </summary>
    public class RemoteAgentDescriptor
    {
        public RemoteAgentDescriptor(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; private set; }

        public string Description { get; private set; }
    }
}
