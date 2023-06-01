using System.CommandLine;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Reliability;

Console.OutputEncoding = Encoding.Unicode;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddFilter("Microsoft", LogLevel.Error)
        .AddFilter("System", LogLevel.Error)
        .AddFilter("Program", LogLevel.Information)
        .AddConsole();
});

// Get an instance of ILogger
var logger = loggerFactory.CreateLogger<Program>();

var _kernel = Kernel.Builder.WithLogger(logger).Build();

_kernel.Config.AddAzureTextCompletionService(EnvVar("AZURE_OPENAI_DEPLOYMENT_NAME"), EnvVar("AZURE_OPENAI_API_ENDPOINT"), EnvVar("AZURE_OPENAI_API_KEY"), EnvVar("AZURE_OPENAI_DEPLOYMENT_LABEL"));

_kernel.Config.SetDefaultHttpRetryConfig(new HttpRetryConfig
{
    MaxRetryCount = 3,
    MinRetryDelay = TimeSpan.FromSeconds(8),
    UseExponentialBackoff = true,
});

var rootCommand = InitializeCommands(_kernel);
return await rootCommand.InvokeAsync(args);

static RootCommand InitializeCommands(IKernel kernel)
{
    var rootCommand = new RootCommand();

    var commitCommand = new Command("commit", "Commit subcommand");

    var commitArgument = new Argument<string>
        ("commitHash", () => { return string.Empty; }, "An argument that is parsed as a string.");
    rootCommand.Add(commitArgument);
    commitCommand.Add(commitArgument);

    rootCommand.SetHandler(async () => await RunPullRequestDescription(kernel));
    commitCommand.SetHandler(async (commitArgumentValue) => await RunCommitMessage(kernel, commitArgumentValue), commitArgument);

    rootCommand.Add(commitCommand);

    return rootCommand;
}


static async Task RunCommitMessage(IKernel kernel, string commitHash = "")
{
    string output = string.Empty;
    if (!string.IsNullOrEmpty(commitHash))
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"show {commitHash}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            }
        };
        process.Start();

        output = process.StandardOutput.ReadToEnd();
    }
    else
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --staged",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            }
        };
        process.Start();

        output = process.StandardOutput.ReadToEnd();

        if (string.IsNullOrEmpty(output))
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "diff HEAD~1",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                }
            };
            process.Start();

            output = process.StandardOutput.ReadToEnd();
        }
    }

    var pullRequestSkill = kernel.ImportSkill(new PRSkill.PullRequestSkill(kernel));

    var kernelResponse = await kernel.RunAsync(output, pullRequestSkill["GenerateCommitMessage"]);

    kernel.Log.LogInformation("Commit Message:\n{result}", kernelResponse.Result);
}

static async Task RunPullRequestDescription(IKernel kernel)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "show --ignore-space-change origin/main..HEAD",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        }
    };
    process.Start();

    string output = process.StandardOutput.ReadToEnd();
    var pullRequestSkill = kernel.ImportSkill(new PRSkill.PullRequestSkill(kernel));

    var kernelResponse = await kernel.RunAsync(output, pullRequestSkill["GeneratePR"]);
    kernel.Log.LogInformation("Pull Request Description:\n{result}", kernelResponse.Result);
}

static string EnvVar(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrEmpty(value)) throw new Exception($"Env var not set: {name}");
    return value;
}