using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace equalizer_connect_universal
{
    /// <summary>
    /// A singleton class.
    /// Used to save and access application-saved data.
    /// </summary>
    class SavedData
    {
        #region fields

        /// <summary>
        /// The singleton instance of this class.
        /// </summary>
        private static SavedData Instance;

        #endregion

        #region public methods

        /// <summary>
        /// Create a new instance of this class.
        /// </summary>
        public SavedData()
        {
            PrintLine();
        }

        /// <summary>
        /// Gets/creates the singleton instance of this class.
        /// </summary>
        /// <returns>Singleton instance</returns>
        public static SavedData GetInstance() {
            PrintLine();
            if (SavedData.Instance == null)
            {
                SavedData.Instance = new SavedData();
            }
            return SavedData.Instance;
        }

        /// <summary>
        /// Check if the given key is accessable as a saved value.
        /// </summary>
        /// <param name="key">The ke to check.</param>
        /// <returns>True if saved.</returns>
        public bool Contains(string key)
        {
            PrintLine();
            return GetValues().ContainsKey(key);
        }
        
        /// <summary>
        /// Gets the given key value as an integer.
        /// </summary>
        /// <param name="key">Key to check for.</param>
        /// <returns>The value, 0 on failure</returns>
        public int GetIntValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToInt32(GetValues()[key]);
            }
            return 0;
        }

        /// <summary>
        /// Gets the given key value as a double.
        /// </summary>
        /// <param name="key">Key to check for.</param>
        /// <returns>The value, NaN on failure</returns>
        public double GetDecimalValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToDouble(GetValues()[key]);
            }
            return Double.NaN;
        }

        /// <summary>
        /// Gets the given key value as a string.
        /// </summary>
        /// <param name="key">Key to check for.</param>
        /// <returns>The value, empty string on failure</returns>
        public string GetStringValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToString(GetValues()[key]);
            }
            return "";
        }

        /// <summary>
        /// Saves the given value as a string with the given key.
        /// </summary>
        /// <param name="key">Key to index the value by</param>
        /// <param name="value">Value to save</param>
        public void SaveValue(string key, int value)
        {
            PrintLine();
            SaveValue(key, Convert.ToString(value));
        }

        /// <summary>
        /// Saves the given value as a string with the given key.
        /// </summary>
        /// <param name="key">Key to index the value by</param>
        /// <param name="value">Value to save</param>
        public void SaveValue(string key, double value)
        {
            PrintLine();
            SaveValue(key, Convert.ToString(value));
        }

        /// <summary>
        /// Saves the given value as a string with the given key.
        /// </summary>
        /// <param name="key">Key to index the value by</param>
        /// <param name="value">Value to save</param>
        public void SaveValue(string key, string value)
        {
            PrintLine();
            GetValues()[key] = value;
        }

        #endregion

        #region private methods

        /// <summary>
        /// Loads the application's LocalSettings.Values.
        /// </summary>
        /// <returns>The application's LocalSettings.Values</returns>
        private Windows.Foundation.Collections.IPropertySet GetValues()
        {
            return ApplicationData.Current.LocalSettings.Values;
        }

        #endregion

        public static void PrintLine(
            [System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return;
            System.Diagnostics.Debug.WriteLine(line + ":SD");
        }
    }
}
