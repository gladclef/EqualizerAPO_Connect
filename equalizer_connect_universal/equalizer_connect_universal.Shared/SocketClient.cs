using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace equalizer_connect_universal
{
    /// <summary>
    /// Used as an interface to sockets.
    /// Large swaths of this have been taken directly from:
    ///     https://code.msdn.microsoft.com/windowsapps/StreamSocket-Sample-8c573931
    /// and from:
    ///     https://msdn.microsoft.com/en-us/library/windows/apps/hh202858(v=vs.105).aspx
    /// </summary>
    public class SocketClient
    {
        #region constants

        // Define a timeout in milliseconds for each asynchronous call. If a response is not received within this 
        // timeout period, the call is aborted.
        const int TIMEOUT_MILLISECONDS = 5000;

        // The maximum size of the data buffer to use with the asynchronous socket methods
        const int MAX_BUFFER_SIZE = 2048;

        // send/receive message constants
        public const string OPERATION_TIMEOUT = "Operation Timeout";
        public const string UNINITIALIZED = "Socket is not initialized";
        public const string SUCCESS = "Success";
        public const string KEEP_ALIVE = "Keep alive check";
        public const string KEEP_ALIVE_ACK = "Keep alive acknowledged";
        public const string NO_MESSAGE = "No message available";
        public const string DISCONNECTED = "NotConnected";
        public const string CONNECTION_ABORTED = "ConnectionAborted";
        public const string CONNECTION_RESET = "ConnectionReset";
        public const string CONNECTION_CLOSED_REMOTELY = "An existing connection was forcibly closed by the remote host.";

        // exception types
        public enum ExceptionType { CONNECTION_NOT_ESTABLISHED, HOSTNAME_INVALID,
            SEND_RECEIVE_NO_SOCKET, SEND_FAILED, RECEIVE_FAILED }

        // number of times to attempt something
        public const int MAX_CONNECT_ATTEMPTS = 1;
        public const int MAX_SEND_ATTEMPS = 1;
        public const int MAX_LISTEN_ATTEMPTS = 1;

        #endregion

        #region fields

        /// <summary>
        /// Cached Socket object that will be used by each call for the lifetime of this class
        /// </summary>
        StreamSocket _socket = null;

        /// <summary>
        /// Cached output stream writer that will be used for each "send" call for the lifetime of this class
        /// </summary>
        DataWriter _dataWriter = null;

        /// <summary>
        /// Cached input stream reader that will be used for each "readmessage" call for the lifetime of this class
        /// </summary>
        DataReader _dataReader = null;

        /// <summary>
        /// Signaling object used to notify when an asynchronous operation is completed
        /// </summary>
        AutoResetEvent _clientDone = new AutoResetEvent(true);

        /// <summary>
        /// The last hostName that was used to try and establish a connection.
        /// </summary>
        string lastHostName = "";

        /// <summary>
        /// The last portNumber that was used to try and establish a connection.
        /// </summary>
        int lastPortNumber = 0;

        /// <summary>
        /// For each connection that is made and ListenForMessages that is called,
        /// set up a boolean that represents if that listener should continue
        /// listening or die (effectively a cancelation token).
        /// </summary>
        Dictionary<int, bool> activeListenerIDs = new Dictionary<int, bool>();

        #endregion

        #region event handlers
        
        /// <summary>
        /// Triggered whenever an exception is caught that might be such that
        /// a re-connection could be viable.
        /// <seealso cref="NonFatalEventArgs"/>
        /// </summary>
        public EventHandler NonFatalException;

        /// <summary>
        /// Triggered whenever an exception is caught such that a retry is likely unviable.
        /// <seealso cref="FatalEventArgs"/>
        /// </summary>
        public EventHandler FatalException;

        /// <summary>
        /// Triggers whenever a message is received.
        /// <seealso cref="MessageReceivedEventArgs"/>
        /// </summary>
        public EventHandler MessageReceived;

        /// <summary>
        /// Triggered whenever a socket has been detected to be closed.
        /// <seealso cref="SocketClosedEventArgs"/>
        /// </summary>
        public EventHandler SocketClosed;

        #endregion

        #region public methods

        /// <summary>
        /// Disconnect when being destructed.
        /// </summary>
        ~SocketClient()
        {
            PrintLine();
            Close();
        }

        /// <summary>
        /// Attempt a TCP socket connection to the given host over the given port.
        /// If there is a soft fail, it will attempt to connect again up to
        /// MAX_CONNECT_ATTEMPTS times.
        /// </summary>
        /// <param name="hostName">The name of the host</param>
        /// <param name="portNumber">The port number to connect</param>
        /// <returns>True if the connection succeeds, false otherwise</returns>
        public async Task<bool> Connect(string hostName, int portNumber)
        {
            PrintLine();

            // wait for the previous async event
            _clientDone.WaitOne(System.Threading.Timeout.Infinite);

            bool retval = await Connect(hostName, portNumber, 0);

            // connection completed! release async event
            _clientDone.Set();

            return retval;
        }

        /// <summary>
        /// Attempts to reconnect the socket using the same credentials as the
        /// last connection attempt.
        /// </summary>
        /// <returns>True if the connection succeeds.</returns>
        public async Task<bool> Reconnect()
        {
            PrintLine();

            // end the last connection
            Close();

            // wait for the previous async event
            _clientDone.WaitOne(System.Threading.Timeout.Infinite);

            // start a new connection
            bool retval = await Connect(lastHostName, lastPortNumber);

            // connection completed! release async event
            _clientDone.Set();

            return retval;
        }

        /// <summary>
        /// Send the given data to the server using the established connection.
        /// If there is a soft fail, it will attempt to send again up to
        /// MAX_CONNECT_ATTEMPTS times.
        /// </summary>
        /// <param name="data">The data to send to the server</param>
        /// <returns>True if the sending succeeds</returns>
        public async Task<bool> Send(string data)
        {
            PrintLine();

            // wait for the previous async event to finish
            _clientDone.WaitOne(System.Threading.Timeout.Infinite);

            bool result = await Send(data, 0);

            // sent! release async event
            _clientDone.Set();

            return result;
        }

        /// <summary>
        /// Closes the Socket connection and releases all associated resources
        /// </summary>
        public void Close()
        {
            PrintLine();

            // cancel all active listeners
            activeListenerIDs[activeListenerIDs.Count - 1] = false;

            if (_dataWriter != null)
            {
                // To reuse the socket with other data writer, application has to detach the stream from the writer
                // before disposing it. This is added for completeness, as this application closes the socket in
                // very next block.
                try
                {
                    _dataWriter.DetachStream();
                    _dataWriter.Dispose();
                }
                catch (Exception)
                {
                    // do nothing
                }
                _dataWriter = null;
            }

            if (_dataReader != null)
            {
                // To reuse the socket with other data reader, application has to detach the stream from the reader
                // before disposing it. This is added for completeness, as this application closes the socket in
                // very next block.
                try
                {
                    _dataReader.DetachStream();
                    _dataReader.Dispose();
                }
                catch (Exception)
                {
                    // do nothing
                }
                _dataReader = null;
            }

            if (_socket != null)
            {
                // Remove the socket from the list of application properties as we are about to close it.
                _socket.Dispose();
                _socket = null;
            }
        }

        #endregion

        #region private methods

        /// <summary>
        /// Attempt a TCP socket connection to the given host over the given port
        /// </summary>
        /// <param name="hostName">The name of the host</param>
        /// <param name="portNumber">The port number to connect</param>
        /// <param name="numAttempts">The number of times a connection has already been tried</param>
        /// <returns>True if the connection succeeds</returns>
        private async Task<bool> Connect(string hostName, int portNumber, int numAttempts)
        {
            PrintLine();

            System.Diagnostics.Debug.WriteLine(String.Format("connecting: {0}, {1}, {2}", hostName, portNumber, numAttempts));
            string result = string.Empty;
            lastHostName = hostName;
            lastPortNumber = portNumber;

            // check the number of attempts
            if (numAttempts >= MAX_CONNECT_ATTEMPTS)
            {
                PrintLine();
                var e = new TimeoutException("Too many attempts to connect to remote server");
                FatalException(this, new FatalEventArgs(
                    ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                throw e;
            }

            // initialize some values
            Task<bool> awaitable = null;
            StreamSocket newSocket = new StreamSocket();
            bool timerPopped = false;

            // connect to the remote end
            try
            {
                var timer = new AwaitTimer(3000);
                timerPopped = !timer.timeTask(newSocket.ConnectAsync(
                    new HostName(hostName),
                    portNumber.ToString(),
                    SocketProtectionLevel.PlainSocket));
            }
            catch (ArgumentException e)
            {
                NonFatalException(this, new NonFatalEventArgs(
                    ExceptionType.HOSTNAME_INVALID, e));

                // attempt to reconnect
                awaitable = Connect(hostName, portNumber, numAttempts + 1);
            }
            catch (Exception e)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                {
                    PrintLine();
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                    throw;
                }
                else
                {
                    NonFatalException(this, new NonFatalEventArgs(
                        ExceptionType.CONNECTION_NOT_ESTABLISHED, e));

                    // attempt to reconnect
                    awaitable = Connect(hostName, portNumber, numAttempts + 1);
                }
            }

            // wait for all async connection attempts
            if (awaitable != null)
            {
                await awaitable;
            }

            // did the timer pop? throw an error!
            if (timerPopped)
            {
                PrintLine();
                newSocket.Dispose();
                throw new TimeoutException("Timeout on connecting");
            }

            // Check that the socket doesn't already exist
            if (_socket != null)
            {
                Close();
            }

            // establish the socket
            _socket = newSocket;
            _dataWriter = new DataWriter(_socket.OutputStream);
            _dataReader = new DataReader(_socket.InputStream);
            ListenForMessages(null, null, activeListenerIDs.Count);
            activeListenerIDs.Add(activeListenerIDs.Count, true);

            PrintLine();
            System.Diagnostics.Debug.WriteLine("new socket: " + newSocket);

            return true;
        }

        /// <summary>
        /// Send the given data to the server using the established connection
        /// </summary>
        /// <param name="data">The data to send to the server</param>
        /// <param name="numAttempts">The number of times a send has already been tried</param>
        /// <returns>True if the send succeeds.</returns>
        private async Task<bool> Send(string data, int numAttempts)
        {
            PrintLine();

            // initialize some values
            bool timerPopped = false;
            Task<bool> awaitable = null;

            System.Diagnostics.Debug.WriteLine(String.Format(">> {0} [{1}]", data, numAttempts));

            // check the number of attempts
            if (numAttempts >= MAX_SEND_ATTEMPS)
            {
                var e = new TimeoutException("Too many attempts to send \"" +
                    data + "\" to remote server");
                FatalException(this, new FatalEventArgs(
                    ExceptionType.SEND_FAILED, e));
                throw e;
            }

            // We are re-using the _socket object initialized in the Connect method
            if (_socket == null || _dataWriter == null)
            {
                var e = new NullReferenceException(
                    "Socket is null while attempting to send message \"" + data + "\"");
                NonFatalException(this, new NonFatalEventArgs(
                    ExceptionType.SEND_RECEIVE_NO_SOCKET, e));
                throw e;
            }

            // Write the string.
            // Writing data to the writer will just store data in memory.
            if (numAttempts == 0)
            {
                _dataWriter.WriteUInt32(Convert.ToUInt32(data.Length));
                _dataWriter.WriteString(data);
            }

            // attempt to write the data
            try
            {
                // Write the locally buffered data to the network.
                var timer = new AwaitTimer(3000);
                timerPopped = !timer.timeTask(_dataWriter.StoreAsync());
            }
            catch (Exception e)
            {
                if (e.Message == CONNECTION_CLOSED_REMOTELY)
                {
                    // close the socket
                    Close();
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.RECEIVE_FAILED, e));
                    throw e;
                }
                else if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                {
                    // If this is an unknown status it means that the error if fatal and retry will likely fail.
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.SEND_FAILED, e));
                    throw;
                }
                else
                {
                    NonFatalException(this, new NonFatalEventArgs(
                        ExceptionType.SEND_FAILED, e));

                    // attempt to send again
                    awaitable = Send(data, numAttempts + 1);
                }
            }

            // wait for tasks to finish
            if (awaitable != null)
            {
                await awaitable;
            }

            // did the timer pop before the send could be completed?
            if (timerPopped)
            {
                throw new TimeoutException("Timeout on sending");
            }

            return true;
        }

        /// <summary>
        /// Invoked once a connection is accepted by Listen() or
        /// a connection has been establed by Connect().
        /// <param name="sender">The listener that accepted the connection.</param>
        /// <param name="args">Parameters associated with the accepted connection.</param>
        /// <param name="activeListeningID">The ID associated with the <see cref="activeListenerIDs"/> dict</param>
        /// <param name="numAttempts">The number of times listening for messages has already been tried</param>
        /// </summary>
        private async void ListenForMessages(
            StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args,
            int activeListeningID,
            int numAttempts = 0)
        {
            PrintLine();

            // check the number of attempts made
            if (numAttempts >= MAX_LISTEN_ATTEMPTS)
            {
                var e = new TimeoutException("Too many attempts to listen for messages from remote server");
                FatalException(this, new FatalEventArgs(
                    ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                throw e;
            }

            // check that the reader exists
            if (_dataReader == null)
            {
                var e = new NullReferenceException("reader doesn't exist for socket");
                FatalException(this, new FatalEventArgs(
                    ExceptionType.RECEIVE_FAILED, e));
                throw e;
            }

            // continue listening for incoming messages for forever!
            while (true)
            {
                try
                {
                    // Read first 4 bytes (length of the subsequent string).
                    uint sizeFieldCount = await _dataReader.LoadAsync(sizeof(uint));
                    if (sizeFieldCount != sizeof(uint))
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        SocketClosed(this, new SocketClosedEventArgs(this));
                        return;
                    }

                    // Read the string.
                    uint stringLength = _dataReader.ReadUInt32();
                    uint actualStringLength = await _dataReader.LoadAsync(stringLength);
                    if (stringLength != actualStringLength)
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        SocketClosed(this, new SocketClosedEventArgs(this));
                        return;
                    }

                    // Tell the main program that a message has been received, and what that message is
                    string m = _dataReader.ReadString(actualStringLength);
                    if (MessageReceived != null)
                    {
                        System.Diagnostics.Debug.WriteLine(String.Format("<< {0} [{1}]", m, numAttempts));
                        MessageReceived(this, new MessageReceivedEventArgs(m));
                    }
                }
                catch (Exception e)
                {
                    // check if I should stop listening
                    if (activeListenerIDs[activeListeningID] == false)
                    {
                        return;
                    }

                    if (e.Message == CONNECTION_CLOSED_REMOTELY)
                    {
                        // close the socket
                        Close();
                        SocketClosed(this, new SocketClosedEventArgs(this));
                        NonFatalException(this, new FatalEventArgs(
                            ExceptionType.RECEIVE_FAILED, e));
                        return;
                    }
                    else if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                    {
                        // If this is an unknown status it means that the error is fatal and retry will likely fail.
                        FatalException(this, new FatalEventArgs(
                            ExceptionType.RECEIVE_FAILED, e));
                        return;
                    }
                    else
                    {
                        NonFatalException(this, new NonFatalEventArgs(
                            ExceptionType.RECEIVE_FAILED, e));

                        // attempt to continue listening
                        ListenForMessages(sender, args, activeListeningID, numAttempts + 1);
                    }
                }
            }
        }

        #endregion

        #region public static methods

        public static string GetExceptionType(NonFatalEventArgs args)
        {
            return GetExceptionType(args.type);
        }

        public static string GetExceptionType(FatalEventArgs args)
        {
            return GetExceptionType(args.type);
        }

        public static string GetExceptionType(ExceptionType t)
        {
            switch (t)
            {
                case ExceptionType.CONNECTION_NOT_ESTABLISHED:
                    return "Connection unable to be established";
                case ExceptionType.HOSTNAME_INVALID:
                    return "Hostname is invalid";
            }
            return "Exception type not recognized";
        }

        #endregion

        #region delegates/classes

        public class NonFatalEventArgs : EventArgs
        {
            public ExceptionType type { get; set; }
            public Exception exception { get; set; }
            public NonFatalEventArgs(ExceptionType t, Exception e)
            {
                type = t;
                exception = e;
            }
        }
        public class FatalEventArgs : EventArgs
        {
            public ExceptionType type { get; set; }
            public Exception exception { get; set; }
            public FatalEventArgs(ExceptionType t, Exception e)
            {
                type = t;
                exception = e;
            }
        }
        public class MessageReceivedEventArgs : EventArgs
        {
            public string message { get; set; }
            public MessageReceivedEventArgs(string m)
            {
                message = m;
            }
        }
        public class SocketClosedEventArgs : EventArgs
        {
            public SocketClient Client { get; set; }
            public SocketClosedEventArgs(SocketClient sc)
            {
                Client = sc;
            }
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            System.Diagnostics.Debug.WriteLine(line + ":SC:" + memberName);
        }
    }
}
