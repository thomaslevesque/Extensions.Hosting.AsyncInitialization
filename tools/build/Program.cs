using System.Runtime.CompilerServices;
using static Bullseye.Targets;
using static SimpleExec.Command;

Directory.SetCurrentDirectory(GetSolutionDirectory());

string artifactsDir = Path.GetFullPath("artifacts");
string logsDir = Path.Combine(artifactsDir, "logs");
string buildLogFile = Path.Combine(logsDir, "build.binlog");
string packagesDir = Path.Combine(artifactsDir, "packages");

string solutionFile = "Extensions.Hosting.AsyncInitialization.sln";

Target(
    "artifactDirectories",
    () =>
    {
        Directory.CreateDirectory(artifactsDir);
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(packagesDir);
    });

Target(
    "build",
    DependsOn("artifactDirectories"),
    () => Run(
        "dotnet",
        $"build -c Release /bl:\"{buildLogFile}\" \"{solutionFile}\""));

Target(
    "test",
    DependsOn("build"),
    action: () => Run(
        "dotnet",
        $"test -c Release --no-build"));

Target(
    "pack",
    DependsOn("build", "test"),
    action: () => Run(
        "dotnet",
        $"pack -c Release --no-build -o \"{packagesDir}\""));

Target("default", DependsOn("pack"));

await RunTargetsAndExitAsync(args);

static string GetSolutionDirectory() =>
    Path.GetFullPath(Path.Combine(GetScriptDirectory(), @"../.."));

static string GetScriptDirectory([CallerFilePath] string filename = null) => Path.GetDirectoryName(filename);
