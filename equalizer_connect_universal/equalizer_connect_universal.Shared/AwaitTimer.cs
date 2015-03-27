using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace equalizer_connect_universal
{
    /// <summary>
    /// Used to set a timeout on async tasks.
    /// </summary>
    class AwaitTimer
    {
        #region fields

        /// <summary>
        /// The number of milliseconds before the timer pops.
        /// </summary>
        public int timeoutMillisecs;
        
        /// <summary>
        /// How often to check if the task has completed.
        /// </summary>
        public int interval;
        
        /// <summary>
        /// Used in place of Thread.Sleep().
        /// </summary>
        private ManualResetEvent timer = new ManualResetEvent(false);

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance with the given default timeout and interval.
        /// </summary>
        /// <param name="millisecs">The timeout value</param>
        /// <param name="interval">The interval to check on</param>
        public AwaitTimer(int millisecs, int interval = 30)
        {
            init(millisecs, interval);
        }

        /// <summary>
        /// Run the task with the given timeout, and interval.
        /// Once the task completes/cancels/errors or the timer pops
        /// the function will return.
        /// </summary>
        /// <param name="task">The task to wait for</param>
        /// <returns>True on completion, false on cancel/error/timeout</returns>
        public bool timeTask(IAsyncInfo task)
        {
            DateTime stopAt = DateTime.Now.AddMilliseconds(timeoutMillisecs);
            while (stopAt > DateTime.Now)
            {
                // check if the task has finished
                if (task.Status == AsyncStatus.Canceled ||
                    task.Status == AsyncStatus.Completed ||
                    task.Status == AsyncStatus.Error)
                {
                    break;
                }

                // wait and try again
                timer.WaitOne(Math.Min(
                    interval,
                    (stopAt - DateTime.Now).Milliseconds));
            }

            return (task.Status == AsyncStatus.Completed);
        }

        #endregion

        #region private methods

        /// <summary>
        /// Initializes values for a new instance of this class.
        /// </summary>
        /// <param name="millisecs">The timeout</param>
        /// <param name="interval">The interval to check on</param>
        private void init(int millisecs, int interval)
        {
            this.timeoutMillisecs = millisecs;
            this.interval = interval;
        }

        #endregion
    }
}
