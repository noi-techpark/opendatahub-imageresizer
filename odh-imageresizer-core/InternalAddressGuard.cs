// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace odh_imageresizer_core
{
    // Prevents the proxy endpoints from reaching internal services (cluster, localhost, cloud metadata).
    // The check runs on the resolved IP address when connecting, so DNS tricks and redirects can't get around it.
    public static class InternalAddressGuard
    {
        public static SocketsHttpHandler CreateHandler(bool allowAutoRedirect) => new()
        {
            AllowAutoRedirect = allowAutoRedirect,
            // Cookies of one user must not be kept in the shared handler and sent for other users
            UseCookies = false,
            ConnectCallback = ConnectAsync
        };

        private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var host = context.DnsEndPoint.Host;
            var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken)).Where(IsPublic).ToArray();
            if (addresses.Length == 0)
                throw new HttpRequestException($"Requests to internal addresses are not allowed ({host})");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return !(b[0] == 0                                  // 0.0.0.0/8 "this network"
                    || b[0] == 10                                   // 10.0.0.0/8 private
                    || (b[0] == 100 && b[1] >= 64 && b[1] < 128)    // 100.64.0.0/10 carrier grade NAT
                    || b[0] == 127                                  // 127.0.0.0/8 loopback
                    || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link local, cloud metadata
                    || (b[0] == 172 && b[1] >= 16 && b[1] < 32)     // 172.16.0.0/12 private
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // 192.0.0.0/24 IETF protocol assignments
                    || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16 private
                    || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // 198.18.0.0/15 benchmarking
                    || b[0] >= 224);                                // multicast, reserved, broadcast
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = address.GetAddressBytes();
                return !(IPAddress.IsLoopback(address)
                    || address.Equals(IPAddress.IPv6None)
                    || address.IsIPv6LinkLocal
                    || address.IsIPv6SiteLocal
                    || address.IsIPv6UniqueLocal
                    || address.IsIPv6Multicast
                    || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b));  // 64:ff9b::/32 NAT64, could map to internal IPv4
            }

            return false;
        }
    }
}
