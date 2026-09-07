using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Transformations;

namespace Eigenverft.NetLib.Infrastructure.Tests.Hosting.Configuration.Values
{
    [TestClass]
    public sealed class ConfigurationValueCodecTests
    {
        [TestMethod]
        public void BuiltInCodecsPreservePersistedWrapperTokens()
        {
            Assert.AreEqual(
                "enc:q7m2n4:" + ReversibleStringTransforms.Base64.Apply("hello"),
                ConfigurationValueCodecs.Base64.Encode("hello"));
            Assert.AreEqual(
                "enc:r1t3o7:" + ReversibleStringTransforms.Rot13.Apply("Hello"),
                ConfigurationValueCodecs.Rot13.Encode("Hello"));
            Assert.AreEqual(
                "enc:c4e5s2:" + ReversibleStringTransforms.Caesar(5).Apply("Hello"),
                ConfigurationValueCodecs.Caesar(5).Encode("Hello"));
            Assert.AreEqual(
                "enc:b9j2s7:" + ReversibleStringTransforms.Base92JsonSafe.Apply("hello"),
                ConfigurationValueCodecs.Base92JsonSafe.Encode("hello"));
        }

        [TestMethod]
        public void AesPasswordCodecRoundTrips()
        {
            ConfigurationValueCodec codec = ConfigurationValueCodecs.AesPassword("test-only-password");
            string encoded = codec.Encode("secret");

            Assert.IsTrue(codec.TryDecode(encoded, out string clearText));
            Assert.AreEqual("secret", clearText);
        }

        [TestMethod]
        public void AesPasswordByteRepresentationUsesSameContext()
        {
            byte[] passwordBytes = { 0x68, 0x65, 0x6C, 0x6C, 0x6F };
            ConfigurationValueCodec stringCodec = ConfigurationValueCodecs.AesPassword("hello");
            ConfigurationValueCodec byteCodec = ConfigurationValueCodecs.AesPassword(passwordBytes);

            string encodedByString = stringCodec.Encode("first");
            string encodedByBytes = byteCodec.Encode("second");

            Assert.IsTrue(byteCodec.TryDecode(encodedByString, out string first));
            Assert.AreEqual("first", first);
            Assert.IsTrue(stringCodec.TryDecode(encodedByBytes, out string second));
            Assert.AreEqual("second", second);
        }

        [TestMethod]
        public void ComposedCodecRollsBackWhenInnerContextDoesNotMatch()
        {
            ConfigurationValueCodec writeCodec = ConfigurationValueCodecs.Compose(
                ConfigurationValueCodecs.AesPassword("correct-password"),
                ConfigurationValueCodecs.Base64);
            ConfigurationValueCodec wrongReadCodec = ConfigurationValueCodecs.Compose(
                ConfigurationValueCodecs.AesPassword("wrong-password"),
                ConfigurationValueCodecs.Base64);

            string encoded = writeCodec.Encode("secret");

            Assert.IsFalse(wrongReadCodec.TryDecode(encoded, out string unchanged));
            Assert.AreEqual(encoded, unchanged);
            Assert.IsTrue(writeCodec.TryDecode(encoded, out string clearText));
            Assert.AreEqual("secret", clearText);
        }

        [TestMethod]
        public void ExternalTransformCanUseDataProtectionPersistedKindWithoutAspNetDependency()
        {
            var externalTransform = new ReversibleStringTransform(
                "ExternalDataProtectionAdapter",
                value => "protected:" + value,
                (string value, out string clearText) =>
                {
                    const string prefix = "protected:";
                    if (!value.StartsWith(prefix, System.StringComparison.Ordinal))
                    {
                        clearText = value;
                        return false;
                    }

                    clearText = value.Substring(prefix.Length);
                    return true;
                });
            var codec = new ConfigurationValueCodec(
                "DataProtection",
                ConfigurationValueKind.DataProtection,
                externalTransform);

            string encoded = codec.Encode("secret");

            StringAssert.StartsWith(encoded, "enc:d7p4r8:");
            Assert.IsTrue(codec.TryDecode(encoded, out string clearText));
            Assert.AreEqual("secret", clearText);
        }
    }
}
