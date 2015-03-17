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

namespace equalizerapo_connect_universal
{
    public partial class Equalizer
    {
        #region constants

        public const double GAIN_ACCURACY = 0.1;

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

        // object references

        /// <summary>
        /// Reference to the track currently being played.
        /// </summary>
        public Track currentTrack { get; private set; }
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
                UpdatePlayback();
            }
        }
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

        public Equalizer()
        {
            PrintLine();
            Init();
        }

        public void Init()
        {
            PrintLine();
            InitializeComponent();

            // set min/max on volume and attach event handlers
            slider_volume.Minimum = -FilterManager.MAX_PREAMP_GAIN;
            slider_volume.Maximum = FilterManager.MAX_PREAMP_GAIN;
            slider_volume.ValueChanged += new RangeBaseValueChangedEventHandler(slider_volume_ValueChanged);
            textbox_volume.TextChanged += new TextChangedEventHandler(textbox_volume_TextChanged);
            textbox_volume.KeyUp += new KeyEventHandler(textbox_volume_KeyUp);

            PrintLine();
            // create new instances of things and set initial values
            filterSliders = new LinkedList<Slider>();
            filterTextboxes = new LinkedList<TextBox>();
            connection = Connection.GetInstance();
            filterManager = FilterManager.GetInstance();
            messageParser = new MessageParser(filterManager, this);
            scrollValues = new Dictionary<string, double>();
            rectGraphicRep = new LinkedList<Rectangle>();
            SelectedSliderIndex = -1;

            PrintLine();
            // check for updates from the viewscroller
            checkScrollTimer = new DispatcherTimer();
            checkScrollTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            checkScrollTimer.Tick += new EventHandler<object>(CheckScrolled);

            PrintLine();
            // smooth scrolling when the selected filter is changed
            updateScrollTimer = new DispatcherTimer();
            updateScrollTimer.Interval = new TimeSpan(0, 0, 0, 0, 30);
            updateScrollTimer.Tick += new EventHandler<object>(UpdateScrollPosition);

            PrintLine();
            // try and reduce the number of times the textbox has to change
            filterChangedTimer = new DispatcherTimer();
            filterChangedTimer.Interval = new TimeSpan(0, 0, 0, 0, 30);
            filterChangedTimer.Tick += new EventHandler<object>(UpdateFilterTextBoxes);

            PrintLine();
            // get updates from the filters
            filterManager.filterAddedEvent += new EventHandler(UpdateFilter);
            filterManager.filterRemovedEvent += new EventHandler(UpdateFilter);
            filterManager.filterChangedEvent += new EventHandler(UpdateFilter);
            filterManager.volumeChangedEvent += new EventHandler(UpdateVolume);
            filterManager.equalizerAppliedEvent += new EventHandler(UpdateIsEqualizerApplied);

            PrintLine();
            // set up track reference
            currentTrack = new Track();
            currentTrack.ChangedEvent += new EventHandler(UpdateTrackname);

            PrintLine();
            // set up connection
            connection.MessageRecievedEvent += new EventHandler(MessageReceived);
            connection.DisconnectedEvent += new EventHandler(Disconnected);
            connection.DisconnectMe += new EventHandler(ConnectionSideDisconnect);
            PrintLine();

            initCalled = true;
        }

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

        public void Close()
        {
            PrintLine();
            connection.Close();
            filterManager.Close();
            filterChangedTimer.Stop();
            updateScrollTimer.Stop();
            checkScrollTimer.Stop();
            filterSliders.Clear();
            filterTextboxes.Clear();
            scrollValues.Clear();
            selectedSliderIndex = -1;
            rectGraphicRep.Clear();
            cachedScrollOffset = 0;
            initCalled = false;
        }

        public void TrackChanged()
        {
            PrintLine();
            foreach (KeyValuePair<double, Filter> pair in filterManager.Filters)
            {
                pair.Value.IsLocked = true;
                int oldSelected = SelectedSliderIndex;
                SelectedSliderIndex = -1;
                SelectedSliderIndex = oldSelected;
            }
        }

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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            PrintLine();
            base.OnNavigatedTo(e);
            if (!initCalled)
            {
                Init();
            }
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            PrintLine();
            base.OnNavigatingFrom(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            PrintLine();
            base.OnNavigatedFrom(e);
            Close();
        }

        #endregion

        #region private methods

        private void MessageReceived(object sender, EventArgs args)
        {
            PrintLine();
            if (IsTouchActive)
            {
                return;
            }
            Connection.MessageReceivedEventArgs mrev =
                (Connection.MessageReceivedEventArgs)args;
            messageParser.ParseMessage(mrev.message);
        }

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

        private void ConnectionSideDisconnect(object sender, EventArgs args)
        {
            PrintLine();
            connection.SideDisconnect();
        }

        private void UpdateTrackname(object sender, EventArgs args)
        {
            PrintLine();
            Track.TrackChangedEventArgs targs =
                args as Track.TrackChangedEventArgs;
            // TODO: remove
            // System.Diagnostics.Debug.WriteLine("UpdateTrackname [" + targs.property + ":" + targs.newValue + "], " + currentTrack.Artist + ", " + currentTrack.Title);
            textblock_now_playing.Text =
                "Now Playing: " +
                currentTrack.Artist + " - " +
                currentTrack.Title;
        }

        private void UpdateFilters(object sender, EventArgs args)
        {
            PrintLine();
            int[] allIndices = new int[filterSliders.Count];
            for (int i = 0; i < allIndices.Length; i++)
            {
                allIndices[i] = i;
            }
            UpdateFilter(sender, new FilterManager.FilterEventArgs(
                allIndices));
        }

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
                slider.Minimum = -FilterManager.GAIN_MAX;
                slider.Maximum = FilterManager.GAIN_MAX;
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

        private void UpdateFilter(object sender, EventArgs args)
        {
            PrintLine();
            SortedDictionary<double, Filter> filters = filterManager.Filters;

            // update the number of sliders/textboxes
            RemoveSliders();
            AddSliders();
            UpdateGraphicalRepresentation();

            // get the arguments
            FilterManager.FilterEventArgs filterArgs =
                args as FilterManager.FilterEventArgs;

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
                if (Math.Abs(slider.Value - gain) >= GAIN_ACCURACY)
                {
                    slider.Value = gain;
                }

                // set timer to update the textbox
                filterChangedTimer.Stop();
                filterChangedTimer.Start();
            }
        }

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

        private void UpdatePlayback()
        {
            PrintLine();
            if (isPlaying)
            {
                if (button_play.Visibility == Visibility.Visible)
                {
                    button_play.Visibility = Visibility.Collapsed;
                    button_pause.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (button_play.Visibility == Visibility.Collapsed)
                {
                    button_play.Visibility = Visibility.Visible;
                    button_pause.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateIsEqualizerApplied(object sender, EventArgs args)
        {
            PrintLine();
            checkbox_apply_equalizer.IsChecked = 
                filterManager.IsEqualizerApplied;
        }

        private static SolidColorBrush GetBrush(string brushName)
        {
            PrintLine();
            return new SolidColorBrush((Application.Current.Resources[brushName] as SolidColorBrush).Color);
        }

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
                scrollviewer_equalizer.ScrollToHorizontalOffset(
                    scrollValues["scrollNew"]);
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
            scrollviewer_equalizer.ScrollToHorizontalOffset(
                scrollNow);

            // increase iterations
            scrollValues["iteration"] += 1;
        }

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
            if (Math.Abs(gain - filter.Gain) < GAIN_ACCURACY)
            {
                return;
            }

            // is the filter locked? ie, can't change
            if (filter.IsLocked)
            {
                // change value back
                UpdateFilter(this,
                    new FilterManager.FilterEventArgs(new int[] { index }));
            }
            else
            {
                // change filter gain
                filter.Gain = gain;
            }
        }

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
                double gainPercentage = (filter.Gain + FilterManager.GAIN_MAX) /
                    (FilterManager.GAIN_MAX * 2);
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
            double xPercent = x / gwidth;
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
                scrollviewer_equalizer.ScrollToHorizontalOffset(scroll);
            }
        }

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

        private void button_filter_prev_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = Math.Max(
                SelectedSliderIndex - 1,
                0);
        }

        private void button_filter_decrease_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            Filter filter = filterManager.Filters.ElementAt(SelectedSliderIndex).Value;
            filter.Gain -= 0.1;
            connection.Send(
                messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

        private void button_filter_increase_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            Filter filter = filterManager.Filters.ElementAt(SelectedSliderIndex).Value;
            filter.Gain += 0.1;
            connection.Send(
                messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

        private void button_filter_next_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = Math.Min(
                SelectedSliderIndex + 1,
                filterSliders.Count - 1);
        }

        private void button_deselect_filter_Click(object sender, RoutedEventArgs e)
        {
            PrintLine();
            SelectedSliderIndex = -1;
        }

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

        private void slider_volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            PrintLine();
            // check that the value is new
            if (Math.Abs(slider_volume.Value - filterManager.PreAmpGain) < GAIN_ACCURACY)
            {
                return;
            }

            // change volume gain
            filterManager.PreAmpGain = slider_volume.Value;

            // update the textbox
            UpdateVolumeTextbox();

            // send a message to the server
            connection.Send(
                messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                false);
        }

        private void textbox_volume_TextChanged(object sender, TextChangedEventArgs args)
        {
            PrintLine();
            if (textbox_volume.FocusState != FocusState.Unfocused)
            {
                return;
            }

            slider_volume.Value = GetDoubleFromText(textbox_volume.Text);
            connection.Send(
                messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                true);
        }

        private void textbox_volume_KeyUp(object sender, KeyRoutedEventArgs args)
        {
            PrintLine();
            // check pre-conditions
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                slider_volume.Value = GetDoubleFromText(textbox_volume.Text);
                connection.Send(
                    messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.VOLUME_CHANGED),
                    true);
            }
        }

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
            if (Math.Abs(slider.Value - filter.Gain) < GAIN_ACCURACY)
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
                    messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                    false);
            }
        }

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
                messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                true);
        }

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
                    messageParser.CreateMessage(MessageParser.MESSAGE_TYPE.FILTERS_GAIN),
                    true);
            }
        }

        private void grid_graphical_representation_Tap(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            grid_graphical_representation_MoveScrollViewer(
                e.GetPosition(sender as Grid));
        }

        private void grid_graphical_representation_MouseMove(object sender, DragEventArgs e)
        {
            PrintLine();
            grid_graphical_representation_MoveScrollViewer(
                e.GetPosition(sender as Grid));
        }

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
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.FILTERS_GAIN), true);
        }

        private void button_remove_filter_Tapped(object sender, TappedRoutedEventArgs args)
        {
            PrintLine();
            // add filter
            filterManager.RemoveFilter();
            ScrollToFilter(filterManager.Filters.Count - 1);

            // send message
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.FILTER_REMOVED), true);
        }

        private void button_add_filter_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // add filter
            filterManager.AddFilter(0);

            // send message
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.FILTER_ADDED), true);
        }

        private void checkbox_apply_equalizer_Checked(object sender, RoutedEventArgs e)
        {
            PrintLine();
            // send message
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.FILTER_APPLY), true);
        }

        private void button_prev_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // send message
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.PREV_TRACK), true);
        }

        private void button_play_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            var playpause = MessageParser.MESSAGE_TYPE.PLAY;

            // send message
            connection.Send(messageParser.CreateMessage(
                playpause), true);
        }

        private void button_pause_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            var playpause = MessageParser.MESSAGE_TYPE.PAUSE;

            // send message
            connection.Send(messageParser.CreateMessage(
                playpause), true);
        }

        private void button_next_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PrintLine();
            // send message
            connection.Send(messageParser.CreateMessage(
                MessageParser.MESSAGE_TYPE.NEXT_TRACK), true);
        }

        private void LayoutRoot_MouseEnter(object sender, object e)
        {
            IsTouchActive = true;
        }

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