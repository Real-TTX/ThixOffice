using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ThixMail.Core.Accounts;

/// <summary>A service record (RFC 2782): where a service of a domain runs.</summary>
public sealed record SrvRecord(int Priority, int Weight, int Port, string Target);

/// <summary>
/// The two DNS questions the auto-configuration asks – SRV (RFC 6186: _imaps._tcp, _submission._tcp …) and MX (the provider behind a domain) – with a
/// small DNS client over UDP to the system's DNS servers (.NET has no SRV lookup of its own). Three seconds per server; nothing found is an empty answer,
/// never an exception. Tests answer with <see cref="ResolverForTests"/>.
/// </summary>
public static class DnsLookup
{
    private const ushort TypeMx = 15;
    private const ushort TypeSrv = 33;

    /// <summary>Tests: (name, type) → the answer records as text ("10 mx.example.com" / "0 1 993 imap.example.com").</summary>
    internal static Func<string, string, IReadOnlyList<string>>? ResolverForTests { get; set; }

    public static async Task<IReadOnlyList<SrvRecord>> SrvAsync(string name, CancellationToken cancellationToken)
    {
        if (ResolverForTests is { } resolver)
        {
            return [.. resolver(name, "SRV").Select(line => line.Split(' ')).Where(p => p.Length == 4)
                .Select(p => new SrvRecord(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), p[3].TrimEnd('.')))];
        }

        var answers = await QueryAsync(name, TypeSrv, cancellationToken);
        return [.. answers.OfType<SrvRecord>().Where(r => r.Target is not ("" or ".")).OrderBy(r => r.Priority).ThenByDescending(r => r.Weight)];
    }

    public static async Task<IReadOnlyList<string>> MxAsync(string domain, CancellationToken cancellationToken)
    {
        if (ResolverForTests is { } resolver)
        {
            return [.. resolver(domain, "MX").Select(line => line.Split(' ')).Where(p => p.Length == 2).OrderBy(p => int.Parse(p[0])).Select(p => p[1].TrimEnd('.'))];
        }

        var answers = await QueryAsync(domain, TypeMx, cancellationToken);
        return [.. answers.OfType<(int Preference, string Host)>().OrderBy(a => a.Preference).Select(a => a.Host)];
    }

    private static async Task<IReadOnlyList<object>> QueryAsync(string name, ushort type, CancellationToken cancellationToken)
    {
        foreach (var server in Servers())
        {
            try
            {
                using var client = new UdpClient(server.AddressFamily);
                var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
                var question = BuildQuery(id, name, type);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await client.SendAsync(question, new IPEndPoint(server, 53), timeout.Token);
                var response = await client.ReceiveAsync(timeout.Token);
                var records = Parse(response.Buffer, id, type);
                if (records is not null)
                {
                    return records;
                }
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException or IndexOutOfRangeException or ArgumentException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        return [];
    }

    private static IEnumerable<IPAddress> Servers()
    {
        var servers = new List<IPAddress>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up))
            {
                servers.AddRange(adapter.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork || !a.IsIPv6SiteLocal));
            }
        }
        catch (NetworkInformationException)
        {
        }

        return servers.Distinct().Take(4);
    }

    private static byte[] BuildQuery(ushort id, string name, ushort type)
    {
        var bytes = new List<byte>();
        bytes.AddRange([(byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0]); // recursion desired, one question
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var ascii = Encoding.ASCII.GetBytes(new IdnMapping().GetAscii(label));
            bytes.Add((byte)ascii.Length);
            bytes.AddRange(ascii);
        }

        bytes.Add(0);
        bytes.AddRange([(byte)(type >> 8), (byte)type, 0, 1]); // class IN
        return [.. bytes];
    }

    /// <summary>The answers of a response (null when it is not the answer to our question).</summary>
    private static List<object>? Parse(byte[] message, ushort id, ushort type)
    {
        if (message.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(message) != id)
        {
            return null;
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(4));
        var answers = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(6));
        var offset = 12;
        for (var index = 0; index < questions; index++)
        {
            ReadName(message, ref offset);
            offset += 4;
        }

        var records = new List<object>();
        for (var index = 0; index < answers; index++)
        {
            ReadName(message, ref offset);
            var recordType = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset));
            var length = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset + 8));
            var data = offset + 10;
            if (recordType == type && type == TypeSrv)
            {
                var at = data + 6;
                records.Add(new SrvRecord(BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(data)), BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(data + 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(data + 4)), ReadName(message, ref at)));
            }
            else if (recordType == type && type == TypeMx)
            {
                var at = data + 2;
                records.Add(((int)BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(data)), ReadName(message, ref at)));
            }

            offset = data + length;
        }

        return records;
    }

    private static string ReadName(byte[] message, ref int offset)
    {
        var labels = new List<string>();
        var position = offset;
        var jumped = false;
        for (var guard = 0; guard < 64; guard++)
        {
            var length = message[position];
            if (length == 0)
            {
                position++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                var pointer = ((length & 0x3F) << 8) | message[position + 1];
                if (!jumped)
                {
                    offset = position + 2;
                }

                jumped = true;
                position = pointer;
                continue;
            }

            labels.Add(Encoding.ASCII.GetString(message, position + 1, length));
            position += length + 1;
        }

        if (!jumped)
        {
            offset = position;
        }

        return string.Join('.', labels);
    }
}
