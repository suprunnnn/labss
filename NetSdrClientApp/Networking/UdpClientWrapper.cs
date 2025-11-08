using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class UdpClientWrapper : IUdpClient, IEquatable<UdpClientWrapper>
{
    private readonly IPEndPoint _localEndPoint;
    private CancellationTokenSource? _cts;
    private UdpClient? _udpClient;

    public event EventHandler<byte[]>? MessageReceived;

    public UdpClientWrapper(int port)
    {
        _localEndPoint = new IPEndPoint(IPAddress.Any, port);
    }

    public async Task StartListeningAsync()
    {
        _cts = new CancellationTokenSource();
        Console.WriteLine("Start listening for UDP messages...");

        try
        {
            _udpClient = new UdpClient(_localEndPoint);
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                MessageReceived?.Invoke(this, result.Buffer);

                Console.WriteLine($"Received from {result.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException ex)
        {
            //empty
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving message: {ex.Message}");
        }
    }

    public void StopListening()
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            Console.WriteLine("Stopped listening for UDP messages.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping: {ex.Message}");
        }
    }

    public void Exit()
    {
        try
        {
            _cts?.Cancel();
            _udpClient?.Close();
            Console.WriteLine("Stopped listening for UDP messages.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping: {ex.Message}");
        }
    }

    public override int GetHashCode()
    {
        var payload = $"{nameof(UdpClientWrapper)}|{_localEndPoint.Address}|{_localEndPoint.Port}";

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(payload));

        return BitConverter.ToInt32(hash, 0);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as UdpClientWrapper);
    }

    public bool Equals(UdpClientWrapper? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return _localEndPoint.Equals(other._localEndPoint);
    }

    public static bool operator ==(UdpClientWrapper? left, UdpClientWrapper? right)
    {
        if (left is null)
        {
            return right is null;
        }
        return left.Equals(right);
    }

    public static bool operator !=(UdpClientWrapper? left, UdpClientWrapper? right)
    {
        return !(left == right);
    }
}
