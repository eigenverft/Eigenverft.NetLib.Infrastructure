using System.Reflection;

namespace Eigenverft.NetLib.Infra.Tests;

[TestClass]
public sealed class ProjectScaffoldTests
{
    [TestMethod]
    public void LibraryAssemblyCanBeLoaded()
    {
        Assembly assembly = Assembly.Load("Eigenverft.NetLib.Infra");

        Assert.AreEqual("Eigenverft.NetLib.Infra", assembly.GetName().Name);
    }
}
