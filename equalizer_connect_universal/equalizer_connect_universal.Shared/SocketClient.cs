using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace equalizerapo_connect_universal
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

        // exception types
        public enum ExceptionType { CONNECTION_NOT_ESTABLISHED, HOSTNAME_INVALID,
            SEND_RECEIVE_NO_SOCKET, SEND_FAILED, RECEIVE_FAILED }

        // number of times to attempt something
        public const int MAX_CONNECT_ATTEMPTS = 3;
        public const int MAX_SEND_ATTEMPS = 3;

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
        static ManualResetEvent _clientDone = new ManualResetEvent(false);

        /// <summary>
        /// The last hostName that was used to try and establish a connection.
        /// </summary>
        string lastHostName = "";
        /// <summary>
        /// The last portNumber that was used to try and establish a connection.
        /// </summary>
        int lastPortNumber = 0;

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
        /// <returns>A string representing the result of this connection attempt</returns>
        public void Connect(string hostName, int portNumber)
        {
            Connect(hostName, portNumber, 0);
        }

        /// <summary>
        /// Attempts to reconnect the socket using the same credentials as the
        /// last connection attempt.
        /// </summary>
        /// <returns>The result of the connection.</returns>
        public void Reconnect()
        {
            PrintLine();
            // end the last connection
            Close();
            (new ManualResetEvent(false)).WaitOne(500);

            // start a new connection
            Connect(lastHostName, lastPortNumber);
        }

        /// <summary>
        /// Send the given data to the server using the established connection.
        /// If there is a soft fail, it will attempt to send again up to
        /// MAX_CONNECT_ATTEMPTS times.
        /// </summary>
        /// <param name="data">The data to send to the server</param>
        /// <returns>The result of the Send request</returns>
        public void Send(string data)
        {
            Send(data, 0);
        }

        /// <summary>
        /// Closes the Socket connection and releases all associated resources
        /// </summary>
        public void Close()
        {
            PrintLine();

            if (_dataWriter != null)
            {
                // To reuse the socket with other data writer, application has to detach the stream from the writer
                // before disposing it. This is added for completeness, as this application closes the socket in
                // very next block.
                _dataWriter.DetachStream();
                _dataWriter.Dispose();
            }

            if (_dataReader != null)
            {
                // To reuse the socket with other data reader, application has to detach the stream from the reader
                // before disposing it. This is added for completeness, as this application closes the socket in
                // very next block.
                _dataReader.DetachStream();
                _dataReader.Dispose();
            }

            if (_socket != null)
            {
                // Remove the socket from the list of application properties as we are about to close it.
                _socket.Dispose();
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
        /// <returns>A string representing the result of this connection attempt</returns>
        private async void Connect(string hostName, int portNumber, int numAttempts)
        {
            PrintLine();
            string result = string.Empty;
            lastHostName = hostName;
            lastPortNumber = portNumber;

            // check the number of attempts
            if (numAttempts >= MAX_CONNECT_ATTEMPTS)
            {
                var e = new TimeoutException("Too many attempts to connect to remote server");
                if (FatalException != null)
                {
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                }
                throw e;
            }

            // create the socket
            StreamSocket newSocket = new StreamSocket();

            // connect to the remote end
            try
            {
                await newSocket.ConnectAsync(
                    new HostName(hostName),
                    portNumber.ToString(),
                    SocketProtectionLevel.PlainSocket);
            }
            catch (ArgumentException e)
            {
                if (NonFatalException != null)
                {
                    NonFatalException(this, new NonFatalEventArgs(
                        ExceptionType.HOSTNAME_INVALID, e));
                }

                // attempt to reconnect
                Connect(hostName, portNumber, numAttempts + 1);
            }
            catch (Exception e)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                {
                    if (FatalException != null)
                    {
                        FatalException(this, new FatalEventArgs(
                            ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                    }
                    throw;
                }
                else
                {
                    if (NonFatalException != null)
                    {
                        NonFatalException(this, new NonFatalEventArgs(
                            ExceptionType.CONNECTION_NOT_ESTABLISHED, e));
                    }

                    // attempt to reconnect
                    Connect(hostName, portNumber, numAttempts + 1);
                }
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
            ListenForMessages(null, null);
        }

        /// <summary>
        /// Send the given data to the server using the established connection
        /// </summary>
        /// <param name="data">The data to send to the server</param>
        /// <param name="numAttempts">The number of times a send has already been tried</param>
        /// <returns>The result of the Send request</returns>
        private async void Send(string data, int numAttempts)
        {
            PrintLine();

            // check the number of attempts
            if (numAttempts >= MAX_SEND_ATTEMPS)
            {
                var e = new TimeoutException("Too many attempts to send \"" +
                    data + "\" to remote server");
                if (FatalException != null)
                {
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.SEND_FAILED, e));
                }
                throw e;
            }

            // We are re-using the _socket object initialized in the Connect method
            if (_socket == null || _dataWriter == null)
            {
                if (NonFatalException != null)
                {
                    var e = new NullReferenceException(
                        "Socket is null while attempting to send message \"" + data + "\"");
                    NonFatalException(this, new NonFatalEventArgs(
                        ExceptionType.SEND_RECEIVE_NO_SOCKET, e));
                    throw e;
                }
            }

            // Write the string. 
            // Writing data to the writer will just store data in memory.
            _dataWriter.WriteString(data);

            // Write the locally buffered data to the network.
            try
            {
                await _dataWriter.StoreAsync();
            }
            catch (Exception e)
            {
                // If this is an unknown status it means that the error if fatal and retry will likely fail.
                if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                {
                    if (FatalException != null)
                    {
                        FatalException(this, new FatalEventArgs(
                            ExceptionType.SEND_FAILED, e));
                    }
                    throw;
                }
                else
                {
                    if (NonFatalException != null)
                    {
                        NonFatalException(this, new NonFatalEventArgs(
                            ExceptionType.SEND_FAILED, e));
                    }

                    // attempt to send again
                    Send(data, numAttempts + 1);
                }
            }
        }

        /// <summary>
        /// Invoked once a connection is accepted by Listen() or
        /// a connection has been establed by Connect().
        /// <param name="sender">The listener that accepted the connection.</param>
        /// <param name="args">Parameters associated with the accepted connection.</param>
        /// </summary>
        private void ListenForMessages(
            StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args)
        {
            ListenForMessages(sender, args, 0);
        }

        /// <summary>
        /// Invoked once a connection is accepted by Listen() or
        /// a connection has been establed by Connect().
        /// <param name="sender">The listener that accepted the connection.</param>
        /// <param name="args">Parameters associated with the accepted connection.</param>
        /// <param name="numAttempts">The number of times listening for messages has already been tried</param>
        /// </summary>
        private async void ListenForMessages(
            StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args,
            int numAttempts)
        {
            PrintLine();

            // get the data reader object
            DataReader reader = null;
            if (_dataReader == null && args != null)
            {
                reader = new DataReader(args.Socket.InputStream);
            }
            else
            {
                reader = _dataReader;
            }

            // check that the reader exists
            if (reader == null)
            {
                if (FatalException != null)
                {
                    var e = new NullReferenceException("reader doesn't exist for socket");
                    FatalException(this, new FatalEventArgs(
                        ExceptionType.RECEIVE_FAILED, e));
                    throw e;
                }
                return;
            }

            while (true)
            {
                try
                {
                    // Read first 4 bytes (length of the subsequent string).
                    uint sizeFieldCount = await reader.LoadAsync(sizeof(uint));
                    if (sizeFieldCount != sizeof(uint))
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        if (SocketClosed != null)
                        {
                            SocketClosed(this, new SocketClosedEventArgs(this));
                        }
                        return;
                    }

                    // Read the string.
                    uint stringLength = reader.ReadUInt32();
                    uint actualStringLength = await reader.LoadAsync(stringLength);
                    if (stringLength != actualStringLength)
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        if (SocketClosed != null)
                        {
                            SocketClosed(this, new SocketClosedEventArgs(this));
                        }
                        return;
                    }

                    // Tell the main program that a message has been received, and what that message is
                    string m = reader.ReadString(actualStringLength);
                    if (MessageReceived != null)
                    {
                        MessageReceived(this, new MessageReceivedEventArgs(m));
                    }
                }
                catch (Exception e)
                {
                    // If this is an unknown status it means that the error is fatal and retry will likely fail.
                    if (SocketError.GetStatus(e.HResult) == SocketErrorStatus.Unknown)
                    {
                        FatalException(this, new FatalEventArgs(
                            ExceptionType.RECEIVE_FAILED, e));
                        throw e;
                    }
                    else
                    {
                        NonFatalException(this, new NonFatalEventArgs(
                            ExceptionType.RECEIVE_FAILED, e));

                        // attempt to continue listening
                        ListenForMessages(sender, args, numAttempts + 1);
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
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":SC");
        }
    }
}
