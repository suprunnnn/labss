using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq; // Додано для .Concat().ToArray()

namespace EchoServer
{
    public class EchoServer
    {
        private readonly int _port;
        private TcpListener? _listener; // <--- ВИПРАВЛЕНО 1
        private readonly CancellationTokenSource _cancellationTokenSource;

        //constuctor
        public EchoServer(int port)
        {
            _port = port;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Console.WriteLine($"Server started on port {_port}.");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Припускаємо, що _listener не буде null, оскільки він ініціалізований вище
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine("Client connected.");

                    _ = Task.Run(() => HandleClientAsync(client, _cancellationTokenSource.Token));
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been closed
                    break;
                }
                catch (NullReferenceException)
                {
                    // _listener був null (хоча за логікою не повинен)
                    break;
                }
            }

            Console.WriteLine("Server shutdown.");
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;

                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        // Echo back the received message
                        await stream.WriteAsync(buffer, 0, bytesRead, token);
                        Console.WriteLine($"Echoed {bytesRead} bytes to the client.");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                finally
                {
                    client.Close();
                    Console.WriteLine("Client disconnected.");
                }
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _listener?.Stop(); // <--- ВИПРАВЛЕНО 2 (додано null-check)
            _cancellationTokenSource.Dispose();
            Console.WriteLine("Server stopped.");
        }

        public static async Task Main(string[] args)
        {
            EchoServer server = new EchoServer(5000);

            // Start the server in a separate task
            _ = Task.Run(() => server.StartAsync());

            string host = "127.0.0.1"; // Target IP
            int port = 60000;          // Target Port
            int intervalMilliseconds = 5000; // Send every 5 seconds

            using (var sender = new UdpTimedSender(host, port))
            {
                Console.WriteLine("Starting sender...");
                sender.StartSending(intervalMilliseconds);

                Console.WriteLine("Press 'q' to quit...");
                while (Console.ReadKey(intercept: true).Key != ConsoleKey.Q)
                {
                    // Just wait until 'q' is pressed
                }

                sender.StopSending();
                server.Stop();
                Console.WriteLine("Sender and server stopped.");
            }
        }
    }

    public class UdpTimedSender : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly UdpClient _udpClient;
        private Timer? _timer; // <--- ВИПРАВЛЕНО 3
        
        // Покращення: Ініціалізуємо Random один раз,
        // щоб уникнути однакових значень при швидких викликах
        private readonly Random _rnd = new Random();
        private ushort i = 0;

        public UdpTimedSender(string host, int port)
        {
            _host = host;
            _port = port;
            _udpClient = new UdpClient();
            // _timer тут не ініціалізується, тому він має бути nullable
        }

        public void StartSending(int intervalMilliseconds)
        {
            if (_timer != null)
                throw new InvalidOperationException("Sender is already running.");

            _timer = new Timer(SendMessageCallback, null, 0, intervalMilliseconds);
        }

        private void SendMessageCallback(object? state) // state може бути null
        {
            try
            {
                //dummy data
                byte[] samples = new byte[1024];
                _rnd.NextBytes(samples); // Використовуємо _rnd
                i++;

                byte[] msg = (new byte[] { 0x04, 0x84 })
                    .Concat(BitConverter.GetBytes(i))
                    .Concat(samples)
                    .ToArray();
                
                var endpoint = new IPEndPoint(IPAddress.Parse(_host), _port);

                _udpClient.Send(msg, msg.Length, endpoint);
                Console.WriteLine($"Message {i} sent to {_host}:{_port} ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        public void StopSending()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            StopSending();
            _udpClient.Dispose();
        }
    }
}
