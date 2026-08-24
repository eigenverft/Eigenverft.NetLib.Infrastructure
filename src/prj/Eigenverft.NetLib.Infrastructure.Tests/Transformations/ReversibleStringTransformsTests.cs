using System;
using System.Text;

using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Tests.Transformations;

[TestClass]
public sealed class ReversibleStringTransformsTests
{
    [TestMethod]
    public void ReversibleStringTransformsRoundTripWithoutJsonPersistenceFraming()
    {
        ReversibleStringTransform base64 = ReversibleStringTransforms.Base64;
        Assert.AreEqual("aGVsbG8=", base64.Apply("hello"));
        Assert.IsTrue(base64.TryReverse("aGVsbG8=", out string base64Clear));
        Assert.AreEqual("hello", base64Clear);

        ReversibleStringTransform base92 = ReversibleStringTransforms.Base92JsonSafe;
        string base92Value = base92.Apply("hello");
        Assert.IsFalse(base92Value.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base92.TryReverse(base92Value, out string base92Clear));
        Assert.AreEqual("hello", base92Clear);

        Assert.AreEqual("Uryyb", ReversibleStringTransforms.Rot13.Apply("Hello"));
        Assert.IsTrue(ReversibleStringTransforms.Rot13.TryReverse("Uryyb", out string rot13Clear));
        Assert.AreEqual("Hello", rot13Clear);

        ReversibleStringTransform caesar = ReversibleStringTransforms.Caesar(5);
        string caesarValue = caesar.Apply("Hello");
        Assert.AreEqual("5:Mjqqt", caesarValue);
        Assert.IsTrue(caesar.TryReverse(caesarValue, out string caesarClear));
        Assert.AreEqual("Hello", caesarClear);
        Assert.IsFalse(ReversibleStringTransforms.Caesar(4).TryReverse(caesarValue, out string mismatchedCaesar));
        Assert.AreEqual(caesarValue, mismatchedCaesar);

        ReversibleStringTransform aes = ReversibleStringTransforms.AesPassword("transform-password");
        string aesValue = aes.Apply("sensitive-value");
        StringAssert.StartsWith(aesValue, "v1.");
        Assert.IsFalse(aesValue.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(aes.TryReverse(aesValue, out string aesClear));
        Assert.AreEqual("sensitive-value", aesClear);
        Assert.IsFalse(
            ReversibleStringTransforms.AesPassword("wrong-password").TryReverse(aesValue, out string failedAes));
        Assert.AreEqual(aesValue, failedAes);

        ReversibleStringTransform composed = ReversibleStringTransforms.Compose(
            ReversibleStringTransforms.Rot13,
            ReversibleStringTransforms.Base64);
        string composedValue = composed.Apply("Hello");
        Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes("Uryyb")), composedValue);
        Assert.IsTrue(composed.TryReverse(composedValue, out string composedClear));
        Assert.AreEqual("Hello", composedClear);
    }

    [TestMethod]
    public void MachineBoundTransformRoundTripsWithoutJsonPersistenceFraming()
    {
        ReversibleStringTransform transform = ReversibleStringTransforms.PhysicalMachineBoundAes();
        string transformed = transform.Apply("machine-bound-value");

        Assert.IsFalse(transformed.StartsWith("enc:", StringComparison.Ordinal));
        StringAssert.StartsWith(transformed, "v1.");
        Assert.IsTrue(transform.TryReverse(transformed, out string clearText));
        Assert.AreEqual("machine-bound-value", clearText);
    }
}
