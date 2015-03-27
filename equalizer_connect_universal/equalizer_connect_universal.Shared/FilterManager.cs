using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace equalizer_connect_universal
{
    /// <summary>
    /// A singleton class.
    /// Manages filters and their gain values.
    /// </summary>
    public class FilterManager
    {
        #region event handlers

        /// <summary>
        /// Triggered whenever the <see cref="PreAmpGain"/> is changed.
        /// </summary>
        public EventHandler volumeChangedEvent;
        
        /// <summary>
        /// Triggered whenever a <see cref="Filters"/> is added.
        /// </summary>
        public EventHandler filterAddedEvent;

        /// <summary>
        /// Triggered whenever a <see cref="Filters"/> is removed.
        /// </summary>
        public EventHandler filterRemovedEvent;

        /// <summary>
        /// Triggered whenever a <see cref="Filters"/> is changed.
        /// </summary>
        public EventHandler filterChangedEvent;

        /// <summary>
        /// Triggered whenever <see cref="IsEqualizerApplied"/> is changed.
        /// </summary>
        public EventHandler equalizerAppliedEvent;

        #endregion

        #region fields/properties

        /// <summary>
        /// The singlton instance of this class.
        /// </summary>
        private static FilterManager instance;

        /// <summary>
        /// The preamp (aka volume) gain.
        /// </summary>
        private double preAmpGain;
        /// <summary>
        /// The preamp (aka volume) gain.
        /// When changed, triggers <see cref="volumeChangedEvent"/>.
        /// </summary>
        public double PreAmpGain
        {
            get { return preAmpGain; }
            set
            {
                preAmpGain = Math.Min(
                    Math.Max(
                        -EqualizerManager.MAX_PREAMP_GAIN,
                        value),
                    EqualizerManager.MAX_PREAMP_GAIN);
                if (volumeChangedEvent != null)
                {
                    volumeChangedEvent(this, EventArgs.Empty);
                }
            }
        }
        
        /// <summary>
        /// Represents the state of the equalizer (applied or no?)
        /// </summary>
        private bool isEqualizerApplied;
        /// <summary>
        /// Represents the state of the equalizer (applied or no?)
        /// When changed, triggers <see cref="equalizerAppliedEvent"/>.
        /// </summary>
        public bool IsEqualizerApplied
        {
            get { return isEqualizerApplied; }
            set
            {
                isEqualizerApplied = value;
                if (equalizerAppliedEvent != null)
                {
                    equalizerAppliedEvent(this, EventArgs.Empty);
                }
            }
        }
        
        /// <summary>
        /// Set of all filters applied to the current <see cref="Track"/>.
        /// </summary>
        public SortedDictionary<double,Filter> Filters { get; private set; }

        /// <summary>
        /// Optimize fired events.
        /// Don't fire lots of events while in the process of changing filters.
        /// Instead, store up the changes and fire them all at once.
        /// </summary>
        /// <seealso cref="filtersChangedLog"/>
        /// <seealso cref="filtersAddedLog"/>
        /// <seealso cref="filtersRemovedLog"/>
        private bool settingNewGainValues;
        
        /// <summary>
        /// Optimization.
        /// Only trigger <see cref="filterChangedEvent"/> if true.
        /// Used to bypass event triggers individually and instead fire
        /// events all at once.
        /// </summary>
        private bool passFilterChangedEvents;
        
        /// <summary>
        /// List of filters that have been changed since
        /// the last event was triggered.
        /// </summary>
        private SortedSet<int> filtersChangedLog;

        /// <summary>
        /// Optimization.
        /// Only trigger <see cref="filterAddedEvent"/> if true.
        /// Used to bypass event triggers individually and instead fire
        /// events all at once.
        /// </summary>
        private bool passFiltersAddedEvent;

        /// <summary>
        /// List of filters that have been added since
        /// the last event was triggered.
        /// </summary>
        private SortedSet<int> filtersAddedLog;

        /// <summary>
        /// Optimization.
        /// Only trigger <see cref="filterRemovedEvent"/> if true.
        /// Used to bypass event triggers individually and instead fire
        /// events all at once.
        /// </summary>
        private bool passFiltersRemovedEvent;

        /// <summary>
        /// List of filters that have been removed since
        /// the last event was triggered.
        /// </summary>
        private SortedSet<int> filtersRemovedLog;

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class, including
        /// initializing many of the fields.
        /// </summary>
        public FilterManager()
        {
            PrintLine();
            Filters = new SortedDictionary<double, Filter>();
            settingNewGainValues = false;
            passFilterChangedEvents = true;
            passFiltersAddedEvent = true;
            passFiltersRemovedEvent = true;
            filtersChangedLog = new SortedSet<int>();
            filtersAddedLog = new SortedSet<int>();
            filtersRemovedLog = new SortedSet<int>();
        }

        /// <summary>
        /// Gets/creates the singleton instance of this class.
        /// </summary>
        /// <returns></returns>
        public static FilterManager GetInstance() {
            PrintLine();
            if (FilterManager.instance == null)
            {
                FilterManager.instance = new FilterManager();
            }
            return FilterManager.instance;
        }

        /// <summary>
        /// Calls <see cref="Close"/>
        /// </summary>
        ~FilterManager() {
            PrintLine();
            Close();
        }

        /// <summary>
        /// Calls <see cref="Clear"/> and
        /// removes the in-class private reference to the singleton instance.
        /// </summary>
        public void Close() {
            PrintLine();
            Clear();
            instance = null;
        }

        /// <summary>
        /// Removes all filters, including triggering the filterRemovedEvent.
        /// </summary>
        public void Clear()
        {
            PrintLine();
            if (Filters.Count > 0)
            {
                // log the filters affected
                int[] affected = new int[Filters.Count];
                for (int i = 0; i < affected.Length; i++) {
                    affected[i] = i;
                }
                FilterEventArgs args = new FilterEventArgs(affected);

                // remove filters and call removal event
                Filters.Clear();
                if (filterRemovedEvent != null)
                {
                    filterRemovedEvent(this, args);
                }
            }
        }

        /// <summary>
        /// Adds, removes, and updates all filters with the given gain values.
        /// Then mass fires the events for the added, removed, or changed filters.
        /// </summary>
        /// <param name="newFilterGains">A set of gain values, one for each filter.</param>
        public void SetNewGainValues(string[] newFilterGains)
        {
            PrintLine();
            settingNewGainValues = true;
            passFilterChangedEvents = false;

            // remove unnecessary filters
            passFiltersRemovedEvent = false;
            while (Filters.Count > newFilterGains.Length)
            {
                RemoveFilter();
            }
            MassFireFiltersRemoved();
            passFiltersRemovedEvent = true;

            // go through existing filters
            int filterIndex = -1;
            foreach (KeyValuePair<double,Filter> pair in Filters)
            {
                filterIndex++;
                Filter filter = pair.Value;
                double gain = Convert.ToDouble(newFilterGains[filterIndex]);

                // check that the gain will change
                if (Math.Abs(filter.Gain - gain) < EqualizerManager.GAIN_ACCURACY)
                {
                    continue;
                }
                filtersChangedLog.Add(filterIndex);

                // change the gain
                bool locked = filter.IsLocked;
                filter.IsLocked = false;
                filter.Gain = gain;
                filter.IsLocked = locked;
            }

            // add necessary filters
            passFiltersAddedEvent = false;
            for(filterIndex = Filters.Count; filterIndex < newFilterGains.Length; filterIndex++)
            {
                AddFilter(Convert.ToDouble(
                    newFilterGains[filterIndex]));
            }
            MassFireFiltersAdded();
            passFiltersAddedEvent = true;

            // pass along event handlers
            settingNewGainValues = false;
            passFilterChangedEvents = true;
            MassFireFiltersChanged();
            MassFireFiltersAdded();
            MassFireFiltersRemoved();
        }

        /// <summary>
        /// Removes the last filter.
        /// Triggers <see cref="filterRemovedEvent"/>
        /// </summary>
        public void RemoveFilter()
        {
            PrintLine();
            // remove the filter
            Filters.Remove(Filters.Keys.Last());
            filtersRemovedLog.Add(Filters.Count);

            // update the other filters
            UpdateFilters();

            // fire the filter removed event
            if (filterRemovedEvent != null &&
                passFiltersRemovedEvent)
            {
                filterRemovedEvent(this, new FilterEventArgs(
                    new int[] { Filters.Count }));
            }
        }

        /// <summary>
        /// Adds a new filter with gain 0.
        /// Adjusts the frequency and Q values of the other filters.
        /// Triggers <see cref="filterAddedEvent"/>.
        /// </summary>
        /// <param name="gain"></param>
        public void AddFilter(double gain)
        {
            PrintLine();
            // create the new filter
            Dictionary<string, double> parameters =
                Filter.GenerateFilterParameters(Filters.Count + 1, Filters.Count);
            Filter filter = new Filter(
                parameters["frequency"], gain, parameters["Q"]);
            filter.FilterChanged += new EventHandler(FilterChanged);
            filter.IsLocked = true;

            // update the changed logs
            filtersAddedLog.Add(Filters.Count - 1);

            // update the frequency/Q on the other filters
            UpdateFilters();

            // add to dictionary of filters
            Filters.Add(parameters["frequency"], filter);

            // fire the new filter event
            if (filterAddedEvent != null &&
                passFiltersAddedEvent)
            {
                filterAddedEvent(this, new FilterEventArgs(
                    new int[] { Filters.Count - 1 }));
            }
        }

        /// <summary>
        /// Find the filter and get its index.
        /// </summary>
        /// <param name="filter">The filter to search for.</param>
        /// <returns>The filter's index, -1 on failure.</returns>
        public int GetFilterIndex(Filter filter)
        {
            PrintLine();
            // get the filter index
            int index = 0;
            foreach (KeyValuePair<double, Filter> pair in Filters)
            {
                if (pair.Value == filter)
                {
                    break;
                }
                index++;
            }

            // check the index
            if (index >= Filters.Count)
            {
                return -1;
            }
            return index;
        }

        #endregion

        #region private methods

        /// <summary>
        /// Updates all filters with new frequency and Q values based on
        /// the number of filters that there are, to evenly space the filters.
        /// </summary>
        private void UpdateFilters()
        {
            PrintLine();
            passFilterChangedEvents = false;

            // update the frequencies and Q's of the other filters
            for (int i = 0; i < Filters.Count; i++)
            {
                Filter filter = Filters.ElementAt(i).Value;
                Dictionary<string, double> parameters =
                    Filter.GenerateFilterParameters(Filters.Count + 1, Filters.Count);
                filter.Frequency = parameters["frequency"];
                filter.Q = parameters["Q"];
                filtersChangedLog.Add(i);
            }

            passFilterChangedEvents = true;
            MassFireFiltersChanged();
        }

        /// <summary>
        /// Triggers <see cref="filterAddedEvent"/> with the
        /// indices of all filters in the <see cref="filtersChangedLog"/>.
        /// Clears the <see cref="filtersChangedLog"/>.
        /// </summary>
        private void MassFireFiltersChanged()
        {
            PrintLine();

            // check pre-conditions
            if (filterChangedEvent == null ||
                settingNewGainValues ||
                filtersChangedLog.Count == 0)
            {
                return;
            }

            // fire event with list of changed filters
            filterChangedEvent(this, new FilterEventArgs(filtersChangedLog.ToArray()));
            filtersChangedLog.Clear();
        }

        /// <summary>
        /// Triggers <see cref="filterAddedEvent"/> with the
        /// indices of all filters in the <see cref="filtersAddedLog"/>.
        /// Clears the <see cref="filtersAddedLog"/>.
        /// </summary>
        private void MassFireFiltersAdded()
        {
            PrintLine();

            // check pre-conditions
            if (filterAddedEvent == null ||
                settingNewGainValues ||
                filtersAddedLog.Count == 0)
            {
                return;
            }

            // fire event with list of added filters
            filterAddedEvent(this, new FilterEventArgs(filtersAddedLog.ToArray()));
            filtersAddedLog.Clear();
        }

        /// <summary>
        /// Triggers <see cref="filterRemovedEvent"/> with the
        /// indices of all filters in the <see cref="filtersRemovedLog"/>.
        /// Clears the <see cref="filtersRemovedLog"/>.
        /// </summary>
        private void MassFireFiltersRemoved()
        {
            PrintLine();

            // check pre-conditions
            if (filterRemovedEvent == null ||
                settingNewGainValues ||
                filtersRemovedLog.Count == 0)
            {
                return;
            }

            // fire event with list of Removed filters
            filterRemovedEvent(this, new FilterEventArgs(filtersRemovedLog.ToArray()));
            filtersRemovedLog.Clear();
        }

        /// <summary>
        /// Callback for <see cref="Filter.FilterChanged"/>.
        /// Triggers <see cref="filterChangedEvent"/> whenever a filter is changed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void FilterChanged(object sender, EventArgs args)
        {
            PrintLine();
            if (filterChangedEvent == null ||
                !passFilterChangedEvents)
            {
                return;
            }

            // get and check the filter index
            int index = GetFilterIndex(sender as Filter);
            if (index < 0)
            {
                return;
            }

            // pass the event
            filterChangedEvent(this, new FilterEventArgs(new int[] { index }));
        }

        #endregion

        #region classes

        public class FilterEventArgs : EventArgs
        {
            public int[] filterIndices;
            public FilterEventArgs(int[] indices)
            {
                filterIndices = indices;
            }
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":FM");
        }
    }
}
