using System;
using System.Collections.Generic;
using System.Text;

namespace equalizer_connect_universal
{
    /// <summary>
    /// Manages the state of the equalizer
    /// (current <see cref="Track"/> and playback status).
    /// </summary>
    public class EqualizerManager
    {
        #region constants

        /// <summary>
        /// How much does the gain of the volume/filter have to change
        /// for the change to be registered.
        /// </summary>
        public const double GAIN_ACCURACY = 0.09;

        /// <summary>
        /// Maximum preamp (aka volume) gain. Positive or negative.
        /// </summary>
        public const double MAX_PREAMP_GAIN = 30;

        /// <summary>
        /// Maximum gain on the filters.
        /// </summary>
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

        /// <summary>
        /// Triggers whenever the playback status is updated.
        /// </summary>
        public EventHandler PlaybackUpdatedEvent;
        
        /// <summary>
        /// Triggers whenever the track is changed/updated.
        /// </summary>
        public EventHandler TrackChangedEvent;

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class.
        /// </summary>
        public EqualizerManager()
        {
            currentTrack = new Track();
            currentTrack.ChangedEvent += TrackChanged;
        }

        #endregion

        #region private methods

        /// <summary>
        /// Callback for <see cref="Track.ChangedEvent"/>.
        /// Triggers <see cref="TrackEventArgs"/>.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
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
