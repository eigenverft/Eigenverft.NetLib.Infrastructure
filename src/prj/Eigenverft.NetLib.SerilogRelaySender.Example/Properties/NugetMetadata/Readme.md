# Eigenverft.NetLib.SerilogRelaySender.Example

This sender is one half of a localhost demo. The sender uses `Eigenverft.NetLib.SerilogRelay` from NuGet; the ASP.NET Core receiver uses `Eigenverft.WebLib.SerilogRelayReceiver` from NuGet and stores events in SQLite.

From the repository root, start the receiver:

```powershell
dotnet run --project src/prj/Eigenverft.NetLib.SerilogRelayReceiver.Example/Eigenverft.NetLib.SerilogRelayReceiver.Example.csproj
```

In another terminal, send an event:

```powershell
dotnet run --project src/prj/Eigenverft.NetLib.SerilogRelaySender.Example/Eigenverft.NetLib.SerilogRelaySender.Example.csproj -- "Hello from the example"
```

Inspect the stored message in the second terminal:

```powershell
(Invoke-RestMethod 'http://127.0.0.1:5217/demo/events')[0].renderMessage
```

The result contains `Hello from the example`. Both programs use loopback without authentication. These projects are examples and are not packed or published.

The receiver accepts up to 256 events per batch and acknowledges successful storage with HTTP 204. Receiver package `1.0.0.8` accepts the sender's additional `ApplicationVersion` field but does not persist or expose it.
