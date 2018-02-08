//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

using Path = System.IO.Path;
using IO = System.IO;
using System.Text.RegularExpressions;
using Cake.Common.Tools;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////
var localPackagesDir = "../LocalPackages";
var buildDir = @".\build";
var unpackFolder = Path.Combine(buildDir, "temp");
var unpackFolderFullPath = Path.GetFullPath(unpackFolder);
var artifactsDir = @".\artifacts";
var nugetVersion = string.Empty;
var nugetPackageFile = string.Empty;

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(context =>
{
    
});

Teardown(context =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(buildDir);
    CleanDirectory(unpackFolder);
    CleanDirectory(artifactsDir);
});

// Task("Restore-NuGet-Packages")
//     .IsDependentOn("Clean")
//     .Does(() =>
// {
//     NuGetRestore("./src/Example.sln");
// });

Task("Unpack-Source-Package")
    .IsDependentOn("Clean")
    .Does(() => 
{
    var processArgumentBuilder = new ProcessArgumentBuilder();
    processArgumentBuilder.Append("-Command");
    processArgumentBuilder.Append($"\"Save-Module -Name AWSPowerShell -Path {unpackFolderFullPath}\"");
    var processSettings = new ProcessSettings { Arguments = processArgumentBuilder, WorkingDirectory = buildDir };
    StartProcess("powershell.exe", processSettings);
    Information($"Saved module to {unpackFolderFullPath}");
});

Task("GetVersion")
    .IsDependentOn("Unpack-Source-Package")
    .Does(() => 
{
    Information("Determining version number");
    Information(System.IO.Directory.GetCurrentDirectory());

    nugetVersion = new DirectoryInfo(unpackFolderFullPath)
        .GetDirectories()[0] // AWSPowerShell
        .GetDirectories()[0].Name; // The version

    Information($"Calculated version number: {nugetVersion}");

    if(BuildSystem.IsRunningOnTeamCity)
        BuildSystem.TeamCity.SetBuildNumber(nugetVersion);
});

Task("Pack")
    .IsDependentOn("GetVersion")
    .Does(() =>
{
    Information($"Building Octopus.Dependencies.AWSPS v{nugetVersion}");
    
    NuGetPack("awsps.nuspec", new NuGetPackSettings {
        BasePath = Path.Combine(nugetVersion, unpackFolder),
        OutputDirectory = artifactsDir,
        ArgumentCustomization = args => args.Append($"-Properties \"version={nugetVersion};subpackagename=AWSPS\"")
    });
});

Task("Publish")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .IsDependentOn("Pack")
    .Does(() => 
{
    NuGetPush($"{artifactsDir}/Octopus.Dependencies.AWSPS.{nugetVersion}.nupkg", new NuGetPushSettings {
        Source = "https://octopus.myget.org/F/octopus-dependencies/api/v3/index.json",
        ApiKey = EnvironmentVariable("MyGetApiKey")
    });
});

Task("CopyToLocalPackages")
    .WithCriteria(BuildSystem.IsLocalBuild)
    .IsDependentOn("Pack")
    .Does(() => 
{
    CreateDirectory(localPackagesDir);
    CopyFileToDirectory(Path.Combine(artifactsDir, $"Octopus.Dependencies.AWSPS.{nugetVersion}.nupkg"), localPackagesDir);
});


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("FullChain")
    .IsDependentOn("Clean")
    .IsDependentOn("Unpack-Source-Package")
    .IsDependentOn("GetVersion")
    .IsDependentOn("Pack")
    .IsDependentOn("Publish")
    .IsDependentOn("CopyToLocalPackages");

Task("Default").Does(() => 
{  
    RunTarget("FullChain");
});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);