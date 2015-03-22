using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.UI.Xaml;

namespace equalizer_connect_universal
{
    public class Connection
    {
        #region constants

        /// <summary>
        /// why 2048? because Arther C. Clark, that's why
        /// </summary>
        public const int APP_PORT = 2048;
        /// <summary>
        /// used in testing with windows simple TCP/IP services
        /// </summary>
        public const int ECHO_PORT = 7;
        /// <summary>
        /// used in testing with windows simple TCP/IP services
        /// </summary>
        public const int QOTD_PORT = 17;
        /// <summary>
        /// how often to check for a keep-alive, measured in seconds
        /// </summary>
        public const double KEEP_ALIVE_TIMOUT = 1;
        /// <summary>
        /// how often to allow messages to go through, measured in seconds
        /// </summary>
        public const double SHORT_TIMEOUT = 0.01;
        /// <summary>
        /// indicates that a message was blocked for being a non-important message
        /// </summary>
        public const string MESSAGE_BLOCKED = "message blocked";
        /// <summary>
        /// Message returned upon a successful attempt
        /// </summary>
        public const string SUCCESS = "Success";
        public const string CANT_CONNECT = "Could not connect";

        #endregion

        #region fields

        /// <summary>
        /// Singleton used so that the initial connection to the
        /// server can be preserved between the connection page and
        /// the application logic.
        /// </summary>
        private static Connection Instance;

        private SocketClient currentSocketClient;
        private DispatcherTimer KeepAliveTimer;
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
                EndConnection();
            }
        }

        private void Init()
        {
            PrintLine();
        }

        public async Task<string> Connect(String hostname, int port)
        {
            PrintLine();
            if (currentSocketClient != null)
            {
                currentSocketClient.Close();
            }
            currentSocketClient = EstablishSocketClient();

            // attempt to connect
            try
            {
                await currentSocketClient.Connect(hostname, port);
            }
            catch (TimeoutException)
            {
                currentSocketClient.Close();
                currentSocketClient = null;
                return CANT_CONNECT;
            }
            catch (Exception e)
            {
                currentSocketClient.Close();
                currentSocketClient = null;
                return e.Message;
            }

            // create the listener
            KeepAliveTimer = new DispatcherTimer();
            KeepAliveTimer.Tick += new EventHandler<object>(CheckAlive);
            KeepAliveTimer.Interval = new TimeSpan(0, 0, Convert.ToInt32(Connection.KEEP_ALIVE_TIMOUT));
            KeepAliveTimer.Start();

            return SUCCESS;
        }

        public SocketClient EstablishSocketClient()
        {
            PrintLine();
            var sc = new SocketClient();
            sc.NonFatalException += NonFatalSocketException;
            sc.FatalException += FatalSocketException;
            sc.MessageReceived += SocketCallback;
            sc.SocketClosed += SocketDisconnected;
            return sc;
        }

        public async Task<string> Send(string data, bool important)
        {
            PrintLine();
            if (currentSocketClient == null) { };
            // check preconditions
            if (currentSocketClient == null)
            {
                return SocketClient.DISCONNECTED;
            }

            // check that there is room for a non-important message
            if (!important &&
                (DateTime.Now.Ticks - lastSendTime < SHORT_TIMEOUT * 1000 * 10000 * 2))
            {
                return MESSAGE_BLOCKED;
            }

            // try to send
            try
            {
                await currentSocketClient.Send(data);
            }
            catch (TimeoutException)
            {
                return CANT_CONNECT;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                lastSendTime = DateTime.Now.Ticks;
            }

            return SUCCESS;
        }

        public void SideDisconnect()
        {
            PrintLine();
            Disconnected();
        }

        #endregion

        #region public static methods

        public static Connection GetInstance()
        {
            PrintLine();
            if (Instance == null)
            {
                Instance = new Connection();
            }
            return Instance;
        }

        #endregion

        #region private methods

        private void NonFatalSocketException(object sender, object e)
        {
            var args = (e as SocketClient.NonFatalEventArgs);
            System.Diagnostics.Debug.WriteLine(args.exception.Message);
            System.Diagnostics.Debug.WriteLine(args.exception.StackTrace);
            // TODO: anything here?
        }

        private void FatalSocketException(object sender, object e)
        {
            PrintLine();
            var args = (e as SocketClient.FatalEventArgs);
            System.Diagnostics.Debug.WriteLine(args.exception.Message);
            System.Diagnostics.Debug.WriteLine(args.exception.StackTrace);
            // TODO: anything here?
        }

        private void SocketDisconnected(object sender, object e)
        {
            PrintLine();
            DeferredDisconnected();
        }

        private void CheckAlive(object sender, object e)
        {
            PrintLine();
            Send(SocketClient.KEEP_ALIVE, false);
        }

        public void GetMessage(string message)
        {
            PrintLine();
            if (message == SocketClient.KEEP_ALIVE)
            {
                // ignore keep alive messages
                return;
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
                    // pass the message along
                    MessageRecievedEvent(this, new MessageReceivedEventArgs(message));
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

        private void SocketCallback(object s, object args)
        {
            PrintLine();
            
            // check the type of args
            if (!(args is SocketClient.MessageReceivedEventArgs))
            {
                System.Diagnostics.Debug.WriteLine("args is of type " + args.GetType().FullName);
                return;
            }

            // receive and parse the message
            string message = (args as SocketClient.MessageReceivedEventArgs).message;

            // determine if this message is either a disconnect or worthy message
            if (String.IsNullOrEmpty(message))
            {
                return;
            }
            else
            {
                GetMessage(message);
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
            System.Diagnostics.Debug.WriteLine(line + ":CX:" + memberName);
        }
    }
}
