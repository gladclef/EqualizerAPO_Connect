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
    /// <summary>
    /// Used as an interface between the application and the SocketClient
    /// class to abstract away some of the details of dealing with sockets
    /// (because God knows there's plenty to be abstracted).
    /// 
    /// Both a singleton and multi-instance class. Singleton class
    /// because there needs to be one primary instance used to establish
    /// communication with the server. Multi-instance class because
    /// every new SocketClient requires a new Connection to be used.
    /// 
    /// In this application, there should only ever be one Connection
    /// instance.
    /// </summary>
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

        /// <summary>
        /// Message returned when a connection can't be established
        /// </summary>
        public const string CANT_CONNECT = "Could not connect";

        #endregion

        #region fields

        /// <summary>
        /// Singleton used so that the initial connection to the
        /// server can be preserved between the connection page and
        /// the application logic.
        /// </summary>
        private static Connection Instance;

        /// <summary>
        /// The SocketClient that belongs to this connection. Every time
        /// <see cref="Connect"/> or <see cref="Close"/> are called
        /// this value changes.
        /// </summary>
        private SocketClient currentSocketClient;

        /// <summary>
        /// Timer to send keep alive messages.
        /// The sending part isn't really important. It's the
        /// act of using the socket, which will generate a
        /// disconnect event if the sending fails.
        /// </summary>
        private DispatcherTimer KeepAliveTimer;

        /// <summary>
        /// The last time, in ticks (nanosecs) that a message
        /// was sent.
        /// Used to rate limit messages sent per second, based
        /// on the importance of the message.
        /// </summary>
        private long lastSendTime;

        #endregion

        #region properties


        #endregion

        #region event handlers

        /// <summary>
        /// Triggered when a message is received from the Socket.
        /// </summary>
        public EventHandler MessageRecievedEvent { get; set; }

        /// <summary>
        /// Triggered when a disconnect is received from the
        /// Socket or this object disconnects the Socket.
        /// </summary>
        public EventHandler DisconnectedEvent { get; set; }

        /// <summary>
        /// Triggered when this object disconnects the Socket.
        /// </summary>
        public EventHandler DisconnectMe { get; set; }

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance.
        /// <seealso cref="Connect"/>
        /// </summary>
        public Connection()
        {
            PrintLine();
            Init();
        }

        /// <summary>
        /// Close the SocketClient when destroying this instance.
        /// </summary>
        ~Connection()
        {
            PrintLine();
            Close();
        }

        /// <summary>
        /// Ends all connections for this instance (eg closing the
        /// SocketClient associated with this instance).
        /// <seealso cref="SideDisconnect"/>
        /// </summary>
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

        /// <summary>
        /// Initialize the class (here to provide the interface for
        /// future implementation).
        /// </summary>
        private void Init()
        {
            PrintLine();
        }

        /// <summary>
        /// Try to connect to the server!
        /// </summary>
        /// <param name="hostname">The IPv4 address of the server.</param>
        /// <param name="port">The port to connect on.
        ///     <see cref="APP_PORT"/></param>
        /// <returns>One of <see cref="CANT_CONNECT"/>,
        ///     <see cref="SUCCESS"/>, or a message from the
        ///     SocketClient.</returns>
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

        /// <summary>
        /// Creates a new SocketClient and subscribes to its events.
        /// </summary>
        /// <returns>The new SocketClient.</returns>
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

        /// <summary>
        /// Tries to send a message to the server.
        /// </summary>
        /// <param name="data">The message to send.</param>
        /// <param name="important">True if this message MUST
        ///     be sent and can't be blocked simply because the
        ///     last message had been sent too recently.</param>
        /// <returns>One of <see cref="SocketClient.DISCONNECTED"/>,
        ///     <see cref="MESSAGE_BLOCKED"/>, <see cref="CANT_CONNECT"/>,
        ///     <see cref="SUCCESS"/>, or a message from the
        ///     SocketClient.</returns>
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
            catch (Exception e)
            {
                if (e is TimeoutException ||
                    e is NullReferenceException)
                {
                    return CANT_CONNECT;
                }
                return e.Message;
            }
            finally
            {
                lastSendTime = DateTime.Now.Ticks;
            }

            return SUCCESS;
        }

        /// <summary>
        /// Disconnect this instance from the SocketClient.
        /// </summary>
        public void SideDisconnect()
        {
            PrintLine();
            Disconnected();
        }

        #endregion

        #region public static methods

        /// <summary>
        /// Creates/gets the static instance of this class.
        /// Used so that said instance can be easily used between views.
        /// </summary>
        /// <returns>The static instance.</returns>
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

        /// <summary>
        /// Handles non-fatal exceptions that get caught/generated
        /// in the SocketClient class.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">The <see cref="SocketClient.NonFatalEventArgs"/></param>
        private void NonFatalSocketException(object sender, object e)
        {
            var args = (e as SocketClient.NonFatalEventArgs);
            System.Diagnostics.Debug.WriteLine(args.exception.Message);
            System.Diagnostics.Debug.WriteLine(args.exception.StackTrace);
            // TODO: anything here?
        }

        /// <summary>
        /// Handles fatal exceptions that get caught/generated
        /// in the SocketClient class.
        /// Disconnects the SocketClient.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">The <see cref="SocketClient.FatalEventArgs"/></param>
        private void FatalSocketException(object sender, object e)
        {
            PrintLine();
            var args = (e as SocketClient.FatalEventArgs);
            System.Diagnostics.Debug.WriteLine(args.exception.Message);
            System.Diagnostics.Debug.WriteLine(args.exception.StackTrace);
            currentSocketClient.Close();
        }

        /// <summary>
        /// Handles disconnect events from the SocketClient.
        /// Disconnects this instance.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">The <see cref="SocketClient.SocketClosedEventArgs"/></param>
        private void SocketDisconnected(object sender, object e)
        {
            PrintLine();
            DeferredDisconnected();
        }

        /// <summary>
        /// Checks if the connection is still live by pinging the server.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void CheckAlive(object sender, object e)
        {
            PrintLine();
            Send(SocketClient.KEEP_ALIVE, false);
        }

        /// <summary>
        /// First interpretter for a message from the SocketClient/server.
        /// For most messages it just passes the message along to
        /// <see cref="MessageRecievedEvent"/>.
        /// </summary>
        /// <param name="message">The message to interpret.</param>
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

        /// <summary>
        /// Closes all connections and calls
        /// <see cref="DisconnectedEvent"/>.
        /// </summary>
        /// <seealso cref="DeferredDisconnected"/>
        /// <seealso cref="EndConnection"/>
        private void Disconnected()
        {
            PrintLine();
            EndConnection();
            if (DisconnectedEvent != null)
            {
                DisconnectedEvent(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called within the class in response to socket exceptions.
        /// Calls <see cref="Disconnected"/>.
        /// Calls <see cref="DisconnectMe"/>.
        /// </summary>
        /// <seealso cref="EndConnection"/>
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

        /// <summary>
        /// Closes and kills the SocketClient and
        /// <see cref="KeepAliveTimer"/>.
        /// </summary>
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

        /// <summary>
        /// Handles incoming message events from the SocketClient.
        /// Accepts the message from the socket and does basic error checking on it.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">The <see cref="SocketClient.MessageReceivedEventArgs"/></param>
        /// <seealso cref="GetMessage"/>
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
            return;
            System.Diagnostics.Debug.WriteLine(line + ":CX:" + memberName);
        }
    }
}
