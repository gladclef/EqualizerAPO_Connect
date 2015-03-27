using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace equalizer_connect_universal
{
    /// <summary>
    /// A representation of a track being played from the server.
    /// </summary>
    public class Track
    {
        #region fields/properties

        /// <summary>
        /// Artist's name
        /// </summary>
        private string artist;
        /// <summary>
        /// Artist's name.
        /// Triggers <see cref="ChangedEvent"/>
        /// </summary>
        public string Artist
        {
            get { return artist; }
            set
            {
                if (artist != value)
                {
                    artist = value;
                    ValueChanged(new TrackChangedEventArgs(
                        "artist", artist));
                }
            }
        }

        /// <summary>
        /// Track's title.
        /// </summary>
        private string title;
        /// <summary>
        /// Track's title.
        /// Triggers <see cref="ChangedEvent"/>
        /// </summary>
        public string Title
        {
            get { return title; }
            set
            {
                if (title != value)
                {
                    title = value;
                    ValueChanged(new TrackChangedEventArgs(
                        "title", title));
                }
            }
        }

        #endregion

        #region event handlers

        /// <summary>
        /// Triggered whenever either the artist or title are changed.
        /// </summary>
        public EventHandler ChangedEvent;

        #endregion

        #region public methods

        /// <summary>
        /// Initializer a new instance of this class.
        /// </summary>
        public Track()
        {
            artist = "";
            title = "";
        }

        #endregion

        #region private methods

        /// <summary>
        /// Triggers <see cref="ChangedEvent"/> as long
        /// as it isn't null.
        /// </summary>
        /// <param name="args">The args to pass to the handlers.</param>
        private void ValueChanged(TrackChangedEventArgs args)
        {
            if (ChangedEvent != null)
            {
                ChangedEvent(this, args);
            }
        }

        #endregion

        #region classes

        public class TrackChangedEventArgs : EventArgs
        {
            public string property;
            public string newValue;

            public TrackChangedEventArgs(string property, string newValue)
            {
                this.property = property;
                this.newValue = newValue;
            }
        }

        #endregion
    }
}
