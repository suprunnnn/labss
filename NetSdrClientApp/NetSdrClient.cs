using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using System;
using System.Collections.Generic;
using System.IO; // Додано для FileStream та BinaryWriter
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;
//using static System.Runtime.InteropServices.JavaScript.JSType; // Цей using, ймовірно, не потрібен і був помилкою

// Припускаємо, що ці інтерфейси та класи існують в інших файлах
namespace NetSdrClientApp.Networking
{
    public interface ITcpClient
    {
        bool Connected { get; }
        event EventHandler<byte[]>? MessageReceived;
        void Connect();
        void Disconnect();
        Task SendMessageAsync(byte[] msg);
    }

    public interface IUdpClient
    {
        event EventHandler<byte[]>? MessageReceived;
        Task StartListeningAsync();
        void StopListening();
    }
}

namespace NetSdrClientApp.Messages
{
    public class NetSdrMessageHelper
    {
        public enum MsgTypes { /* ... */ SetControlItem }
        public enum ControlItemCodes { /* ... */ IQOutputDataSampleRate, RFFilter, ADModes, ReceiverState, ReceiverFrequency }
        public static byte[] GetControlItemMessage(MsgTypes type, ControlItemCodes code, byte[] args) => new byte[0]; // Stub
        public static void TranslateMessage(byte[] e, out MsgTypes type, out ControlItemCodes code, out ushort sequenceNum, out byte[] body) // Stub
        {
            type = MsgTypes.SetControlItem;
            code = ControlItemCodes.ReceiverState;
            sequenceNum = 0;
            body = e;
        }
        public static IEnumerable<short> GetSamples(int bits, byte[] body) => new short[0]; // Stub
    }
}
// Кінець припущень

namespace NetSdrClientApp
{
    public class NetSdrClient
    {
        private ITcpClient _tcpClient;
        private IUdpClient _udpClient;

        public bool IQStarted { get; set; }

        public NetSdrClient(ITcpClient tcpClient, IUdpClient udpClient)
        {
            _tcpClient = tcpClient;
            _udpClient = udpClient;

            _tcpClient.MessageReceived += _tcpClient_MessageReceived;
            _udpClient.MessageReceived += _udpClient_MessageReceived;
        }

        public async Task ConnectAsync()
        {
            if (!_tcpClient.Connected)
            {
                _tcpClient.Connect();

                var sampleRate = BitConverter.GetBytes((long)100000).Take(5).ToArray();
                var automaticFilterMode = BitConverter.GetBytes((ushort)0).ToArray();
                var adMode = new byte[] { 0x00, 0x03 };

                //Host pre setup
                var msgs = new List<byte[]>
                {
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.IQOutputDataSampleRate, sampleRate),
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.RFFilter, automaticFilterMode),
                    NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ADModes, adMode),
                };

                foreach (var msg in msgs)
                {
                    await SendTcpRequest(msg);
                }
            }
        }

        public void Disconect()
        {
            _tcpClient.Disconnect();
        }

        public async Task StartIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var iqDataMode = (byte)0x80;
            var start = (byte)0x02;
            var fifo16bitCaptureMode = (byte)0x01;
            var n = (byte)1;

            var args = new[] { iqDataMode, start, fifo16bitCaptureMode, n };

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);
            
            await SendTcpRequest(msg);

            IQStarted = true;

            _ = _udpClient.StartListeningAsync();
        }

        public async Task StopIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var stop = (byte)0x01;
            var args = new byte[] { 0, stop, 0, 0 };

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);

            await SendTcpRequest(msg);

            IQStarted = false;

            _udpClient.StopListening();
        }

        public async Task ChangeFrequencyAsync(long hz, int channel)
        {
            var channelArg = (byte)channel;
            var frequencyArg = BitConverter.GetBytes(hz).Take(5);
            var args = new[] { channelArg }.Concat(frequencyArg).ToArray();

            var msg = NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverFrequency, args);

            await SendTcpRequest(msg);
        }

        private void _udpClient_MessageReceived(object? sender, byte[] e)
        {
            NetSdrMessageHelper.TranslateMessage(e, out MsgTypes type, out ControlItemCodes code, out ushort sequenceNum, out byte[] body);
            var samples = NetSdrMessageHelper.GetSamples(16, body);

            Console.WriteLine($"Samples recieved: " + body.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));

            using (FileStream fs = new FileStream("samples.bin", FileMode.Append, FileAccess.Write, FileShare.Read))
            using (BinaryWriter sw = new BinaryWriter(fs))
            {
                foreach (var sample in samples)
                {
                    sw.Write((short)sample); //write 16 bit per sample as configured 
                }
            }
        }

        // --- ВИПРАВЛЕНО ТУТ (додано ?) ---
        private TaskCompletionSource<byte[]>? responseTaskSource;

        // --- І ВИПРАВЛЕНО ТУТ (додано ? до Task<byte[]>) ---
        private async Task<byte[]?> SendTcpRequest(byte[] msg)
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return null; // Тепер це дозволено, оскільки Task<byte[]?> може повернути null
            }

            responseTaskSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responseTask = responseTaskSource.Task;

            await _tcpClient.SendMessageAsync(msg);

            var resp = await responseTask;

            return resp;
        }

        private void _tcpClient_MessageReceived(object? sender, byte[] e)
        {
            //TODO: add Unsolicited messages handling here
            if (responseTaskSource != null)
            {
                responseTaskSource.SetResult(e);
                responseTaskSource = null; // Тепер це дозволено, оскільки поле responseTaskSource є nullable
            }
            Console.WriteLine("Response recieved: " + e.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
        }
    }
}
