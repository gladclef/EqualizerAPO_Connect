using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace equalizerapo_connect_universal
{
    public class FilterManager
    {
        #region constants

        public const double GAIN_ACCURACY = 0.1;
        public const double MAX_PREAMP_GAIN = 30;
        public const double GAIN_MAX = 15;

        #endregion

        #region events

        public EventHandler volumeChangedEvent;
        public EventHandler filterAddedEvent;
        public EventHandler filterRemovedEvent;
        public EventHandler filterChangedEvent;
        public EventHandler equalizerAppliedEvent;

        #endregion

        #region fields/properties

        private static FilterManager instance;
        private double preAmpGain;
        public double PreAmpGain
        {
            get { return preAmpGain; }
            set
            {
                preAmpGain = Math.Min(
                    Math.Max(
                        -MAX_PREAMP_GAIN,
                        value),
                    MAX_PREAMP_GAIN);
                if (volumeChangedEvent != null)
                {
                    volumeChangedEvent(this, EventArgs.Empty);
                }
            }
        }
        private bool isEqualizerApplied;
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
        public SortedDictionary<double,Filter> Filters { get; private set; }

        // optimize fired events
        private bool settingNewGainValues;
        private bool passFilterChangedEvents;
        private SortedSet<int> filtersChangedLog;
        private bool passFiltersAddedEvent;
        private SortedSet<int> filtersAddedLog;
        private bool passFiltersRemovedEvent;
        private SortedSet<int> filtersRemovedLog;

        #endregion

        #region public methods

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

        public static FilterManager GetInstance() {
            PrintLine();
            if (FilterManager.instance == null)
            {
                FilterManager.instance = new FilterManager();
            }
            return FilterManager.instance;
        }

        ~FilterManager() {
            PrintLine();
            Close();
        }

        public void Close() {
            PrintLine();
            Clear();
            instance = null;
        }

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
                if (Math.Abs(filter.Gain - gain) < GAIN_ACCURACY)
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
