using System;
using System.Collections.Generic;
using System.Text;

namespace equalizer_connect_universal
{
    public class EqualizerManager
    {
        #region constants

        public const double GAIN_ACCURACY = 0.1;
        public const double MAX_PREAMP_GAIN = 30;
        public const double GAIN_MAX = 15;

        #endregion

        #region fields

        /// <summary>
        /// Is the current song playing or paused?
        /// </summary>
        private bool isPlaying;
        /// <summary>
        /// Updates the playback when changed via <see cref="UpdatePlayback"/>.
        /// </summary>
        public bool IsPlaying
        {
            get { return isPlaying; }
            set
            {
                isPlaying = value;
                if (PlaybackUpdated != null)
                {
                    PlaybackUpdated(this,
                        new BoolEventArgs(isPlaying));
                }
            }
        }

        #endregion

        #region event handlers

        public EventHandler PlaybackUpdated;

        #endregion

        #region classes

        public class BoolEventArgs : EventArgs {
            public bool value { get; set; }
            public BoolEventArgs(bool v)
            {
                value = v;
            }
        }

        #endregion
    }
}
