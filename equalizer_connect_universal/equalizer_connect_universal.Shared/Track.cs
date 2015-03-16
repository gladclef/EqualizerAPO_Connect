using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace equalizerapo_connect_silverlight
{
    public class Track
    {
        private string artist;
        public string Artist
        {
            get { return artist; }
            set
            {
                artist = value;
                ValueChanged(new TrackChangedEventArgs(
                    "artist", artist));
            }
        }
        private string title;
        public string Title
        {
            get { return title; }
            set
            {
                title = value;
                ValueChanged(new TrackChangedEventArgs(
                    "title", title));
            }
        }

        public EventHandler ChangedEvent;

        public Track()
        {
            artist = "";
            title = "";
        }

        private void ValueChanged(TrackChangedEventArgs args)
        {
            if (ChangedEvent != null)
            {
                ChangedEvent(this, args);
            }
        }

        public class TrackChangedEventArgs : EventArgs {
            public string property;
            public string newValue;

            public TrackChangedEventArgs(string property, string newValue) {
                this.property = property;
                this.newValue = newValue;
            }
        }
    }
}
