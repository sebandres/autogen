// Copyright (c) Microsoft Corporation. All rights reserved.
// BedrockConfig.cs

using AutoGen.Core;

namespace AutoGen.AWS.Agent
{
    public class BedrockConfig : ILLMConfig
    {
        public BedrockConfig(string modelId)
        {
            this.ModelId = modelId;
        }

        public string ModelId { get; }
    }
}
