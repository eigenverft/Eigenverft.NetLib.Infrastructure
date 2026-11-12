using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Eigenverft.NetLib.SerilogRelay
{
    internal sealed class HappyEyeballsConnector
    {
        // RFC 8305's default leaves most of the relay's two-second request budget for delivery.
        internal static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(250);

        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
        private readonly Func<IPAddress, int, CancellationToken, Task<Stream>> _connect;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        internal HappyEyeballsConnector()
            : this(ResolveAsync, ConnectSocketAsync, Task.Delay) { }

        internal HappyEyeballsConnector(
            Func<string, CancellationToken, Task<IPAddress[]>> resolve,
            Func<IPAddress, int, CancellationToken, Task<Stream>> connect,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _resolve = resolve;
            _connect = connect;
            _delay = delay;
        }

        internal async Task<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
        {
            IPAddress[] addresses = Interleave(await _resolve(endpoint.Host, cancellationToken).ConfigureAwait(false));
            using var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = attemptsCancellation.Token;
            Task canceled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            var pending = new List<Task<Stream>>();
            Task nextAttempt = Task.CompletedTask;
            int next = 0;
            Exception lastError = new SocketException((int)SocketError.HostNotFound);
            try
            {
                while (next < addresses.Length || pending.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (next < addresses.Length && (pending.Count == 0 || nextAttempt.IsCompleted))
                    {
                        pending.Add(_connect(addresses[next++], endpoint.Port, token));
                        nextAttempt = next < addresses.Length ? _delay(AttemptDelay, token) : canceled;
                    }

                    Task completed = await Task.WhenAny(pending.Cast<Task>().Concat(new[] { nextAttempt, canceled })).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (completed == nextAttempt)
                        continue;

                    var attempt = (Task<Stream>)completed;
                    pending.Remove(attempt);
                    try
                    {
                        return await attempt.ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        lastError = error;
                        // A failed connect needs no head start; immediately try the next address.
                        nextAttempt = next < addresses.Length ? Task.CompletedTask : canceled;
                    }
                }

                throw lastError;
            }
            finally
            {
                attemptsCancellation.Cancel();
                foreach (Task<Stream> attempt in pending)
                    _ = DisposeLosingConnectionAsync(attempt);
            }
        }

        internal static IPAddress[] Interleave(IPAddress[] addresses)
        {
            if (addresses.Length == 0)
                return addresses;

            // Mapped IPv4 addresses cannot connect through an IPv6-only socket.
            IPAddress[] distinct = addresses.Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).Distinct().ToArray();
            AddressFamily preferredFamily = distinct[0].AddressFamily;
            var first = new Queue<IPAddress>(distinct.Where(address => address.AddressFamily == preferredFamily));
            var second = new Queue<IPAddress>(distinct.Where(address => address.AddressFamily != preferredFamily));
            var ordered = new List<IPAddress>(distinct.Length);
            while (first.Count > 0 || second.Count > 0)
            {
                if (first.Count > 0)
                    ordered.Add(first.Dequeue());
                if (second.Count > 0)
                    ordered.Add(second.Dequeue());
            }
            return ordered.ToArray();
        }

        internal static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
#if NETSTANDARD2_1
            return await Dns.GetHostAddressesAsync(host).WaitAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, token).ConfigureAwait(false);
#else
            return await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
#endif
        }

        internal static async Task<Stream> ConnectSocketAsync(IPAddress address, int port, CancellationToken token)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using CancellationTokenRegistration registration = token.Register(socket.Dispose);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task DisposeLosingConnectionAsync(Task<Stream> attempt)
        {
            try
            {
                (await attempt.ConfigureAwait(false)).Dispose();
            }
            catch (Exception)
            {
                // Observe failed/canceled losers, including a connect completing after cancellation.
            }
        }
    }
}
