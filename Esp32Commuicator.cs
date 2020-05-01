using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;

using Android.App;
using System.Threading.Tasks;
using Android.Net.Wifi;
using Com.Zepfiro.Android.Esptouch;
using System.Collections.Generic;
using System.Threading;
using System.Net.Http;

namespace SmartPlugAndroid
{
    public class Esp32Commuicator
    {
        const int UDPPORT = 2000;
        const int TCPPORT = 80;
        const string MAGICKEYNETWORK = "589234";

        const string APPID = "APP";

        string DISCOVERYCOMMAND = $"{MAGICKEYNETWORK}|{APPID}|Discover";

        private UdpClient udpClient = new UdpClient();
        private HttpClient httpClient = new HttpClient();

        private Task<bool> smartConfigTask;

        public (string, IPEndPoint) ActiveDevice { get; private set; }


        public Dictionary<string, IPEndPoint> RegisteredDevices { get; private set; } = new Dictionary<string, IPEndPoint>();


        public event Action<string> FeedbackCallback;


        public Esp32Commuicator()
        {
            httpClient.Timeout = new TimeSpan(0, 0, 3);

            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, UDPPORT));
            Task.Run(UdpReciever);
        }

        public bool SetActiveDevice(string name)
        {
            if (!RegisteredDevices.ContainsKey(name))
                return false;

            ActiveDevice = (name, RegisteredDevices[name]);

            return true;
        }
        public void ClearDevices()
        {
            RegisteredDevices.Clear();
            ActiveDevice = (null, null);
        }

        public byte[] SendCommandData(string command, byte[] data)
        {
            try
            {
                Uri uri = new Uri($"http://{ActiveDevice.Item2}/{command}");

                Debug.WriteLine("Sending data..");

                MultipartFormDataContent content = new MultipartFormDataContent();
                ByteArrayContent baContent = new ByteArrayContent(data);
                content.Add(baContent, "file", "file");

                var response = httpClient.PostAsync(uri, content).Result;

                var bytes = response.Content.ReadAsByteArrayAsync().Result;

                return bytes;
            }
            catch (Exception e)
            {
                FeedbackCallback($"Communication Error: {e.Message}");
                return new byte[0];
            }
        }
        public byte[] SendCommandData(string command, IEnumerable<(string, string)> data)
        {
            try
            {
                Uri uri = new Uri($"http://{ActiveDevice.Item2}/{command}");

                Debug.WriteLine("Sending data..");

                MultipartFormDataContent content = new MultipartFormDataContent();
                foreach (var (name, value) in data)
                {
                    System.Net.Http.StringContent sContent = new StringContent(value);
                    content.Add(sContent, name);
                }

                var response = httpClient.PostAsync(uri, content).Result;

                var bytes = response.Content.ReadAsByteArrayAsync().Result;

                return bytes;
            }
            catch (Exception e)
            {
                FeedbackCallback($"Communication Error: {e.Message}");
                return new byte[0];
            }
        }

        public string SendCommandDataParsed(string command, byte[] data)
        {
            return Encoding.UTF8.GetString(SendCommandData(command, data));
        }
        public string SendCommandDataParsed(string command, IEnumerable<(string, string)> data)
        {
            return Encoding.UTF8.GetString(SendCommandData(command, data));
        }

        public byte[] SendCommand(string command)
        {
            try
            {
                Uri uri = new Uri($"http://{ActiveDevice.Item2}/{command}");

                Debug.WriteLine($"Calling: {uri}");

                var response = httpClient.GetAsync(uri).Result;

                response.EnsureSuccessStatusCode();
                var bytes = response.Content.ReadAsByteArrayAsync().Result;

                Debug.WriteLine(Encoding.UTF8.GetString(bytes));

                return bytes;
            }
            catch (Exception e)
            {
                FeedbackCallback($"Communication Error: {e.Message}");
                return new byte[0];
            }
        }
        public string SendCommandParsed(string command)
        {
            return Encoding.UTF8.GetString(SendCommand(command));
        }

        public event Action<string> OnNewDeviceDiscovered;

        public void SendDiscoveryCommand()
        {
            RegisteredDevices.Clear();
            ActiveDevice = (null, null);
            var endpoint = new IPEndPoint(IPAddress.Broadcast, UDPPORT);
            var message = Encoding.UTF8.GetBytes(DISCOVERYCOMMAND);
            udpClient.Send(message, message.Length, endpoint);
        }

        private void UdpReciever()
        {
            var from = new IPEndPoint(0, 0);
            while (true)
            {
                var recvBuffer = udpClient.Receive(ref from);

                string msgstr = Encoding.UTF8.GetString(recvBuffer);

                Debug.WriteLine("Incoming message: " + msgstr);

                var parts = msgstr.Split('|');

                if (parts.Length != 3 || parts[0] != MAGICKEYNETWORK || parts[1] == APPID)
                    continue;

                var senderId = parts[1];
                var ipstr = parts[2];


                try
                {
                    var ip = new IPEndPoint(IPAddress.Parse(ipstr), TCPPORT);

                    if (!RegisteredDevices.ContainsKey(senderId) || RegisteredDevices[senderId] != ip)
                    {
                        RegisteredDevices[senderId] = ip;
                        Debug.WriteLine("Registered Esp32: " + senderId + " at " + ipstr);
                        FeedbackCallback($"Found device {senderId} at {ipstr}");
                        OnNewDeviceDiscovered.Invoke(senderId);
                    }
                    else
                    {
                        RegisteredDevices[senderId] = ip;
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Couldn't proces message. " + e.Message);
                }
            }
        }


        public Task<bool> SendSmartConfig(string ssid, string bssid, string password)
        {
            FeedbackCallback("Sending Wifi Credentials");

            if (smartConfigTask == null || smartConfigTask.IsCompleted)
                smartConfigTask = Task.Run(() =>
                {
                    try
                    {
                        IEsptouchTask mEsptouchTask = new EsptouchTask(ssid, bssid, password, true, 3000,
                                 Application.Context);

                        IEsptouchResult result = mEsptouchTask.ExecuteForResult();
                        if (result.IsSuc)
                        {
                            FeedbackCallback("Successfully sent Wifi Credentials");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        FeedbackCallback("Failed to send Wifi Credentials: " + ex.Message);
                        return false;
                    }

                    return false;
                });

            return smartConfigTask;
        }
    }
}