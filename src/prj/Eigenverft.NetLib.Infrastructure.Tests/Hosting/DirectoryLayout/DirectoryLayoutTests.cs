using System;
using System.Collections.Generic;
using System.IO;

using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.NetLib.Infrastructure.Tests;

[TestClass]
public sealed class DirectoryLayoutTests
{
    [TestMethod]
    public void CreateDefaultCreatesStandardDirectories()
    {
        string root = CreateTempRoot();

        try
        {
            AppDirectoryLayout layout = AppDirectoryLayoutFactory.CreateDefault(rootPath: root);

            Assert.AreEqual(Path.GetFullPath(root), layout.RootPath);
            Assert.AreEqual(Path.Combine(root, "AppLogs"), layout[DefaultDirectory.ApplicationLogFiles]);
            Assert.AreEqual(Path.Combine(root, "AppData"), layout[DefaultDirectory.ApplicationData]);
            Assert.AreEqual(Path.Combine(root, "AppState"), layout[DefaultDirectory.ApplicationState]);
            Assert.AreEqual(Path.Combine(root, "AppProtectionKeys"), layout[DefaultDirectory.ApplicationProtectionKeys]);
            Assert.AreEqual(Path.Combine(root, "AppCerts"), layout[DefaultDirectory.ApplicationCerts]);
            Assert.AreEqual(Path.Combine(root, "AppSettings"), layout[DefaultDirectory.ApplicationSettings]);

            foreach (string directoryPath in layout.GetByKey.Values)
            {
                Assert.IsTrue(Directory.Exists(directoryPath), $"Expected directory '{directoryPath}' to exist.");
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void HostApplicationBuilderFactoryCreatesDefaultLayout()
    {
        HostApplicationBuilder builder = HostApplicationBuilderFactory.CreateWithDefaultDirectory();
        IAppDirectoryLayout layout = builder.GetDirectoryLayout();

        Assert.AreEqual(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), layout.RootPath);
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "AppSettings"), layout[DefaultDirectory.ApplicationSettings]);
    }

    [TestMethod]
    public void HostApplicationBuilderFactoryAcceptsExplicitArguments()
    {
        HostApplicationBuilder builder = HostApplicationBuilderFactory.CreateWithDefaultDirectory(new[] { "--SampleSetting=Expected" });

        Assert.AreEqual("Expected", builder.Configuration["SampleSetting"]);
        Assert.IsNotNull(builder.GetDirectoryLayout());
    }

    [TestMethod]
    public void HostBuilderExtensionProvidesSameLayoutBeforeAndAfterBuild()
    {
        string folderName = $"evf-layout-test-{Guid.NewGuid():N}";
        string expectedPath = Path.Combine(AppContext.BaseDirectory, folderName);

        try
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            builder.AddDirectoryLayout(new Dictionary<string, string>
            {
                ["TestData"] = folderName,
            });

            IAppDirectoryLayout beforeBuild = builder.GetDirectoryLayout();

            using IHost host = builder.Build();
            IAppDirectoryLayout fromServices = host.Services.GetRequiredService<IAppDirectoryLayout>();
            AppDirectoryLayout concrete = host.Services.GetRequiredService<AppDirectoryLayout>();

            Assert.AreSame(beforeBuild, fromServices);
            Assert.AreSame(beforeBuild, concrete);
            Assert.AreEqual(expectedPath, beforeBuild["TestData"]);
            Assert.IsTrue(Directory.Exists(expectedPath));
        }
        finally
        {
            DeleteTempRoot(expectedPath);
        }
    }

    [TestMethod]
    public void TypedOverridesRetainUnspecifiedDefaults()
    {
        string root = CreateTempRoot();

        try
        {
            AppDirectoryLayout layout = AppDirectoryLayoutFactory.CreateDefault(
                new Dictionary<DefaultDirectory, string>
                {
                    [DefaultDirectory.ApplicationData] = "State",
                },
                root);

            Assert.AreEqual("State", Path.GetFileName(layout[DefaultDirectory.ApplicationData]));
            Assert.AreEqual("AppLogs", Path.GetFileName(layout[DefaultDirectory.ApplicationLogFiles]));
            Assert.AreEqual("AppState", Path.GetFileName(layout[DefaultDirectory.ApplicationState]));
            Assert.AreEqual("AppProtectionKeys", Path.GetFileName(layout[DefaultDirectory.ApplicationProtectionKeys]));
            Assert.AreEqual("AppCerts", Path.GetFileName(layout[DefaultDirectory.ApplicationCerts]));
            Assert.AreEqual("AppSettings", Path.GetFileName(layout[DefaultDirectory.ApplicationSettings]));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void CreateSupportsCustomSemanticKeys()
    {
        string root = CreateTempRoot();

        try
        {
            AppDirectoryLayout layout = AppDirectoryLayoutFactory.Create(
                new Dictionary<string, string>
                {
                    ["Cache"] = "cache",
                    ["Imports"] = "incoming",
                },
                root);

            Assert.AreEqual(Path.Combine(root, "cache"), layout["Cache"]);
            Assert.AreEqual(Path.Combine(root, "incoming"), layout["Imports"]);
            Assert.IsTrue(layout.TryGet("cache", out string cachePath));
            Assert.AreEqual(Path.Combine(root, "cache"), cachePath);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void CreateRejectsNestedFolderMappings()
    {
        string root = CreateTempRoot();

        try
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                AppDirectoryLayoutFactory.Create(
                    new Dictionary<string, string>
                    {
                        ["Data"] = Path.Combine("nested", "data"),
                    },
                    root));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"Eigenverft.NetLib.Infrastructure.Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best effort.
        }
    }
}
