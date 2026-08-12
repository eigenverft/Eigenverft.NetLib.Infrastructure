using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Eigenverft.NetLib.Infrastructure.Tests.Packaging
{
    [TestClass]
    public sealed class PackageConsumptionTests
    {
        [TestMethod]
        public async Task PackedPackageDeclaresAndSuppliesRequiredHostingDependencies()
        {
            string repositoryRoot = FindRepositoryRoot();
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "prj",
                "Eigenverft.NetLib.Infrastructure",
                "Eigenverft.NetLib.Infrastructure.csproj");

            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "Eigenverft.NetLib.Infrastructure.Tests",
                Guid.NewGuid().ToString("N"));
            string packageFeed = Path.Combine(testRoot, "feed");
            string packageVersion = $"1.0.0-packagecheck.{Guid.NewGuid():N}";

            Directory.CreateDirectory(packageFeed);

            try
            {
                await RunDotnetAsync(
                    repositoryRoot,
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "-o",
                    packageFeed,
                    $"-p:Version={packageVersion}",
                    "-v",
                    "minimal");

                string packagePath = Directory.GetFiles(packageFeed, "*.nupkg", SearchOption.TopDirectoryOnly).Single();

                AssertPackageDependency(packagePath, "net8.0", "Microsoft.Extensions.Hosting", "8.0.1");
                AssertPackageDependency(packagePath, "net10.0", "Microsoft.Extensions.Hosting", "10.0.0");

                await BuildConsumerAsync(testRoot, packageFeed, packageVersion, "net8.0", run: false);
                await BuildConsumerAsync(testRoot, packageFeed, packageVersion, "net10.0", run: true);
            }
            finally
            {
                try
                {
                    Directory.Delete(testRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup only; test assertions must not be hidden by temp cleanup failures.
                }
            }
        }

        private static async Task BuildConsumerAsync(
            string testRoot,
            string packageFeed,
            string packageVersion,
            string targetFramework,
            bool run)
        {
            string consumerDirectory = Path.Combine(testRoot, $"consumer-{targetFramework}");
            Directory.CreateDirectory(consumerDirectory);

            string projectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
            string programPath = Path.Combine(consumerDirectory, "Program.cs");
            string nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.config");

            File.WriteAllText(
                projectPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{targetFramework}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <UseAppHost>false</UseAppHost>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Eigenverft.NetLib.Infrastructure" Version="{packageVersion}" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(
                programPath,
                """
                using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;

                var builder = HostApplicationBuilderFactory.CreateWithDefaultDirectory();
                var directories = builder.GetDirectoryLayout();

                string settingsDirectory = directories[DefaultDirectory.ApplicationSettings];
                return string.IsNullOrWhiteSpace(settingsDirectory) ? 1 : 0;
                """);

            XDocument nugetConfig = new(
                new XElement(
                    "configuration",
                    new XElement(
                        "packageSources",
                        new XElement("clear"),
                        new XElement("add", new XAttribute("key", "package-under-test"), new XAttribute("value", packageFeed)))));
            nugetConfig.Save(nugetConfigPath);

            await RunDotnetAsync(
                consumerDirectory,
                "restore",
                projectPath,
                "--configfile",
                nugetConfigPath,
                "-v",
                "minimal");

            await RunDotnetAsync(
                consumerDirectory,
                "build",
                projectPath,
                "-c",
                "Release",
                "--no-restore",
                "-v",
                "minimal");

            if (run)
            {
                await RunDotnetAsync(
                    consumerDirectory,
                    "run",
                    "--project",
                    projectPath,
                    "-c",
                    "Release",
                    "--no-build",
                    "--no-restore");
            }
        }

        private static void AssertPackageDependency(
            string packagePath,
            string targetFramework,
            string dependencyId,
            string expectedVersion)
        {
            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            ZipArchiveEntry nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

            using Stream nuspecStream = nuspecEntry.Open();
            XDocument document = XDocument.Load(nuspecStream);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

            XElement? dependency = document
                .Descendants(ns + "group")
                .Where(group => string.Equals((string?)group.Attribute("targetFramework"), targetFramework, StringComparison.OrdinalIgnoreCase))
                .Elements(ns + "dependency")
                .SingleOrDefault(item => string.Equals((string?)item.Attribute("id"), dependencyId, StringComparison.Ordinal));

            Assert.IsNotNull(
                dependency,
                $"Packed package does not declare required dependency '{dependencyId}' for '{targetFramework}'.");
            Assert.AreEqual(expectedVersion, (string?)dependency.Attribute("version"));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string projectPath = Path.Combine(
                    directory.FullName,
                    "src",
                    "prj",
                    "Eigenverft.NetLib.Infrastructure",
                    "Eigenverft.NetLib.Infrastructure.csproj");

                if (File.Exists(projectPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate the Eigenverft.NetLib.Infrastructure repository root.");
        }

        private static string ResolveDotnetExecutable()
        {
            string? path = Environment.GetEnvironmentVariable("PATH");

            if (!string.IsNullOrWhiteSpace(path))
            {
                foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = Path.Combine(directory.Trim(), "dotnet.exe");

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new FileNotFoundException("Unable to locate dotnet.exe on PATH for package-consumption testing.");
        }

        private static async Task RunDotnetAsync(string workingDirectory, params string[] arguments)
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ResolveDotnetExecutable(),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(2));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort process cleanup before reporting the timeout.
                }

                Assert.Fail($"dotnet {string.Join(" ", arguments)} timed out after two minutes.");
            }

            string output = await outputTask;
            string error = await errorTask;

            Assert.AreEqual(
                0,
                process.ExitCode,
                $"dotnet {string.Join(" ", arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }
}
