﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Threading;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

namespace equalizerapo_connect_universal
{
    public partial class ConnectToServer : PhoneApplicationPage
    {
        public const string LAST_ADDRESS = "last loaded ipaddress";

        private Connection connection;

        public ConnectToServer()
        {
            InitializeComponent();
            connection = Connection.GetInstance();
            LoadIPAddress();
        }

        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //NavigationService.Navigate(new Uri("/MainPage.xaml", UriKind.Relative));
        }

        private void LoadIPAddress()
        {
            SavedData sd = null;
            sd = SavedData.GetInstance();
            if (sd.Contains(LAST_ADDRESS))
            {
                textbox_hostname.Text = sd.GetStringValue(LAST_ADDRESS);
            }
        }

        private void button_connect_Click(object sender, RoutedEventArgs e)
        {
            string hostname = this.textbox_hostname.Text;
            int port = Connection.APP_PORT;

            // save the hostname for next time
            SavedData sd = SavedData.GetInstance();
            sd.SaveStringValue(LAST_ADDRESS, hostname);

            // tell the user we're about to connect
            Log(String.Format(
                "Trying to connect, hostname:{0}, port:{1}",
                hostname,
                port));
            
            // try the connection
            string success = connection.Connect(hostname, port);
            Log(success);

            // was successful? continue!
            if (success == "Success")
            {
                (new ManualResetEvent(false)).WaitOne(1000);
                NavigationService.Navigate(
                    new Uri("/Equalizer.xaml", UriKind.Relative));
            }
        }

        private void Log(string toPrint)
        {
            Log(toPrint, true);
        }

        private void Log(string toPrint, bool appendNewline)
        {
            if (appendNewline)
            {
                toPrint += "\n";
            }
            textblock_logger.Text += toPrint;
        }
    }
}