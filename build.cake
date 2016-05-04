//////////////////////////////////////////////////////////////////////
// ADDINS
//////////////////////////////////////////////////////////////////////

#addin "Cake.FileHelpers"

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool GitVersion.CommandLine
#tool GitLink

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// should MSBuild & GitLink treat any errors as warnings.
var treatWarningsAsErrors = false;

// Get whether or not this is a local build.
var local = BuildSystem.IsLocalBuild;
Information("local={0}", local);

var isRunningOnUnix = IsRunningOnUnix();
Information("isRunningOnUnix={0}", isRunningOnUnix);

var isRunningOnWindows = IsRunningOnWindows();
Information("isRunningOnWindows={0}", isRunningOnWindows);

//var isRunningOnBitrise = Bitrise.IsRunningOnBitrise;
var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
Information("isRunningOnAppVeyor={0}", isRunningOnAppVeyor);

var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
Information("isPullRequest={0}", isPullRequest);

var isRepository = StringComparer.OrdinalIgnoreCase.Equals("ghuntley/HttpClientDiagnostics", AppVeyor.Environment.Repository.Name);
Information("isRepository={0}", isRepository);

// Parse release notes.
var releaseNotes = ParseReleaseNotes("RELEASENOTES.md");

// Get version.
var version = releaseNotes.Version.ToString();
Information("version={0}", version);

var epoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
Information("epoch={0}", epoch);

var gitSha = GitVersion().Sha;
Information("gitSha={0}", gitSha);

var semVersion = local ? string.Format("{0}.{1}", version, epoch) : string.Format("{0}.{1}", version, epoch);
Information("semVersion={0}", semVersion);

// Define directories.
var artifactDirectory = "./artifacts/";

// Define global marcos.
Action Abort = () => { throw new Exception("a non-recoverable fatal error occurred."); };

Action<string> RestorePackages = (solution) =>
{
    NuGetRestore(solution, new NuGetRestoreSettings() { ConfigFile = "./src/.nuget/NuGet.config" });
};

Action<string, string> Package = (nuspec, basePath) =>
{
    CreateDirectory(artifactDirectory);

    Information("Packaging {0} using {1} as the BasePath.", nuspec, basePath);

    NuGetPack(nuspec, new NuGetPackSettings {
        Authors                  = new [] { "Geoffrey Huntley" },
        Owners                   = new [] { "ghuntley" },

        ProjectUrl               = new Uri("https://ghuntley.com/"),
        IconUrl                  = new Uri("https://i.imgur.com/8XUGpUI.png"),
        LicenseUrl               = new Uri("https://opensource.org/licenses/MIT"),
        Copyright                = "Copyright (c) Geoffrey Huntley",
        RequireLicenseAcceptance = false,

        Version                  = semVersion,
        Tags                     = new [] {  "httpclient", "http", "rest", "networking", "diagnostics", "tracing", "logging", "liblog" },
        ReleaseNotes             = new List<string>(releaseNotes.Notes),

        Symbols                  = true,
        Verbosity                = NuGetVerbosity.Detailed,
        OutputDirectory          = artifactDirectory,
        BasePath                 = basePath,
    });
};

Action<string> SourceLink = (solutionFileName) =>
{
    GitLink("./", new GitLinkSettings() {
        RepositoryUrl = "https://github.com/ghuntley/HttpClientDiagnostics",
        SolutionFileName = solutionFileName,
        ErrorsAsWarnings = treatWarningsAsErrors,
    });
};


///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(() =>
{
    Information("Building version {0} of HttpClientDiagnostics.", semVersion);
});

Teardown(() =>
{
    // Executed AFTER the last task.
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Build")
    .IsDependentOn("RestorePackages")
    .IsDependentOn("UpdateAssemblyInfo")
    .Does (() =>
{
    if(isRunningOnUnix)
    {
        throw new NotImplementedException("Building on OSX is not implemented.");
    }
    else
    {
        Action<string> build = (filename) =>
        {
            var solution = System.IO.Path.Combine("./src/", filename);

            // UWP (project.json) needs to be restored before it will build.
            RestorePackages(solution);

            Information("Building {0}", solution);

            MSBuild(solution, new MSBuildSettings()
                .SetConfiguration(configuration)
                .WithProperty("NoWarn", "1591") // ignore missing XML doc warnings
                .WithProperty("TreatWarningsAsErrors", treatWarningsAsErrors.ToString())
                .SetVerbosity(Verbosity.Minimal)
                .SetNodeReuse(false));

            SourceLink(solution);
        };

        build("HttpClientDiagnostics.sln");
    }
});

Task("UpdateAppVeyorBuildNumber")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UpdateBuildVersion(semVersion);
});

Task("UpdateAssemblyInfo")
    .IsDependentOn("UpdateAppVeyorBuildNumber")
    .Does (() =>
{
    var file = "./src/CommonAssemblyInfo.cs";

    CreateAssemblyInfo(file, new AssemblyInfoSettings {
        Product = "HttpClientDiagnostics",
        Version = version,
        FileVersion = version,
        InformationalVersion = semVersion,
        Copyright = "Copyright (c) Geoffrey Huntley"
    });
});

Task("RestorePackages").Does (() =>
{
    RestorePackages("./src/HttpClientDiagnostics.sln");
});

Task("RunUnitTests")
    .IsDependentOn("Build")
    .Does(() =>
{
//    XUnit2("./src/HttpClientDiagnostics.Tests/bin/Release/HttpClientDiagnostics.Tests.dll", new XUnit2Settings {
//        OutputDirectory = artifactDirectory,
//        XmlReportV1 = true,
//        NoAppDomain = true
//    });
});

Task("Package")
    .IsDependentOn("Build")
    .Does (() =>
{
    if(isRunningOnUnix)
    {
        throw new NotImplementedException("Packaging on OSX is not implemented.");
    }
    else
    {
        Package("./src/HttpClientDiagnostics.nuspec", "./src/HttpClientDiagnostics");
    }
});

Task("Publish")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .Does (() =>
{
    if(isRunningOnUnix)
    {
        throw new NotImplementedException("Publishing on OSX is not implemented.");
    }
    else
    {
        // Resolve the API key.
        var apiKey = EnvironmentVariable("MYGET_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Could not resolve MyGet API key.");
        }

        // only push whitelisted packages.
        foreach(var package in new[] { "HttpClientDiagnostics" })
        {
            // only push the package which was created during this build run.
            var packagePath = artifactDirectory + File(string.Concat(package, ".", semVersion, ".nupkg"));
            //var symbolsPath = artifactDirectory + File(string.Concat(package, ".", semVersion, ".symbols.nupkg"));

            // Push the package.
            NuGetPush(packagePath, new NuGetPushSettings {
                Source = "https://www.myget.org/F/ghuntley/api/v2/package",
                ApiKey = apiKey
            });

            // Push the symbols
            //NuGetPush(symbolsPath, new NuGetPushSettings {
            //    Source = "https://www.myget.org/F/ghuntley/api/v2/package",
            //    ApiKey = apiKey
            //});

        }
    }
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////


//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget("Publish");
