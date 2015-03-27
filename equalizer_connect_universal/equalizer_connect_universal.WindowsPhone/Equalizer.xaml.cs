using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace equalizer_connect_universal
{
    /// <summary>
    /// The primarily useful page of the application. This is the page from
    /// which filters are created, destroyed, and edited. The volume is also
    /// changed from the this page and the song playback.
    /// 
    /// There are event handlers here from most of the application's other
    /// classes and event handlers from the GUI objects.
    /// </summary>
    public partial class Equalizer
    {
        #region constants

        #endregion

        #region fields/properties

        // managers

        /// <summary>
        /// reference to the connection object, which handles
        /// incoming and outgoing messages. Also generates connection,
        /// disconnection, and incoming message events.
        /// </summary>
        private Connection connection;
        /// <summary>
        /// reference to the object that creates and parses messagse to be sent
        /// to/handled from the server.
        /// </summary>
        private MessageParser messageParser;
        /// <summary>
        /// reference to the object that manages filters, their values,
        /// and updates their values. Also handles filter changed events.
        /// </summary>
        private FilterManager filterManager;
        /// <summary>
        /// Manages the state of the equalizer, including playback and constants
        /// </summary>
        private EqualizerManager equalizerManager;

        // object references

        /// <summary>
        /// UI Sliders linked to the <see cref="filterManager">filters</see>.
        /// </summary>
        private LinkedList<Slider> filterSliders;
        /// <summary>
        /// UI Textboxes linked to the <see cref="filterManager">filters</see>.
        /// </summary>
        private LinkedList<TextBox> filterTextboxes;
        /// <summary>
        /// Set of rectangles representing the gains of the
        /// <see cref="filterManager">filters</see>.
        /// </summary>
        private LinkedList<Rectangle> rectGraphicRep;

        // cached values

        /// <summary>
        /// Optimization to keep the scrollviewer from being updated
        /// too often. Possibly necessary to keep from firing an endless
        /// chain of events.
        /// </summary>
        private double cachedScrollOffset;
        /// <summary>
        /// True while the screen of the device is being touched.
        /// False when not touched.
        /// Primary use: to prevent execution of feedback value updates while UI controls are active.
        /// </summary>
        public bool IsTouchActive { get; private set; }
        /// <summary>
        /// To force the <see cref="Equalizer"/> object to be reinitialized
        /// when the Equalizer.xaml page is reloaded.
        /// <seealso cref="Init"/>
        /// <seealso cref="Close"/>
        /// <seealso cref="OnNavigatedFrom"/>
        /// <seealso cref="OnNavigatedTo"/>
        /// </summary>
        private bool initCalled;

        // primary values

        /// <summary>
        /// Optimization. Used to reduce the number of times
        /// that textboxes have to be redrawn.
        /// </summary>
        private DispatcherTimer filterChangedTimer;
        /// <summary>
        /// Updates the scrollviewer with a new horizontal offset that
        /// enables smooth scrolling to a position.
        /// </summary>
        private DispatcherTimer updateScrollTimer;
        /// <summary>
        /// Checks periodically to see if the scrollviewer
        /// horizontal offset has been changed.
        /// </summary>
        private DispatcherTimer checkScrollTimer;
        /// <summary>
        /// Used for smooth scrolling. Possible keys are:
        /// rectLeftOld
        /// rectLeftNew
        /// scrollOld
        /// scrollNew
        /// iteration
        /// <seealso cref="updateScollTimer"/>
        /// </summary>
        private Dictionary<string, double> scrollValues;
        /// <summary>
        /// index of the currently selected slider/filter
        /// <seealso cref="filterManager"/>
        /// </summary>
        private int selectedSliderIndex;
        /// <summary>
        /// When set, also moves the selection rectangle
        /// and smoothly scrolls the scrollviewer.
        /// </summary>
        public int SelectedSliderIndex
        {
            get { return selectedSliderIndex; }
            set
            {
                // check that the value has changed
                if (selectedSliderIndex == value)
                {
                    return;
                }

                // lock the old filter
                if (selectedSliderIndex >= 0 &&
                    filterManager != null &&
                    selectedSliderIndex < filterManager.Filters.Count)
                {
                    filterManager.Filters.ElementAt(selectedSliderIndex).Value.IsLocked = true;
                }

                if (value != -1)
                {
                    // a slider has been selected

                    // unlock the new filter
                    int oldIndex = selectedSliderIndex;
                    selectedSliderIndex = Math.Max(
                        Math.Min(
                            filterSliders.Count - 1,
                            value),
                        0);
                    filterManager.Filters.ElementAt(selectedSliderIndex).Value.IsLocked = false;

                    // visibly show that the filter is selected
                    stackpanel_standard_controls.Visibility = Visibility.Collapsed;
                    stackpanel_filter_controls.Visibility = Visibility.Visible;
                    rectangle_selected.Margin = new Thickness(
                        (oldIndex == -1)
                            ? filterSliders.ElementAt(selectedSliderIndex).Margin.Left
                            : rectangle_selected.Margin.Left,
                        0, 0, 0);
                    scrollValues["rectLeftOld"] = rectangle_selected.Margin.Left;
                    scrollValues["rectLeftNew"] = filterSliders.ElementAt(selectedSliderIndex).Margin.Left;
                    rectangle_selected.Visibility = Visibility.Visible;

                    // smoothly scroll the selection rectangle and scrollviewer
                    ScrollToFilter(selectedSliderIndex);
                }
                else
                {
                    // all sliders deselected
                    selectedSliderIndex = value;
                    stackpanel_standard_controls.Visibility = Visibility.Visible;
                    stackpanel_filter_controls.Visibility = Visibility.Collapsed;
                    rectangle_selected.Visibility = Visibility.Collapsed;
                    if (checkScrollTimer != null)
                    {
                        checkScrollTimer.Stop();
                    }
                }

                UpdateGraphicalRepresentation();
            }
        }
        /// <summary>
        /// Used in place of Thread.Sleep()
        /// </summary>
        private static ManualResetEvent neverTrigger = new ManualResetEvent(false);

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class.
        /// Calls <see cref="Init"/>.
        /// </summary>
        public Equalizer()
        {
            PrintLine();
            Init();
        }

        /// <summary>
        /// Initialize all local fields, subscribe to events, and
        /// set the flag <see cref="initCalled"/> to 1.
        /// </summary>
        public void Init()
        {
            PrintLine();
            InitializeComponent();

            // close everything if navigating away from this page
            Windows.Phone.UI.Input.HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // set min/max on volume and attach event handlers
            slider_volume.Minimum = -EqualizerManager.MAX_PREAMP_GAIN;
            slider_volume.Maximum = EqualizerManager.MAX_PREAMP_GAIN;
            slider_volume.ValueChanged += slider_volume_ValueChanged;
            textbox_volume.TextChanged += textbox_volume_TextChanged;
            textbox_volume.KeyUp += textbox_volume_KeyUp;

            // create new instances of things and set initial values
            filterSliders = new LinkedList<Slider>();
            filterTextboxes = new LinkedList<TextBox>();
            connection = Connection.GetInstance();
            equalizerManager = new EqualizerManager();
            filterManager = FilterManager.GetInstance();
            messageParser = new MessageParser(filterManager, equalizerManager);
            scrollValues = new Dictionary<string, double>();
            rectGraphicRep = new LinkedList<Rectangle>();
            SelectedSliderIndex = -1;

            // get updates from the equalizerManager
            equalizerManager.PlaybackUpdatedEvent += UpdatePlayback;
            equalizerManager.TrackChangedEvent += UpdateTrackname;

            // check for updates from the viewscroller
            checkScrollTimer = new DispatcherTimer();
            checkScrollTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            checkScrollTimer.Tick += CheckScrolled;

            // smooth scrolling when the selected filter is changed
            updateScrollTimer = new DispatcherTimer();
            updateScrollTimer.Interval = new TimeSpan(0, 0, 0, 0, 30);
            updateScrollTimer.Tick += UpdateScrollPosition;

            // try and reduce the number of times the textbox has to change
            filterChangedTimer = new DispatcherTimer();
            filterChangedTimer.Interval = new TimeSpan(0, 0, 0, 0, 30);
            filterChangedTimer.Tick += UpdateFilterTextBoxes;

            // get updates from the filters
            filterManager.filterAddedEvent += UpdateFilters;
            filterManager.filterRemovedEvent += UpdateFilters;
            filterManager.filterChangedEvent += UpdateFilters;
            filterManager.volumeChangedEvent += UpdateVolume;
            filterManager.equalizerAppliedEvent += UpdateIsEqualizerApplied;

            // set up connection
            connection.MessageRecievedEvent += MessageReceived;
            connection.DisconnectedEvent += Disconnected;
            connection.DisconnectMe += ConnectionSideDisconnect;

            initCalled = true;
        }

        /// <summary>
        /// Calls <see cref="Close"/> when destroyed.
        /// </summary>
        ~Equalizer()
        {
            PrintLine();
            try
            {
                Close();
            }
            catch (UnauthorizedAccessException)
            {
                Close();
            }
        }

        /// <summary>
        /// Ends all connections, timers, clears out all collections,
        /// unselects the slider, closes all composing objects, and
        /// sets <see cref="initCalled"/> to 0.
        /// </summary>
        public void Close()
        {
            PrintLine();
            connection.Close();
            filterManager.Close();
            try
            {
                filterChangedTimer.Stop();
            }
            catch (Exception) { }
            try
            {
                updateScrollTimer.Stop();
            }
            catch (Exception) { }
            try
            {
                checkScrollTimer.Stop();
            }
            catch (Exception) { }
            filterSliders.Clear();
            filterTextboxes.Clear();
            scrollValues.Clear();
            selectedSliderIndex = -1;
            rectGraphicRep.Clear();
            cachedScrollOffset = 0;
            initCalled = false;
        }

        /// <summary>
        /// Updates the nice rectangle on the graphical representation of
        /// the filters so that it fits over the sliders actually being
        /// displayed.
        /// </summary>
        public void UpdateGraphicalRepresentationBorder()
        {
            PrintLine();
            // get some values
            double scrollWidth = Math.Max(
                scrollviewer_equalizer.ActualWidth,
                1);
            double extentWidth = Math.Max(
                scrollviewer_equalizer.ExtentWidth,
                1);
            double gwidth = grid_graphical_representation.Width;
            double hoffset = scrollviewer_equalizer.HorizontalOffset;

            // calculate new width/offset
            double widthPercent = Math.Min(
                scrollWidth / extentWidth,
                1);
            double width = gwidth * widthPercent;
            double scrollRange = extentWidth - scrollWidth;
            double offsetPercent = (scrollRange == 0)
                ? 0
                : hoffset / scrollRange;
            double offsetRange = gwidth - width;
            double offset = offsetRange * offsetPercent;

            // update with the new width/offset
            if (border_graphical_representation.Width != width)
            {
                border_graphical_representation.Width = width;
            }
            if (border_graphical_representation.Margin.Left != offset)
            {
                border_graphical_representation.Margin = new Thickness(
                    offset, 0, 0, 0);
            }
        }

        #endregion

        #region protected methods

        /// <summary>
        /// Calls <see cref="Init"/> if <see cref="initCalled"/> = 0.
        /// </summary>
        /// <param name="e">Passed along to the base method.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            PrintLine();
            base.OnNavigatedTo(e);
            if (!initCalled)
            {
                Init();
            }
        }

        /// <summary>
        /// Calls <see cref="Close"/>.
        /// </summary>
        /// <param name="e">Passed to the base method.</param>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            PrintLine();
            base.OnNavigatedFrom(e);
            Close();
        }

        #endregion

        #region private methods

        /// <summary>
        /// Go back to the connection page and call <see cref="Close"/>,
        /// because that's apparently not something WP apps do anymore.
        /// </summary>
        /// <param name="sender">The phone? N/A</param>
        /// <param name="e">N/A</param>
        private void HardwareButtons_BackPressed(object sender, Windows.Phone.UI.Input.BackPressedEventArgs e)
        {
            if (Frame.CurrentSourcePageType == this.GetType() &&
                !e.Handled)
            {
                e.Handled = true;
                Close();
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
                else
                {
                    Frame.Navigate(typeof(ConnectToServer));
                }
            }
        }

        /// <summary>
        /// Callback for <see cref="Connection.MessageRecievedEvent"/>.
        /// If <see cref="IsTouchActive"/> is false, then
        /// handles the message in <see cref="MessageParser.ParseMessage"/>.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">A Connection.MessageReceivedEventArgs object</param>
        /// <seealso cref="LayoutRoot_MouseEnter"/>
        /// <seealso cref="LayoutRoot_MouseLeave"/>
        private void MessageReceived(object sender, EventArgs args)
        {
            PrintLine();
            if (IsTouchActive)
            {
                return;
            }
            var mrev = (Connection.MessageReceivedEventArgs)args;
            messageParser.ParseMessage(mrev.message);
        }

        /// <summary>
        /// Callback for <see cref="Connection.DisconnectedEvent"/>.
        /// Navigates back a frame.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void Disconnected(object sender, EventArgs args)
        {
            PrintLine();
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ConnectToServer));
            }
        }

        /// <summary>
        /// Callback for <see cref="Connection.DisconnectMe"/>.
        /// Calls <see cref="Connection.SideDisconnect"/>.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void ConnectionSideDisconnect(object sender, EventArgs args)
        {
            PrintLine();
            connection.SideDisconnect();
        }

        /// <summary>
        /// Callback for <see cref="EqualizerManager.TrackChangedEvent"/>.
        /// Updates the displayed artist/track title.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">A Track.TrackChangedEventArgs object.</param>
        private void UpdateTrackname(object sender, object args)
        {
            PrintLine();
            var targs = args as Track.TrackChangedEventArgs;
            textblock_now_playing.Text =
                "Now Playing: " +
                equalizerManager.CurrentTrack.Artist + " - " +
                equalizerManager.CurrentTrack.Title;
        }

        /// <summary>
        /// Removes sliders until the number of sliders matches the number of filters.
        /// </summary>
        private void RemoveSliders()
        {
            PrintLine();
            SortedDictionary<double, Filter> filters = filterManager.Filters;

            // remove as many sliders as are necessary
            while (filterSliders.Count > filters.Count)
            {
                grid_equalizer.Children.Remove(
                    filterSliders.Last.Value);
                filterSliders.RemoveLast();
                grid_equalizer_numbers.Children.Remove(
                    filterTextboxes.Last.Value);
                filterTextboxes.RemoveLast();
            }
        }

        /// <summary>
        /// Adds sliders until the number of sliders matches the number of filters.
        /// </summary>
        private void AddSliders()
        {
            PrintLine();
            SortedDictionary<double, Filter> filters = filterManager.Filters;

            // add as many sliders as are necessary
            while (filterSliders.Count < filters.Count)
            {
                // create slider
                Thickness margin = slider_volume.Margin;
                Slider slider = new Slider();
                slider.Width = slider_volume.Width;
                slider.Height = grid_equalizer.Height;
                slider.Minimum = -EqualizerManager.GAIN_MAX;
                slider.Maximum = EqualizerManager.GAIN_MAX;
                slider.Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
                slider.Foreground = GetBrush("PhoneAccentBrush");
                slider.Orientation = slider_volume.Orientation;
                slider.Margin = new Thickness(
                    (slider.Width + margin.Left) * filterSliders.Count,
                    margin.Top, margin.Right, margin.Bottom);
                slider.HorizontalAlignment = HorizontalAlignment.Left;
                slider.ValueChanged +=
                    new RangeBaseValueChangedEventHandler(
                        slider_filter_ValueChanged);
                slider.Tapped +=
                    new TappedEventHandler(
                        slider_filter_Tapped);
                grid_equalizer.Children.Add(slider);
                filterSliders.AddLast(slider);

                // create the textbox input scope
                InputScope numScope = new InputScope();
                InputScopeName scopeName = new InputScopeName();
                scopeName.NameValue = InputScopeNameValue.CurrencyAmountAndSymbol;
                numScope.Names.Add(scopeName);

                // create textbox
                TextBox textbox = new TextBox();
                textbox.Width = textbox_volume.Width;
                textbox.Height = grid_equalizer_numbers.Height;
                textbox.Background = textbox_volume.Background;
                textbox.BorderBrush = textbox_volume.BorderBrush;
                textbox.BorderThickness = textbox_volume.BorderThickness;
                textbox.Margin = new Thickness(textbox.Width * filterTextboxes.Count, 0, 0, 0);
                textbox.HorizontalAlignment = HorizontalAlignment.Left;
                textbox.TextChanged += 
                    new TextChangedEventHandler(
                        textbox_filter_TextChanged);
                textbox.KeyUp +=
                    new KeyEventHandler(
                        textbox_filter_KeyUp);
                textbox.InputScope = numScope;
                grid_equalizer_numbers.Children.Add(textbox);
                filterTextboxes.AddLast(textbox);
            }
        }

        /// <summary>
        /// Matches the sliders to the filters and their gains, and
        /// updates the graphical representation of the filters.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">A FilterManager.FilterEventArgs object</param>
        private void UpdateFilters(object sender, EventArgs args)
        {
            PrintLine();
            SortedDictionary<double, Filter> filters = filterManager.Filters;

            // update the number of sliders/textboxes
            RemoveSliders();
            AddSliders();
            UpdateGraphicalRepresentation();

            // get the arguments
            var filterArgs = args as FilterManager.FilterEventArgs;

            int index = -1;
            foreach (KeyValuePair<double, Filter> pair in filters)
            {
                // check that this filter needs to be updated
                index++;
                if (!filterArgs.filterIndices.Contains(index))
                {
                    continue;
                }
                Filter filter = pair.Value;
                double gain = filter.Gain;

                // update the slider
                Slider slider = filterSliders.ElementAt(index);
                if (Math.Abs(slider.Value - gain) >= EqualizerManager.GAIN_ACCURACY)
                {
                    slider.Value = gain;
                }

                // set timer to update the textbox
                filterChangedTimer.Stop();
                filterChangedTimer.Start();
            }
        }

        /// <summary>
        /// Updates the textbox for the volume slider with the
        /// correct gain value.
        /// </summary>
        private void UpdateVolumeTextbox()
        {
            PrintLine();
            string sgain = slider_volume.Value.ToString();
            int tenthsPosition = sgain.Contains('.') ?
                sgain.IndexOf('.') + 2 : sgain.Length;
            sgain = sgain.Substring(0, tenthsPosition);
            if (textbox_volume.Text != sgain)
            {
                textbox_volume.Text = sgain;
            }
        }

        /// <summary>
        /// Updates the textboxes associated with the filters with the current
        /// gain values from the filters.
        /// </summary>
        /// <param name="sender">Object that called this method.</param>
        /// <param name="args">Event arguments for this call.</param>
        private void UpdateFilterTextBoxes(object sender, object args)
        {
            PrintLine();
            filterChangedTimer.Stop();

            SortedDictionary<double, Filter> filters = filterManager.Filters;
            TextBox[] textboxes = filterTextboxes.ToArray();

            int index = -1;
            foreach (KeyValuePair<double, Filter> pair in filters)
            {
                // get the filter, its index, its gain, and the textBox
                index++;
                Filter filter = pair.Value;
                TextBox textBox = textboxes[index];
                double gain = filter.Gain;

                // update the textbox
                string sgain = gain.ToString();
                int tenthsPosition = sgain.Contains('.') ?
                    sgain.IndexOf('.') + 2 : sgain.Length;
                sgain = sgain.Substring(0, tenthsPosition);
                if (sgain != textBox.Text)
                {
                    textBox.Text = sgain;
                }
            }
        }

        /// <summary>
        /// Callback for <see cref="FilterManager.volumeChangedEvent"/>.
        /// Updates the value of the volume slider to match the new gain value.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void UpdateVolume(object sender, EventArgs args)
        {
            PrintLine();
            Slider slider = slider_volume;
            double gain = filterManager.PreAmpGain;
            if (gain != slider.Value)
            {
                slider.Value = gain;
            }
        }

        /// <summary>
        /// Callback for <see cref="EqualizerManager.PlaybackUpdatedEvent"/>.
        /// Changes which playback button is visible (playing or paused?).
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void UpdatePlayback(object sender, object args)
        {
            PrintLine();
            if (equalizerManager.IsPlaying)
            {
                // is playing!
                if (button_play.Visibility == Visibility.Visible)
                {
                    button_play.Visibility = Visibility.Collapsed;
                    button_pause.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // is paused!
                if (button_play.Visibility == Visibility.Collapsed)
                {
                    button_play.Visibility = Visibility.Visible;
                    button_pause.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Callback for <see cref="FilterManager.equalizerAppliedEvent"/>.
        /// Checks the "apply filter" checkbox or unchecks it.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void UpdateIsEqualizerApplied(object sender, EventArgs args)
        {
            PrintLine();
            checkbox_apply_equalizer.IsChecked = 
                filterManager.IsEqualizerApplied;
        }

        /// <summary>
        /// Gets a brush by name from the Application resources.
        /// </summary>
        /// <param name="brushName">Eg: "PhoneAccentBrush"</param>
        /// <returns>A new SolidColorBrush with the associated color.</returns>
        private static SolidColorBrush GetBrush(string brushName)
        {
            PrintLine();
            return new SolidColorBrush((Application.Current.Resources[brushName] as SolidColorBrush).Color);
        }

        /// <summary>
        /// Gets the filter index that the given slider corresponds to.
        /// </summary>
        /// <param name="slider">The slider</param>
        /// <returns>The index, or -1 if not found.</returns>
        private int GetSliderIndex(Slider slider)
        {
            PrintLine();
            int index = 0;
            foreach (Slider s in filterSliders)
            {
                if (s == slider)
                {
                    break;
                }
                index++;
            }
            if (index >= filterSliders.Count)
            {
                index = -1;
            }
            return index;
        }

        /// <summary>
        /// Callback for the <see cref="updateScrollTimer"/>.
        /// Updates the position of the sliders scrollviewer and the
        /// background rectangle that highlights the selected slider.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        /// <seealso cref="scrollValues"/>
        private void UpdateScrollPosition(object sender, object args)
        {
            PrintLine();
            // check that the animation isn't over
            double maxIterations = 300 / updateScrollTimer.Interval.Milliseconds;
            if (scrollValues["iteration"] > maxIterations)
            {
                updateScrollTimer.Stop();
                rectangle_selected.Margin = new Thickness(
                    scrollValues["rectLeftNew"], 0, 0, 0);
                scrollviewer_equalizer.ChangeView(scrollValues["scrollNew"], 0, 0);
                scrollValues["iteration"] = 0;
                return;
            }

            // get new animation values
            double iteration = scrollValues["iteration"];
            double rectNew = scrollValues["rectLeftNew"];
            double rectOld = scrollValues["rectLeftOld"];
            double scrollNew = scrollValues["scrollNew"];
            double scrollOld = scrollValues["scrollOld"];
            double progress = Math.Sin(
                Math.PI / 2 * iteration / maxIterations);
            double rectNow = (rectNew - rectOld) * progress + rectOld;
            double scrollNow = (scrollNew - scrollOld) * progress + scrollOld;

            // update UI
            rectangle_selected.Margin = new Thickness(
                rectNow, 0, 0, 0);
            scrollviewer_equalizer.ChangeView(scrollNow, 0, 0);

            // increase iterations
            scrollValues["iteration"] += 1;
        }

        /// <summary>
        /// Given a string that has a double value in it, get the represented double value.
        /// </summary>
        /// <param name="text">The string to match.</param>
        /// <returns>The double, or NaN upon failure.</returns>
        private double GetDoubleFromText(string text)
        {
            System.Text.RegularExpressions.Regex numMatch =
                new System.Text.RegularExpressions.Regex("(-)?\\d+(\\.\\d+)?");
            System.Text.RegularExpressions.Match match =
                numMatch.Match(text);
            if (!match.Success)
            {
                return double.NaN;
            }
            return Convert.ToDouble(match.Value);
        }

        /// <summary>
        /// Updates the gain of the associated filter if
        /// the gain has actually changed and the filter isn't locked.
        /// </summary>
        /// <param name="textBox">The textbox that whose value has changed.</param>
        private void TextBoxTextChanged(TextBox textBox)
        {
            PrintLine();
            // get the index of the textbox
            int index = 0;
            foreach (TextBox t in filterTextboxes)
            {
                if (t == textBox)
                {
                    break;
                }
                index++;
            }
            if (index >= filterSliders.Count)
            {
                System.Diagnostics.Debug.WriteLine("**error: can't find the TextBox");
                return;
            }

            // check that the text is a valid number
            double gain = GetDoubleFromText(textBox.Text);
            if (gain == double.NaN)
            {
                return;
            }

            // check that the value has changed
            Filter filter = filterManager.Filters.ElementAt(index).Value;
            if (Math.Abs(gain - filter.Gain) < EqualizerManager.GAIN_ACCURACY)
            {
                return;
            }

            // is the filter locked? ie, can't change
            if (filter.IsLocked)
            {
                // change value back
                UpdateFilters(this,
                    new FilterManager.FilterEventArgs(new int[] { index }));
            }
            else
            {
                // change filter gain
                filter.Gain = gain;
            }
        }

        /// <summary>
        /// Updates the graphical representation of filters, so that
        /// the number of rectangles and their heights matches the number of
        /// filters and their gains, respectively.
        /// Calls <see cref="UpdateGraphicalRepresentationBorder"/>.
        /// </summary>
        private void UpdateGraphicalRepresentation()
        {
            PrintLine();

            // remove unnecessary rectangles
            bool widthNeedsUpdate = (rectGraphicRep.Count != filterManager.Filters.Count);
            while (rectGraphicRep.Count > filterManager.Filters.Count)
            {
                grid_graphical_representation.Children.Remove(
                    rectGraphicRep.Last.Value);
                rectGraphicRep.RemoveLast();
            }

            // get the new widths for rectangles
            double width = grid_graphical_representation.Width /
                filterSliders.Count;
            double rectWidth = width - 2;
            if (widthNeedsUpdate)
            {
                UpdateGraphicalRepresentationBorder();
            }
            if (filterSliders.Count == 0)
            {
                return;
            }

            // update heights and fills of rectangles, adding rectangles as necessary
            KeyValuePair<double, Filter>[] filters =
                filterManager.Filters.ToArray();
            for (int index = 0; index < filters.Length; index++)
            {
                Filter filter = filters[index].Value;

                // add rectangle?
                Rectangle rect;
                if (rectGraphicRep.Count > index)
                {
                    rect = rectGraphicRep.ElementAt(index);
                }
                else
                {
                    rect = new Rectangle();
                    rect.HorizontalAlignment =
                        HorizontalAlignment.Left;
                    rect.VerticalAlignment =
                        VerticalAlignment.Bottom;
                    rect.Margin = new Thickness(0, 0, 0, 2);
                    rectGraphicRep.AddLast(rect);
                    grid_graphical_representation.Children.Add(rect);
                }

                // update width
                if (rect.Width != rectWidth)
                {
                    rect.Width = rectWidth;
                    rect.Margin = new Thickness(width * index + 2, 0, 0, 2);
                }

                // update height
                double gainPercentage = (filter.Gain + EqualizerManager.GAIN_MAX) /
                    (EqualizerManager.GAIN_MAX * 2);
                double height = (grid_graphical_representation.Height - 4) * gainPercentage;
                if (height != rect.Height)
                {
                    rect.Height = height;
                }

                // update fill
                if (SelectedSliderIndex == index)
                {
                    rect.Fill = GetBrush("PhoneAccentBrush");
                }
                else
                {
                    rect.Fill = rectangle_selected.Fill;
                }
            }
        }

        /// <summary>
        /// Callback for the <see cref="checkScrollTimer"/>.
        /// If the filters scrollviewer scroll value has changed, then
        /// update the graphical representation of the filters.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        /// <seealso cref="UpdateGraphicalRepresentation"/>
        private void CheckScrolled(object sender, object args)
        {
            PrintLine();
            if (scrollviewer_equalizer.Visibility != Visibility.Visible)
            {
                return;
            }
            if (cachedScrollOffset != scrollviewer_equalizer.HorizontalOffset)
            {
                UpdateGraphicalRepresentationBorder();
                cachedScrollOffset = scrollviewer_equalizer.HorizontalOffset;
            }
        }

        /// <summary>
        /// Adjusts the horizontal scroll of the filters scrollviewer to
        /// move to the same position as is being scrolled to on the
        /// graphical representation, currently being used as a region selector.
        /// </summary>
        /// <param name="p">The physical point that was tapped on the grid.</param>
        private void grid_graphical_representation_MoveScrollViewer(Point p)
        {
            PrintLine();
            // get some values
            double scrollWidth = Math.Max(
                scrollviewer_equalizer.ActualWidth,
                1);
            double extentWidth = Math.Max(
                scrollviewer_equalizer.ExtentWidth,
                1);
            double gwidth = grid_graphical_representation.Width;
            double x = Math.Max(
                Math.Min(
                    p.X,
                    gwidth),
                0);

            // calculate offset
            double widthPercent = Math.Min(
                scrollWidth / extentWidth,
                1);
            double width = gwidth * widthPercent;
            double offsetRange = gwidth - width;
            double offsetLeft = x - (width / 2);
            double offsetPercent = offsetLeft / offsetRange;
            double scrollRange = extentWidth - scrollWidth;
            double scroll = Math.Floor(scrollRange * offsetPercent);

            // update the horizontal offset
            if (scrollviewer_equalizer.HorizontalOffset != scroll)
            {
                // stop the timers
                updateScrollTimer.Stop();

                // update
                scrollviewer_equalizer.ChangeView(scroll, 0, 0);
            }
        }

        /// <summary>
        /// Scrolls the filter scrollviewer so that the given filter is in view
        /// (or at least the slider that corresponds to that filter).
        /// </summary>
        /// <param name="filterIndex">The filter you want to show.</param>
        private void ScrollToFilter(int filterIndex)
        {
            // get the scroll to value
            double scrollTo = filterSliders.ElementAt(filterIndex).Margin.Left;

            // scroll the scrollviewer
            scrollValues["scrollOld"] = scrollviewer_equalizer.HorizontalOffset;
            double offset = Math.Min(
                Math.Max(
                    scrollTo + rectangle_selected.Width - scrollviewer_equalizer.ActualWidth,
                    scrollviewer_equalizer.HorizontalOffset),
                scrollTo);
            scrollValues["scrollNew"] = offset;

            // set any values not yet set
            if (!scrollValues.ContainsKey("rectLeftOld"))
            {
                scrollValues["rectLeftOld"] = 0;
            }
            if (!scrollValues.ContainsKey("rectLeftNew"))
            {
                scrollValues["rectLeftNew"] = 0;
            }

            // start the smooth scroll timer
            scrollValues["iteration"] = 0;
            if (updateScrollTimer != null &&
                !updateScrollTimer.IsEnabled)
            {
                updateScrollTimer.Start();
            }
            if (checkScrollTimer != null &&
                !checkScrollTimer.IsEnabled)
            {
                checkScrollTimer.Start();
            }
        }

        #endregion

        #region ui event handlers

        /// <summary>
        /// Selects the slider to the left of the currently selected slider.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_filter_prev_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = Math.Max(
                SelectedSliderIndex - 1,
                0);
        }

        /// <summary>
        /// Decrease the gain of the currently selected filter by 0.1.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_filter_decrease_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            Filter filter = filterManager.Filters.ElementAt(SelectedSliderIndex).Value;
            filter.Gain -= 0.1;
            connection.Send(
                messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

        /// <summary>
        /// Increase the gain of the currently selected filter by 0.1.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_filter_increase_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            Filter filter = filterManager.Filters.ElementAt(SelectedSliderIndex).Value;
            filter.Gain += 0.1;
            connection.Send(
                messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

        /// <summary>
        /// Selects the slider to the right of the currently selected slider.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_filter_next_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = Math.Min(
                SelectedSliderIndex + 1,
                filterSliders.Count - 1);
        }

        /// <summary>
        /// Deselects the currently selected filter,
        /// causing the macro manager buttons to pop back up.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_deselect_filter_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = -1;
        }

        /// <summary>
        /// Selects the given slider and brings up the filter micro controls,
        /// hiding the macro controls.
        /// </summary>
        /// <param name="sender">The slider that was tapped</param>
        /// <param name="args">N/A</param>
        private void slider_filter_Tapped(object sender, TappedRoutedEventArgs args)
        {
            PrintLine();
            // get the index of the slider
            Slider slider = sender as Slider;
            int index = GetSliderIndex(slider);
            if (index == -1)
            {
                System.Diagnostics.Debug.WriteLine("**error: can't find the Slider");
                return;
            }

            // select the slider
            neverTrigger.WaitOne(30);
            SelectedSliderIndex = index;
        }

        /// <summary>
        /// Updates the gain on the volume based on the value of the slider.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void slider_volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            PrintLine();
            // check that the value is new
            if (Math.Abs(slider_volume.Value - filterManager.PreAmpGain) < EqualizerManager.GAIN_ACCURACY)
            {
                return;
            }

            // change volume gain
            filterManager.PreAmpGain = slider_volume.Value;

            // update the textbox
            UpdateVolumeTextbox();

            // send a message to the server
            connection.Send(
                messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                false);
        }

        /// <summary>
        /// Updates the gain on the volume based on the value in the textbox.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void textbox_volume_TextChanged(object sender, TextChangedEventArgs args)
        {
            PrintLine();
            if (textbox_volume.FocusState != FocusState.Unfocused)
            {
                return;
            }

            slider_volume.Value = GetDoubleFromText(textbox_volume.Text);
            connection.Send(
                messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                true);
        }

        /// <summary>
        /// Checks if the key was the enter key. If so, then the gain on the volume
        /// is updated accordingly.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">A KeyRoutedEventArgs object</param>
        private void textbox_volume_KeyUp(object sender, KeyRoutedEventArgs args)
        {
            PrintLine();
            // check pre-conditions
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                slider_volume.Value = GetDoubleFromText(textbox_volume.Text);
                connection.Send(
                    messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                    true);
            }
        }

        /// <summary>
        /// Updates the gain on the filter that corresponds to the given slider,
        /// based on the value of the slider.
        /// </summary>
        /// <param name="sender">The slider</param>
        /// <param name="args">N/A</param>
        private void slider_filter_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            PrintLine();
            // check pre-conditions
            if (!(sender is Slider))
            {
                System.Diagnostics.Debug.WriteLine("**error: sender is not Slider");
                return;
            }

            // get the index of the slider
            Slider slider = sender as Slider;
            int index = GetSliderIndex(slider);
            if (index == -1)
            {
                System.Diagnostics.Debug.WriteLine("**error: can't find the Slider");
                return;
            }

            // check that the value is new
            Filter filter = filterManager.Filters.ElementAt(index).Value;
            if (Math.Abs(slider.Value - filter.Gain) < EqualizerManager.GAIN_ACCURACY)
            {
                return;
            }

            // is the filter locked? ie, can't change
            if (filter.IsLocked)
            {
                // change value back
                slider.Value = filter.Gain;
            }
            else
            {
                // change filter gain
                filter.Gain = slider.Value;
                connection.Send(
                    messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                    false);
            }
        }

        /// <summary>
        /// Updates the gain on the filter that corresponds to the given textbox,
        /// based on the value of the textbox.
        /// </summary>
        /// <param name="sender">The textbox</param>
        /// <param name="args">N/A</param>
        private void textbox_filter_TextChanged(object sender, TextChangedEventArgs args)
        {
            PrintLine();
            // check pre-conditions
            if (!(sender is TextBox))
            {
                System.Diagnostics.Debug.WriteLine("**error: sender is not TextBox");
                return;
            }
            TextBox textBox = sender as TextBox;
            if (textBox.FocusState != FocusState.Unfocused)
            {
                return;
            }

            TextBoxTextChanged(textBox);
            connection.Send(
                messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

        /// <summary>
        /// If the enter key, then
        /// updates the gain on the filter that corresponds to the given textbox,
        /// based on the value of the textbox.
        /// </summary>
        /// <param name="sender">The textbox</param>
        /// <param name="args">A KeyRoutedEventArgs object</param>
        private void textbox_filter_KeyUp(object sender, object args)
        {
            PrintLine();
            // check pre-conditions
            if (!(sender is TextBox))
            {
                System.Diagnostics.Debug.WriteLine("**error: sender is not TextBox");
                return;
            }
            TextBox textBox = sender as TextBox;

            if (!(args is KeyRoutedEventArgs))
            {
                System.Diagnostics.Debug.WriteLine("args is not a KeyRoutedEventArgs, but rather a " + args.GetType().FullName);
                return;
            }
            if ((args as KeyRoutedEventArgs).Key == Windows.System.VirtualKey.Enter)
            {
                TextBoxTextChanged(textBox);
                connection.Send(
                    messageParser.PrepareMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                    true);
            }
        }

        /// <summary>
        /// Moves the filters scrollviewer to a corresponding horizontal offset.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">Contains the position under the user's thumb.</param>
        /// <seealso cref="grid_graphical_representation_MoveScrollViewer"/>
        private void grid_graphical_representation_Tap(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            grid_graphical_representation_MoveScrollViewer(
                e.GetPosition(sender as Grid));
        }

        /// <summary>
        /// Moves the filters scrollviewer to a corresponding horizontal offset.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">Contains the position under the user's thumb.</param>
        /// <seealso cref="grid_graphical_representation_MoveScrollViewer"/>
        private void grid_graphical_representation_MouseMove(object sender, DragEventArgs e)
        {
            PrintLine();
            grid_graphical_representation_MoveScrollViewer(
                e.GetPosition(sender as Grid));
        }

        /// <summary>
        /// Set the value of all filter gains to 0.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_zero_equalizer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // zero out the filters
            string[] zeroes = new string[filterManager.Filters.Count];
            for (int i = 0; i < zeroes.Length; i++)
            {
                zeroes[i] = "0";
            }
            filterManager.SetNewGainValues(zeroes);

            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.FILTERS_GAIN), true);
        }

        /// <summary>
        /// Removes the last filter, slider, and textbox.
        /// Adjusts the selected filter value accordingly.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="args">N/A</param>
        private void button_remove_filter_Tapped(object sender, TappedRoutedEventArgs args)
        {
            PrintLine();
            // add filter
            filterManager.RemoveFilter();
            if (filterManager.Filters.Count > 0)
            {
                SelectedSliderIndex = Math.Min(
                    selectedSliderIndex,
                    filterManager.Filters.Count - 1);
                ScrollToFilter(filterManager.Filters.Count - 1);
            }
            else
            {
                SelectedSliderIndex = -1;
            }

            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.FILTER_REMOVED), true);
        }

        /// <summary>
        /// Adds a new filter, slider, and textbox.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_add_filter_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // add filter
            filterManager.AddFilter(0);

            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.FILTER_ADDED), true);
        }

        /// <summary>
        /// Passes the status of the checkbox on to the server.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void checkbox_apply_equalizer_Checked(object sender, RoutedEventArgs e)
        {
            PrintLine();
            if (connection == null)
            {
                return;
            }

            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.FILTER_APPLY), true);
        }

        /// <summary>
        /// Tells the server to go to the previous track.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_prev_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.PREV_TRACK), true);
        }

        /// <summary>
        /// Tells the server to go to play the current track.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_play_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            var playpause = MessageParser.MESSAGE_TYPE.PLAY;

            // send message
            connection.Send(messageParser.PrepareMessage(
                playpause), true);
        }

        /// <summary>
        /// Tells the server to go to pause the current track.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_pause_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            var playpause = MessageParser.MESSAGE_TYPE.PAUSE;

            // send message
            connection.Send(messageParser.PrepareMessage(
                playpause), true);
        }

        /// <summary>
        /// Tells the server to go to the next track.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        private void button_next_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // send message
            connection.Send(messageParser.PrepareMessage(
                MessageParser.MESSAGE_TYPE.NEXT_TRACK), true);
        }

        /// <summary>
        /// Sets <see cref="IsTouchActive"/> to true.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        /// <seealso cref="MessageReceived"/>
        private void LayoutRoot_MouseEnter(object sender, object e)
        {
            IsTouchActive = true;
        }

        /// <summary>
        /// Sets <see cref="IsTouchActive"/> to false.
        /// </summary>
        /// <param name="sender">N/A</param>
        /// <param name="e">N/A</param>
        /// <seealso cref="MessageReceived"/>
        private void LayoutRoot_MouseLeave(object sender, object e)
        {
            IsTouchActive = false;
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":EQ:" + memberName);
        }
    }
}