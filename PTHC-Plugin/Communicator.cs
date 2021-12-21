using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PTHC_Plugin
{

    public class Communicator
    {
        private static TcpClient? _client;
        private static NetworkStream? _stream;

        private static bool _running = true;

        private static readonly Encoding Encoding = Encoding.UTF8;

        public Communicator()
        {
            _client = new TcpClient();
        }

        public void Start()
        {
            _client?.Connect("127.0.0.1", 8989);
            _stream = _client?.GetStream();

            while (_running)
                try
                {
                    string line;
                    while ((line = ReadString()) != null) ParseAndHandleMessage(line);
                }
                catch (EndOfStreamException)
                {
                    _running = false;
                    _stream?.Close();
                    _client?.Close();
                    PthcPlugin.Instance?.OnCommunicatorDisconnect();
                    Thread.CurrentThread.Join();
                }
        }

        public static void EndConnection()
        {
            Console.WriteLine("Ending TCP Connection");
            _running = false;
            _stream?.Close();
            _client?.Close();
            PthcPlugin.Instance?.CommunicatorThread.Join();
        }

        public static void SendUserApprovalRequest(string userId, int playerIndex)
        {
            SendMessage(OutMessageTypes.REQUESTUSERAPPROVAL, new[] {userId, playerIndex.ToString()});
        }

        public static void AnnounceWinner(string discordId)
        {
            SendMessage(OutMessageTypes.ANNOUNCEWINNER, new[] {discordId});
        }

        private static void SendMessage(OutMessageTypes type, string[] args)
        {
            var fullMessage = type + ":";

            for (var i = 0; i < args.Length; i++)
            {
                fullMessage += args[i];
                if (i != args.Length - 1) fullMessage += ",";
            }

            WriteString(fullMessage + "\n");
            _stream?.Flush();
        }

        private static void ParseAndHandleMessage(string message)
        {
            var temp = message.Split(':');

            var rawType = temp[0];

            var type = (InMessageTypes) Enum.Parse(typeof(InMessageTypes), rawType);

            var args = temp[1].Split(',');

            switch (type)
            {
                case InMessageTypes.USERAPPROVALRESPONSE:
                    Handler.HandleUserApprovalResponse(args[0], int.Parse(args[1]));
                    break;
                case InMessageTypes.SETGRACETIME:
                    Handler.HandleSetGraceTime(int.Parse(args[0]));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void WriteString(string message)
        {
            var bytes = Encoding.GetBytes(message);
            _stream?.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString()
        {
            var bytes = new byte[1024];

            _stream?.Read(bytes, 0, bytes.Length);

            return Encoding.GetString(bytes);
        }

        private enum InMessageTypes
        {
            // ReSharper disable InconsistentNaming
            // ReSharper disable IdentifierTypo
            SETGRACETIME,
            USERAPPROVALRESPONSE
        }

        private enum OutMessageTypes
        {
            REQUESTUSERAPPROVAL,
            ANNOUNCEWINNER
        }
    }
}