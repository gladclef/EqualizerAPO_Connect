﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace equalizer_connect_universal
{
    /// <summary>
    /// Parses messages received from the server and
    /// prepares messages to send to the server.
    /// </summary>
    public class MessageParser
    {
        #region fields/properties

        /// <summary>
        /// Possible types of messages to prepare.
        /// </summary>
        public enum MESSAGE_TYPE
        {
            TRACK_CHANGED, FILTERS_GAIN, FILTER_REMOVED,
            FILTER_ADDED, PLAY, PAUSE, VOLUME_CHANGED,
            FILTER_APPLY, NEXT_TRACK, PREV_TRACK
        };

        /// <summary>
        /// The <see cref="FilterManager"/> communicated with
        /// in parsing/preparing messages.
        /// </summary>
        private FilterManager filterAPI;

        /// <summary>
        /// The <see cref="EqualizerManager"/> communicated with
        /// in parsing/preparing messages.
        /// </summary>
        private EqualizerManager equalizer;

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class.
        /// </summary>
        /// <param name="fm">Used in parsing/preparing messages.</param>
        /// <param name="eq">Used in parsing/preparing messages.</param>
        public MessageParser(FilterManager fm, EqualizerManager eq)
        {
            PrintLine();
            filterAPI = fm;
            equalizer = eq;
        }

        /// <summary>
        /// Parse a message from the server.
        /// May update the filters or playback based on the message contents.
        /// </summary>
        /// <param name="message">The message to parse.</param>
        public void ParseMessage(string message) {
            PrintLine();
            // get the message parts
            string[] messageParts = message.Split(new char[] { ':' }, 2);
            string messageType = messageParts[0];
            string restOfMessage = messageParts[1];

            System.Diagnostics.Debug.WriteLine("-- " + messageType);
            System.Diagnostics.Debug.WriteLine("   -- " + restOfMessage);

            // parse the message
            switch (messageType)
            {
                case "apply_filter":
                    filterAPI.IsEqualizerApplied =
                        (restOfMessage == "true") ? true : false;
                    break;
                case "filter":
                    if (restOfMessage == "added")
                    {
                        filterAPI.AddFilter(0);
                    }
                    else if (restOfMessage == "removed")
                    {
                        filterAPI.RemoveFilter();
                    }
                    break;
                case "filters":
                    filterAPI.SetNewGainValues(restOfMessage.Split(new char[] { ',' }));
                    break;
                case "playback":
                    if (restOfMessage == "play")
                    {
                        equalizer.IsPlaying = true;
                    }
                    else if (restOfMessage == "pause")
                    {
                        equalizer.IsPlaying = false;
                    }
                    break;
                case "volume":
                    filterAPI.PreAmpGain = Convert.ToDouble(restOfMessage);
                    break;
                case "track_changed":
                    foreach (string nextMessage in restOfMessage.Split(new char[] { ';' }))
                    {
                        ParseMessage(nextMessage);
                    }
                    break;
                case "artist":
                    equalizer.CurrentTrack.Artist = restOfMessage;
                    break;
                case "trackname":
                    equalizer.CurrentTrack.Title = restOfMessage;
                    break;
            }
        }

        /// <summary>
        /// Prepares a message to send to the server.
        /// Talks to the <see cref="FilterManager"/> and/or
        /// <see cref="EqualizerManager"/> to get the most up-to-date
        /// message contents.
        /// </summary>
        /// <param name="type">The type of message to prepare.</param>
        /// <returns>A string that represents that message.</returns>
        public string PrepareMessage(MESSAGE_TYPE type)
        {
            PrintLine();
            StringBuilder sb = new StringBuilder();

            switch (type)
            {
                case MESSAGE_TYPE.FILTER_APPLY:
                    sb.Append("apply_filter:");
                    sb.Append(filterAPI.IsEqualizerApplied ? "true" : "false");
                    break;
                case MESSAGE_TYPE.FILTER_REMOVED:
                    sb.Append("filter:removed");
                    break;
                case MESSAGE_TYPE.FILTER_ADDED:
                    sb.Append("filter:added");
                    break;
                case MESSAGE_TYPE.FILTERS_GAIN:
                    sb.Append("filters:");

                    // get the gains on the filters
                    bool first = true;
                    foreach (KeyValuePair<double,Filter> pair in filterAPI.Filters)
                    {
                        Filter filter = pair.Value;
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append(",");
                        }
                        sb.Append(filter.Gain.ToString());
                    }
                    break;
                case MESSAGE_TYPE.PAUSE:
                    sb.Append("playback:pause");
                    break;
                case MESSAGE_TYPE.PLAY:
                    sb.Append("playback:play");
                    break;
                case MESSAGE_TYPE.PREV_TRACK:
                    sb.Append("playback:previous");
                    break;
                case MESSAGE_TYPE.NEXT_TRACK:
                    sb.Append("playback:next");
                    break;
                case MESSAGE_TYPE.VOLUME_CHANGED:
                    sb.Append("volume:");
                    sb.Append(filterAPI.PreAmpGain);
                    break;
            }

            return sb.ToString();
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":MP");
        }
    }
}
