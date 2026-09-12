using System;
using System.IO;

using Eigenverft.NetLib.Configuration.Values;
using Eigenverft.NetLib.Transformations;
using Eigenverft.NetLib.Security.DataProtection.Values;
using Eigenverft.NetLib.Security.DataProtection.Transformations;

namespace Eigenverft.NetLib.Security.DataProtection.Tests;

[TestClass]
public sealed class DataProtectionAdapterTests
{
    [TestMethod]
    public void TransformRoundTripsWithTheSamePersistentKeyRingAndIsolationContext()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            ReversibleStringTransform writer = AspNetDataProtectionStringTransforms.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "configuration-values");
            string protectedValue = writer.Apply("persisted secret");

            ReversibleStringTransform reader = AspNetDataProtectionStringTransforms.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "configuration-values");

            Assert.IsTrue(reader.TryReverse(protectedValue, out string clearText));
            Assert.AreEqual("persisted secret", clearText);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public void TransformRejectsAValueCreatedForAnotherPurpose()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            ReversibleStringTransform writer = AspNetDataProtectionStringTransforms.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "purpose-a");
            string protectedValue = writer.Apply("persisted secret");

            ReversibleStringTransform wrongPurpose = AspNetDataProtectionStringTransforms.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "purpose-b");

            Assert.IsFalse(wrongPurpose.TryReverse(protectedValue, out string unchanged));
            Assert.AreEqual(protectedValue, unchanged);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [TestMethod]
    public void ConfigurationCodecRoundTripsAndRejectsWrongPurpose()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            ConfigurationValueCodec writer = AspNetDataProtectionConfigurationValueCodecs.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "configuration-values");
            string encoded = writer.Encode("persisted secret");

            Assert.IsTrue(writer.TryDecode(encoded, out string clearText));
            Assert.AreEqual("persisted secret", clearText);

            ConfigurationValueCodec wrongPurpose = AspNetDataProtectionConfigurationValueCodecs.DataProtection(
                directory,
                "DataProtectionAdapterTests",
                "another-purpose");

            Assert.IsFalse(wrongPurpose.TryDecode(encoded, out string unchanged));
            Assert.AreEqual(encoded, unchanged);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Eigenverft.NetLib.Security.DataProtection.Tests.Values",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
