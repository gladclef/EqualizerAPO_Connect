using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.Foundation;

namespace equalizer_connect_universal
{
    class AwaitTimer
    {
        public int timeoutMillisecs;
        public int interval;
        private ManualResetEvent timer = new ManualResetEvent(false);

        public AwaitTimer(int millisecs, int interval = 30)
        {
            init(millisecs, interval);
        }

        private void init(int millisecs, int interval)
        {
            this.timeoutMillisecs = millisecs;
            this.interval = interval;
        }

        /// <summary>
        /// Run the task with the given timeout, and interval.
        /// Once the task completes/cancels/errors or the timer pops
        /// the function will return.
        /// </summary>
        /// <param name="task">The task to wait for</param>
        /// <returns>True on completion, false on cancel/error/timeout</returns>
        public bool timeTask(IAsyncAction task)
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
    }
}
