using System.Reflection;

namespace Eigenverft.NetLib.Infrastructure.Tests;

[TestClass]
public sealed class ProjectScaffoldTests
{
    [TestMethod]
    public void LibraryAssemblyCanBeLoaded()
    {
        Assembly assembly = Assembly.Load("Eigenverft.NetLib.Infrastructure");

        Assert.AreEqual("Eigenverft.NetLib.Infrastructure", assembly.GetName().Name);
    }
}
