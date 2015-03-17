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
        /// Updates the playback when changed via <see cref="Equalizer.UpdatePlayback"/>.
        /// </summary>
        public bool IsPlaying
        {
            get { return isPlaying; }
            set
            {
                isPlaying = value;
                if (PlaybackUpdatedEvent != null)
                {
                    PlaybackUpdatedEvent(this,
                        new BoolEventArgs(isPlaying));
                }
            }
        }
        /// <summary>
        /// Reference to the track currently being played.
        /// </summary>
        private Track currentTrack;
        /// <summary>
        /// Updates the playback when changed via <see cref="Equalizer.UpdateTrackname"/>.
        /// </summary>
        public Track CurrentTrack
        {
            get { return currentTrack; }
            set
            {
                Track oldTrack = currentTrack;
                currentTrack = value;
                currentTrack.ChangedEvent += TrackChanged;
                TrackChanged(this, null);
            }
        }

        #endregion

        #region event handlers

        public EventHandler PlaybackUpdatedEvent;
        public EventHandler TrackChangedEvent;

        #endregion

        #region public methods

        public EqualizerManager()
        {
            currentTrack = new Track();
            currentTrack.ChangedEvent += TrackChanged;
        }

        #endregion

        #region private methods

        private void TrackChanged(object sender, object args)
        {
            if (TrackChangedEvent != null)
            {
                TrackChangedEvent(this, new
                    TrackEventArgs(currentTrack));
            }
        }

        #endregion

        #region classes

        public class BoolEventArgs : EventArgs {
            public bool value { get; set; }
            public BoolEventArgs(bool v)
            {
                value = v;
            }
        }
        public class TrackEventArgs : EventArgs
        {
            public Track oldTrack { get; set; }
            public TrackEventArgs(Track o)
            {
                oldTrack = o;
            }
        }

        #endregion
    }
}
