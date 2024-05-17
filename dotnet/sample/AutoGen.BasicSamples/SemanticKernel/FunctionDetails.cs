// Copyright (c) Microsoft Corporation. All rights reserved.
// FunctionDetails.cs

namespace AutoGen.BasicSample.SemanticKernel
{
    public class FunctionDetails
    {
        public string Name { get; set; }
        public string Description { get; set; }
        //public string Parameters { get; set; }
    }

    public class FunctionDetailsParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public string Parameters { get; set; }
    }
}
