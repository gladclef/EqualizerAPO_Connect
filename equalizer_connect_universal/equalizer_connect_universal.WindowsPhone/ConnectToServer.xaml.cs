using System;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;

namespace equalizer_connect_universal
{
    /// <summary>
    /// The front page of the application.
    /// A simple page used to input the IPv4 address of the
    /// server and attempt to connect to it.
    /// </summary>
    public partial class ConnectToServer
    {
        #region constants/fields

        /// <summary>
        /// Key to the last address used to connect to a computer.
        /// </summary>
        public const string LAST_ADDRESS = "last loaded ipaddress";

        /// <summary>
        /// Used in place of Thread.Sleep()
        /// </summary>
        public ManualResetEvent sleeper = new ManualResetEvent(false);

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class, including
        /// initialize fields
        /// </summary>
        public ConnectToServer()
        {
            InitializeComponent();
            LoadIPAddress();

            // close upon leaving this app
            Windows.Phone.UI.Input.HardwareButtons.BackPressed += HardwareButtons_BackPressed;
        }

        #endregion

        #region private/protected methods

        /// <summary>
        /// Closes all connections and exits the application upon back button press,
        /// because that apparently isn't a thing that WP apps do automatically
        /// anymore.
        /// </summary>
        /// <param name="sender">The phone, I guess? N/A</param>
        /// <param name="e">N/A</param>
        private void HardwareButtons_BackPressed(object sender, Windows.Phone.UI.Input.BackPressedEventArgs e)
        {
            if (Frame.CurrentSourcePageType == this.GetType() &&
                !e.Handled)
            {
                e.Handled = true;
                Connection.GetInstance().Close();
                Application.Current.Exit();
            }
        }

        /// <summary>
        /// Load the IPv4 address used last time a connection was tried.
        /// </summary>
        private void LoadIPAddress()
        {
            SavedData sd = null;
            sd = SavedData.GetInstance();
            if (sd.Contains(LAST_ADDRESS))
            {
                textbox_hostname.Text = sd.GetStringValue(LAST_ADDRESS);
            }
        }

        /// <summary>
        /// Tries to connect to the server using the IPv4 address provided in the textbox.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void button_connect_Click(object sender, RoutedEventArgs args)
        {
            string hostname = this.textbox_hostname.Text;
            int port = Connection.APP_PORT;

            // save the hostname for next time
            SavedData sd = SavedData.GetInstance();
            sd.SaveValue(LAST_ADDRESS, hostname);

            // attempt to connect
            AttemptConnection(hostname, port);
        }

        /// <summary>
        /// Attempts to establish a connection to the server.
        /// If successful, establishes the connection and navigates to the Equalizer page.
        /// </summary>
        /// <param name="hostname">The IPv4 address to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        private async void AttemptConnection(string hostname, int port)
        {
            Connection connection = Connection.GetInstance();

            // tell the user we're about to connect
            Log(String.Format(
                "Trying to connect, hostname:{0}, port:{1}",
                hostname,
                port));

            // try the connection
            string success = await connection.Connect(hostname, port);
            if (String.IsNullOrEmpty(success) ||
                success == Connection.CANT_CONNECT)
            {
                Log("Failed to connect");
            }
            else
            {
                Log(success);
            }

            // was successful? continue!
            if (success == SocketClient.SUCCESS)
            {
                sleeper.WaitOne(1000);
                Frame.Navigate(typeof(Equalizer));
            }
        }

        /// <summary>
        /// Write text to the textblock.
        /// </summary>
        /// <param name="toPrint">The text to log.</param>
        /// <param name="appendNewline">Append a newline at the end of the text?</param>
        private void Log(string toPrint, bool appendNewline = true)
        {
            if (appendNewline)
            {
                toPrint += "\n";
            }
            textblock_logger.Text += toPrint;
        }

        #endregion
    }
}