using System.Diagnostics.CodeAnalysis;

#if NETSTANDARD2_1
using RelayHttpHandler = System.Net.Http.StandardSocketsHttpHandler;
#else
using RelayHttpHandler = System.Net.Http.SocketsHttpHandler;
#endif

namespace Eigenverft.NetLib.SerilogRelay
{
    public partial class SerilogRelaySink
    {
        private static readonly HappyEyeballsConnector _connector = new HappyEyeballsConnector();
        private static readonly RelayHttpHandler _defaultHttpHandler = CreateHttpHandler(false);
        private static readonly RelayHttpHandler _dangerousHttpHandler = CreateHttpHandler(true);

        internal static RelayHttpHandler GetHttpClientHandler(bool dangerousAcceptAnyServerCertificate)
            => dangerousAcceptAnyServerCertificate ? _dangerousHttpHandler : _defaultHttpHandler;

        [SuppressMessage("Security", "CA5359", Justification = "Certificate bypass requires the existing explicit dangerous opt-in; default handlers retain platform validation.")]
        private static RelayHttpHandler CreateHttpHandler(bool dangerousAcceptAnyServerCertificate)
        {
            var handler = new RelayHttpHandler
            {
                UseCookies = false,
#if NETSTANDARD2_1
                // The backport does not discover OS proxy settings itself.
                Proxy = System.Net.WebRequest.GetSystemWebProxy(),
                ConnectCallback = (context, token) => _connector.ConnectAsync(context.DnsEndPoint, token),
#else
                ConnectCallback = (context, token) => new System.Threading.Tasks.ValueTask<System.IO.Stream>(_connector.ConnectAsync(context.DnsEndPoint, token)),
#endif
            };
            if (dangerousAcceptAnyServerCertificate)
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            return handler;
        }
    }
}
