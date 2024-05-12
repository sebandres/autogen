// Copyright (c) Microsoft Corporation. All rights reserved.
// ExampleXX_SemanticKernel_CodeGenerator.cs

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

//using System.Text;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
//using FluentAssertions;
using Microsoft.SemanticKernel;
//using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
namespace AutoGen.BasicSample;

public class ExampleXX_SemanticKernel_CodeGenerator
{
    public class AdminPlugin
    {
        [KernelFunction]
        [Description("Creates a working directory.")]
        public string CreateWorkingDirectory()
        {
            // setup dotnet interactive
            var workDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            if (!Directory.Exists(workDir))
                Directory.CreateDirectory(workDir);

            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Working Directory Created: {workDir}]");
            Console.ResetColor();

            return workDir;
        }
    }

    public class CodePlugin
    {
        [KernelFunction]
        [Description("Writes a file in a solution or project.")]
        public string WriteFile(
            [Description("The working directory")] string workingDir,
            [Description("File path relative to the working directoty")] string relativeFilePath,
            [Description("The full content of the file to save")] string fileFullContent
            )
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Saving file at: {workingDir} /{relativeFilePath}]");
            Console.WriteLine($"{fileFullContent}");
            Console.ResetColor();

            // ensure sub directories exist
            Directory.CreateDirectory(Path.GetDirectoryName($"{workingDir}/{relativeFilePath}"));

            File.WriteAllText($"{workingDir}/{relativeFilePath}", fileFullContent);

            return "File saved successfully";
        }

        [KernelFunction]
        [Description("Reads all files in a folder or directory recursively")]
        public string ReadAllFiles([Description("The directory we are working in")] string workingDir)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Reading solution at: {workingDir}]");
            Console.ResetColor();

            var filesContent = ReadFilesRecursively(workingDir, workingDir);
            return filesContent;
        }

        private string ReadFilesRecursively(string workingDir, string directoryPath)
        {
            var entireContent = new StringBuilder();
            try
            {
                // Get all files in the current directory.
                foreach (string file in Directory.GetFiles(directoryPath))
                {
                    // Check if the file ends with .cs or .csproj.
                    if (file.EndsWith(".cs") || file.EndsWith(".csproj"))
                    {
                        // Read the content of the file.
                        entireContent.AppendLine($"```csharp {file.Replace(workingDir, string.Empty)}");
                        entireContent.AppendLine(File.ReadAllText(file));
                        entireContent.AppendLine("```");
                    }
                }

                // Recursively call this method for each subdirectory.
                foreach (string subdirectory in Directory.GetDirectories(directoryPath))
                {
                    entireContent.AppendLine(ReadFilesRecursively(workingDir, subdirectory));
                }
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine($"Access denied to the path: {directoryPath}. {e.Message}");
            }
            catch (IOException e)
            {
                Console.WriteLine($"An I/O error occurred while accessing the path: {directoryPath}. {e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"An error occurred: {e.Message}");
            }

            return entireContent.ToString();
        }
    }

    public class CreateProjectPlugin
    {
        [KernelFunction]
        [Description("Creates a new dotnet console project.")]
        public void CreateDotnetConsoleProject(
            [Description("The working directory")] string workingDir,
            [Description("The name of the project")] string projectName)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Project Created: {projectName}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new console -o {projectName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }

        [KernelFunction]
        [Description("Creates a new dotnet library project.")]
        public void CreateDotnetLibraryProject(
            [Description("The working directory")] string workingDir,
            [Description("The name of the project")] string projectName)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Project Created: {projectName}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new classlib -o {projectName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }

        [KernelFunction]
        [Description("Creates a new dotnet solution.")]
        public void CreateDotnetSolution(
            [Description("The working directory")] string workingDir,
            [Description("The name of the solution")] string solutionName)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Solution Created: {solutionName}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new sln -n {solutionName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }

        [KernelFunction]
        [Description("Adds a dotnet project to a dotnet solution.")]
        public void AddDotnetProjectToSolution(
            [Description("The working directory")] string workingDir,
            [Description("The full path of the project we want to add to the solution")] string projectPath)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Project Added to Solution: {projectPath}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"sln add --in-root ""{projectPath}""",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }

        [KernelFunction]
        [Description("Adds a reference from a project to another project.")]
        public void AddDotnetProjectReference(
            [Description("The working directory")] string workingDir,
            [Description("The name of the project we are adding the reference to")] string projectName,
            [Description("The name of the project to be referenced")] string projectToBeRefernecedName)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Project being references: {workingDir} / {projectName} / {projectToBeRefernecedName}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"add reference {Path.Combine(workingDir, projectToBeRefernecedName)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.Combine(workingDir, projectName)
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }
    }

    public class CompileCodePlugin
    {
        [KernelFunction]
        [Description("Compiles a dotnet solution")]
        public void CompileDotnetSolution([Description("The directory we want to compile")] string workingDir)
        {
            // Print to the console
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"[Compiling solution at: {workingDir}]");
            Console.ResetColor();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"build",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDir
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }
    }

    private static SemanticKernelAgent CreateAdminKernel(OpenAIConfig gptConfig)
    {
        var openAIKey = gptConfig.ApiKey;
        var modelId = gptConfig.ModelId;
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelId, apiKey: openAIKey);
        var kernel = builder.Build();
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.0f
        };

        kernel.Plugins.AddFromObject(new AdminPlugin());

        var skAgent = kernel
            .ToSemanticKernelAgent(
            name: "admin",
            //description: "A lead software engineer who can coordinate a team of engineers to solve a problem.",
            systemMessage: "You are a lead software engineer who takes coding problem from user and resolves problem by splitting them into small tasks and assign each task to the most appropriate engineer." +
            "Here's available engineers who you can assign a task to:" +
            "    - projectCreator: creates new projects and solutions for dotnet, it also adds references between projects. It doesn't create files." +
            "    - coder: write and save dotnet code to resolve a task. The coder will create the class files." +
            "    - compiler: compiles dotnet code from coder and reports back with any issues." +
            "" +
            "Create a single working directory using a kernel function." +
            "All engineers share the same working directory." +
            "Use the same working directory in all interactions." +
            "" +
            "Before assigning tasks to engineers you must come up with the plan to solve the problem." +
            "You will assign tasks to the engineers in sequential order.",
            settings: settings);

        return skAgent;
    }

    private static SemanticKernelAgent CreateCodeCompilerKernel(OpenAIConfig gptConfig)
    {
        var openAIKey = gptConfig.ApiKey;
        var modelId = gptConfig.ModelId;
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelId, apiKey: openAIKey);
        var kernel = builder.Build();
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.0f
        };

        kernel.Plugins.AddFromObject(new CompileCodePlugin());

        var skAgent = kernel
            .ToSemanticKernelAgent(
            name: "compiler",
            //description: "A Senior c# Engineer Agent that compiles code",
            systemMessage: "You are a Senior C# Engineer that can compile applications in dotnet 8." +
            "After you compile a code you must report back any errors so that they can be fixed." +
            "You never create any code.",
            settings: settings);

        return skAgent;
    }

    private static SemanticKernelAgent CreateCodeBuilderKernel(OpenAIConfig gptConfig)
    {
        var openAIKey = gptConfig.ApiKey;
        var modelId = gptConfig.ModelId;
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelId, apiKey: openAIKey);
        var kernel = builder.Build();
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.0f
        };

        kernel.Plugins.AddFromObject(new CodePlugin());

        var skAgent = kernel
            .ToSemanticKernelAgent(
            name: "coder",
            //description: "A Senior c# Engineer Agent that can only code on existing solutions and can't compile.",
            systemMessage: "You are a Senior C# Engineer that can code console applications in dotnet 8." +
            "You need the working directory in order to perform your tasks." +
            "Before making any changes you will first read all the files in the working directory." +
            "Once you know what code you need to create or change you will write it to a file." +
            "If you need a new project created you must ask the project-creator to create it for you." +
            "You must implement all code." +
            "All code must be complete." +
            "Don't say the contents of files you read." +
            "Don't say the contents of what changes you will make, simply save them.",
            settings: settings);

        return skAgent;
    }

    private static SemanticKernelAgent CreateProjectBuilderKernel(OpenAIConfig gptConfig)
    {
        var openAIKey = gptConfig.ApiKey;
        var modelId = gptConfig.ModelId;
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelId, apiKey: openAIKey);
        var kernel = builder.Build();
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.0f
        };

        kernel.Plugins.AddFromObject(new CreateProjectPlugin());

        var skAgent = kernel
            .ToSemanticKernelAgent(
            name: "projectCreator",
            //description: "A Dotnet project creator Agent that can create dotnet projects and solutions but can't code or compile.",
            systemMessage: "You are a Senior C# Engineer that can create console applications, class libraries and solutions in dotnet 8." +
            "You never generate any code." +
            "Every time you create a new project you must add it to the solution." +
            "Don't assign work to other agents.",
            settings: settings);

        return skAgent;
    }

    public static async Task RunAsync()
    {
        var gpt35Config = LLMConfiguration.GetOpenAIGPT3_5_Turbo();
        var gpt4Config = LLMConfiguration.GetOpenAIGPT4();

        var projectCreatorAgent = CreateProjectBuilderKernel(gpt35Config);
        var projectCreatorAgentWithMiddleware = projectCreatorAgent
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        var coderAgent = CreateCodeBuilderKernel(gpt4Config);
        var coderAgentWithMiddleware = coderAgent
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        var compilerAgent = CreateCodeCompilerKernel(gpt35Config);
        var compilerAgentWithMiddleware = compilerAgent
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        var adminAgent = CreateAdminKernel(gpt4Config);
        var adminAgentWithMiddleware = adminAgent
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        var groupAdmin = new GPTAgent(
            name: "groupAdmin",
            systemMessage: "You are the admin of the group chat",
            temperature: 0f,
            config: gpt35Config);

        var userProxy = new UserProxyAgent(
            name: "user",
            defaultReply: GroupChatExtension.TERMINATE,
            humanInputMode: HumanInputMode.NEVER)
    .RegisterPrintMessage();

        //// Create admin agent
        //var admin = new AssistantAgent(
        //    name: "admin",
        //    systemMessage: """
        //    You are a lead software engineer who takes coding problem from user and resolve problem by splitting them into small tasks and assign each task to the most appropriate engineer.
        //    Here's available engineers who you can assign a task to:
        //    - project-creator: creates new projects and solutions for dotnet, it also adds references between projects. It doesn't create files.
        //    - coder: write and save dotnet code to resolve a task. The coder will create the class files.
        //    - compiler: compiles dotnet code from coder and reports back with any issues.

        //    You always provide the working directory to every engineer when engaging with them.
        //    Always ask engineers to implement the complete code.
        //    """,
        //    llmConfig: new ConversableAgentConfig
        //    {
        //        Temperature = 0,
        //        ConfigList = [gpt35Config],
        //    })
        //    .RegisterPrintMessage();

        var adminToCoderTransition = Transition.Create(adminAgentWithMiddleware, coderAgentWithMiddleware, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != adminAgentWithMiddleware.Name)
            {
                return false;
            }

            return true;
        });

        var adminToCompilerTransition = Transition.Create(adminAgentWithMiddleware, compilerAgentWithMiddleware, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != adminAgentWithMiddleware.Name)
            {
                return false;
            }

            return true;
        });

        var adminToProjectCreatorTransition = Transition.Create(adminAgentWithMiddleware, projectCreatorAgentWithMiddleware, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != adminAgentWithMiddleware.Name)
            {
                return false;
            }

            return true;
        });

        var projectCreatorToAdminTransition = Transition.Create(projectCreatorAgentWithMiddleware, adminAgentWithMiddleware);
        var coderToAdminTransition = Transition.Create(coderAgentWithMiddleware, adminAgentWithMiddleware);
        var coderToProjectCreatorTransition = Transition.Create(coderAgentWithMiddleware, projectCreatorAgentWithMiddleware);
        var projectCreatorToCoderTransition = Transition.Create(projectCreatorAgentWithMiddleware, coderAgentWithMiddleware);
        var compilerToAdminTransition = Transition.Create(compilerAgentWithMiddleware, adminAgentWithMiddleware);
        var userToAdminTransition = Transition.Create(userProxy, adminAgentWithMiddleware);
        var adminToUserTransition = Transition.Create(adminAgentWithMiddleware, userProxy, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != adminAgentWithMiddleware.Name)
            {
                return false;
            }

            return true;
        });

        var workflow = new Graph(
            [
                adminToProjectCreatorTransition,
                projectCreatorToAdminTransition,
                adminToCoderTransition,
                coderToAdminTransition,
                adminToCompilerTransition,
                compilerToAdminTransition,
                //projectCreatorToCoderTransition,
                //coderToProjectCreatorTransition,
                //coderToRunnerTransition,
                //coderToReviewerTransition,
                //reviewerToAdminTransition,
                //adminToRunnerTransition,
                //runnerToAdminTransition,
                adminToUserTransition,
                userToAdminTransition,
            ]);


        // create group chat
        var groupChat = new GroupChat(
            admin: groupAdmin,
            members: [adminAgentWithMiddleware, coderAgentWithMiddleware, compilerAgentWithMiddleware, projectCreatorAgentWithMiddleware, userProxy],
            workflow: workflow);

        var groupChatManager = new GroupChatManager(groupChat);
        //await userProxy.SendAsync(groupChatManager, "Create a C# console application that will take the user input as numbers, add them and then display the result in the console.", maxRound: 30);
        await userProxy.SendAsync(groupChatManager, "Create a snake game in a C# console application, " +
            "the game must be complete and have all code, " +
            "the game must be fully playable using the cursor keys of the keyboard, " +
            "keep track of the lives which the player starts with 3 and the score every time it eats a fruit it wins 10 points. " +
            "Always display the lives left as well as the score on screen whilst the user is playing." +
            "Implement all the logic required to run the game.", maxRound: 30);
    }
}
