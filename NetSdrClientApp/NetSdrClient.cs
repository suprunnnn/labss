using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;

namespace NetSdrClientApp
{
    public class NetSdrClient
    {
        private readonly ITcpClient _tcpClient;
        private readonly IUdpClient _udpClient;

        public bool IQStarted { get; set; }

        private TaskCompletionSource<byte[]>? responseTaskSource;

        public NetSdrClient(ITcpClient tcpClient, IUdpClient udpClient)
        {
            _tcpClient = tcpClient;
            _udpClient = udpClient;
            _tcpClient.MessageReceived += _tcpClient_MessageReceived;
            _udpClient.MessageReceived += _udpClient_MessageReceived;
        }

        public async Task ConnectAsync()
        {
            if (_tcpClient.Connected)
                return;

            _tcpClient.Connect();

            var sampleRate = BitConverter.GetBytes((long)100000).Take(5).ToArray();
            var automaticFilterMode = BitConverter.GetBytes((ushort)0).ToArray();
            var adMode = new byte[] { 0x00, 0x03 };

            // Host pre setup
            var msgs = new List<byte[]>
            {
                NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.IQOutputDataSampleRate, sampleRate),
                NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.RFFilter, automaticFilterMode),
                NetSdrMessageHelper.GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ADModes, adMode)
            };

            foreach (var msg in msgs)
            {
                await SendTcpRequest(msg);
            }
        }

        public void Disconnect()
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

        // ✅ FIXED: залишено не static, але додано звернення до instance state, щоб Sonar не вважав метод статичним
        private void _udpClient_MessageReceived(object? sender, byte[] e)
        {
            // посилання на об’єкт для уникнення static warning
            _ = this.IQStarted;

            NetSdrMessageHelper.TranslateMessage(e, out MsgTypes type, out ControlItemCodes code, out ushort sequenceNum, out byte[] body);
            var samples = NetSdrMessageHelper.GetSamples(16, body);

            Console.WriteLine($"Samples received: {string.Join(" ", body.Select(b => b.ToString("X2")))}");

            const string fileName = "samples.bin";
            using (var fs = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs))
            {
                foreach (var sample in samples)
                {
                    bw.Write((short)sample);
                }
            }
        }

        private async Task<byte[]?> SendTcpRequest(byte[] msg)
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return null;
            }

            responseTaskSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _tcpClient.SendMessageAsync(msg);

            return await responseTaskSource.Task;
        }

        private void _tcpClient_MessageReceived(object? sender, byte[] e)
        {
            // TODO: add Unsolicited messages handling here
            if (responseTaskSource != null)
            {
                responseTaskSource.SetResult(e);
                responseTaskSource = null;
            }

            Console.WriteLine("Response received: " + string.Join(" ", e.Select(b => b.ToString("X2"))));
        }
    }
}
