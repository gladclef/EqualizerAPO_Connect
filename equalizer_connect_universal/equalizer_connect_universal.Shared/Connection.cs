using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace equalizerapo_connect_universal
{
    public class Connection
    {
        #region constants

        // why 2048? because Arther C. Clark, that's why
        public const int APP_PORT = 2048;
        // used in testing with windows simple TCP/IP services
        public const int ECHO_PORT = 7;
        // used in testing with windows simple TCP/IP services
        public const int QOTD_PORT = 17;
        // measured in seconds
        public const int KEEP_ALIVE_TIMOUT = 1;
        public const double LISTENING_TIMOUT = 0.03;
        public const double SHORT_TIMOUT = 0.01;
        // indicates that a message was blocked for being a non-important message
        public const string MESSAGE_BLOCKED = "message blocked";

        #endregion

        #region fields

        private static Connection Instance;
        private SocketClient currentSocketClient;
        private CancellationToken cancelKeepAlive;
        private CancellationToken cancelMessageListener;
        private CancellationToken cancelShortMessageListener;
        private Queue<string> messageQueue;
        private long lastSendTime;

        #endregion

        #region properties


        #endregion

        #region event handlers

        public EventHandler MessageRecievedEvent { get; set; }
        public EventHandler DisconnectedEvent { get; set; }
        public EventHandler DisconnectMe { get; set; }

        #endregion

        #region public methods

        public Connection()
        {
            PrintLine();
            Init();
        }

        public Connection(String hostname, int port)
        {
            PrintLine();
            Init();
            Connect(hostname, port);
        }

        ~Connection()
        {
            PrintLine();
            Close();
        }

        public void Close()
        {
            PrintLine();
            Connection.Instance = null;
            try
            {
                EndConnection();
            }
            catch (UnauthorizedAccessException)
            {
                Deployment.Current.Dispatcher.BeginInvoke(() =>
                {
                    EndConnection();
                });
            }
        }

        private void Init()
        {
            PrintLine();
            messageQueue = new Queue<string>();
        }

        public string Connect(String hostname, int port)
        {
            PrintLine();
            if (currentSocketClient != null)
            {
                currentSocketClient.Close();
            }
            currentSocketClient = new SocketClient();

            string success = currentSocketClient.Connect(hostname, port);
            if (success == SocketClient.SUCCESS)
            {
                // handle incoming messages
                currentSocketClient.HandleIncomingMessages(
                    new SocketClient.SocketCallbackDelegate(SocketCallback));

                // create the listener
                KeepAliveTimer = new DispatcherTimer();
                KeepAliveTimer.Tick += new EventHandler(CheckAlive);
                KeepAliveTimer.Interval = new TimeSpan(0, 0, Connection.KEEP_ALIVE_TIMOUT);
                KeepAliveTimer.Start();

                // create the listener
                ListeningTimer = new DispatcherTimer();
                ListeningTimer.Tick += new EventHandler(GetMessage);
                ListeningTimer.Interval = new TimeSpan(0, 0, 0, 0,
                    Convert.ToInt32(Connection.LISTENING_TIMOUT * 1000));
                ListeningTimer.Start();

                // create the listener
                ShortTimer = new DispatcherTimer();
                ShortTimer.Tick += new EventHandler(GetMessage);
                ShortTimer.Interval = new TimeSpan(0, 0, 0, 0,
                    Convert.ToInt32(Connection.SHORT_TIMOUT * 1000));
            }
            else
            {
                currentSocketClient.Close();
                currentSocketClient = null;
            }
            
            return success;
        }

        public string Send(string data, bool important)
        {
            PrintLine();
            if (currentSocketClient == null) { };
            PrintLine();
            // check preconditions
            if (currentSocketClient == null)
            {
                PrintLine();
                return SocketClient.DISCONNECTED;
            }

            PrintLine();
            // check that there is room for a non-important message
            if (!important &&
                (DateTime.Now.Ticks - lastSendTime < SHORT_TIMOUT * 1000 * 10000 * 2))
            {
                return MESSAGE_BLOCKED;
            }

            PrintLine();
            // try to send, get success status
            string success = currentSocketClient.Send('%' + data);
            lastSendTime = DateTime.Now.Ticks;

            PrintLine();
            // message sending was NOT successful?
            if (success != SocketClient.SUCCESS)
            {
                if (success == SocketClient.DISCONNECTED)
                {
                    PrintLine();
                    // try reconnecting
                    string reconnectSuccess = currentSocketClient.Reconnect();

                    // reconnecting failed, disconnect
                    if (reconnectSuccess != SocketClient.SUCCESS)
                    {
                        DeferredDisconnected();
                    }
                }
            }
            else
            {
                //System.Diagnostics.Debug.WriteLine(">> " + data);
            }

            return success;
        }

        public string Receive()
        {
            PrintLine();
            if (currentSocketClient == null)
            {
                throw new InvalidOperationException("no connection established to SocketClient");
            }
            return currentSocketClient.Receive();
        }

        public string Receive(bool waitForTimeout)
        {
            PrintLine();
            // check preconditions
            if (currentSocketClient == null)
            {
                throw new InvalidOperationException("no connection established to SocketClient");
            }

            // try to receive
            string success = currentSocketClient.Receive(waitForTimeout);

            // message receiving was NOT successful?
            if (success != SocketClient.SUCCESS)
            {
                if (success == SocketClient.DISCONNECTED)
                {
                    DeferredDisconnected();
                }
            }

            return success;
        }

        public void SideDisconnect()
        {
            PrintLine();
            Disconnected();
        }

        #endregion

        #region public static methods

        public static Connection GetInstance() {
            PrintLine();
            if (Instance == null)
            {
                Instance = new Connection();
            }
            return Instance;
        }

        #endregion

        #region private methods

        private void CheckAlive(object sender, EventArgs e)
        {
            PrintLine();
            Send(SocketClient.KEEP_ALIVE, false);
        }

        public void GetMessage(object sender, EventArgs e)
        {
            PrintLine();
            if (messageQueue.Count == 0)
            {
                // no more messages, stop listening so quickly
                ShortTimer.Stop();
                return;
            }
            string message = messageQueue.Dequeue();

            if (message == SocketClient.KEEP_ALIVE)
            {
                // continue through the queue until a useful message is received
                GetMessage(sender, e);
            }
            else if (message == SocketClient.OPERATION_TIMEOUT ||
                message == SocketClient.UNINITIALIZED ||
                message == SocketClient.NO_MESSAGE)
            {
                // do nothing
            }
            else
            {
                if (MessageRecievedEvent != null)
                {
                    MessageRecievedEvent(this, new MessageReceivedEventArgs(message));
                    // start listening for messages more often
                    ShortTimer.Start();
                }
            }
        }

        private void Disconnected()
        {
            PrintLine();
            EndConnection();
            if (DisconnectedEvent != null)
            {
                DisconnectedEvent(this, EventArgs.Empty);
            }
        }

        private void DeferredDisconnected()
        {
            PrintLine();
            if (DisconnectMe != null)
            {
                DisconnectMe(this, EventArgs.Empty);
            }
            else
            {
                Disconnected();
            }
        }

        private void EndConnection()
        {
            PrintLine();
            if (ListeningTimer != null)
            {
                ListeningTimer.Stop();
                ListeningTimer = null;
            }
            if (ShortTimer != null)
            {
                ShortTimer.Stop();
                ShortTimer = null;
            }
            if (KeepAliveTimer != null)
            {
                KeepAliveTimer.Stop();
                KeepAliveTimer = null;
            }
            if (currentSocketClient != null)
            {
                currentSocketClient.Close();
                currentSocketClient = null;
            }
        }

        private void SocketCallback(object s, System.Net.Sockets.SocketAsyncEventArgs e)
        {
            PrintLine();
            // get ready for the next message
            if (currentSocketClient != null)
            {
                try
                {
                    currentSocketClient.HandleIncomingMessages(
                        new SocketClient.SocketCallbackDelegate(SocketCallback));
                }
                catch (ObjectDisposedException)
                {
                    // do something here?
                }
            }
            
            // receive and parse the message
            string message;
            if (e.SocketError == System.Net.Sockets.SocketError.Success)
            {
                // Retrieve the data from the buffer
                message = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                message = message.Trim('\0');
            }
            else
            {
                message = e.SocketError.ToString();
            }

            // parse messages
            string[] messages = message.Split(new char[] { '%' });

            foreach (string m in messages)
            {
                // determine if this message is either a disconnect or worthy message
                if (m.Length == 0)
                {
                    continue;
                }
                else if (m == SocketClient.CONNECTION_ABORTED)
                {
                    PrintLine();
                    // try reconnecting
                    string reconnectSuccess = currentSocketClient.Reconnect();

                    // reconnecting failed, disconnect
                    if (reconnectSuccess != SocketClient.SUCCESS)
                    {
                        DeferredDisconnected();
                    }
                }
                else if (m == SocketClient.CONNECTION_RESET)
                {
                    // do nothing?
                    System.Diagnostics.Debug.WriteLine(m);
                }
                else
                {
                    messageQueue.Enqueue(m);
                }
            }
        }
        
        #endregion

        #region classes

        public class MessageReceivedEventArgs : EventArgs
        {
            public string message;

            public MessageReceivedEventArgs(string message)
            {
                this.message = message;
            }
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":CX");
        }
    }
}
