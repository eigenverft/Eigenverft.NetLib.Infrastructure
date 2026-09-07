using System;

using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Tests.Security.Protection;

[TestClass]
public sealed class DpapiMachineProtectionTests
{
    [TestMethod]
    public void DpapiTransformsRoundTripWithoutJsonPersistenceFramingOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows DPAPI is available only on Windows.");
        }

        // Exercise the internal DPAPI machine-protection primitive through its public reversible-transform adapters.
        ReversibleStringTransform base64 = ReversibleStringTransforms.DpapiMachine;
        string base64Value = base64.Apply("dpapi-base64");
        Assert.IsFalse(base64Value.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base64.TryReverse(base64Value, out string base64Clear));
        Assert.AreEqual("dpapi-base64", base64Clear);

        ReversibleStringTransform base64Url = ReversibleStringTransforms.DpapiMachineBase64Url;
        string base64UrlValue = base64Url.Apply("dpapi-base64url");
        Assert.IsFalse(base64UrlValue.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base64Url.TryReverse(base64UrlValue, out string base64UrlClear));
        Assert.AreEqual("dpapi-base64url", base64UrlClear);
    }
}
