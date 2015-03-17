using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace equalizer_connect_universal
{
    class SavedData
    {
        #region constants

        enum DTYPE { INT, DEC, STR };
        public const string KEY_VALUE_STORE = "KeyValueStore.txt";

        #endregion

        #region fields

        private static SavedData Instance;

        #endregion

        #region public methods

        public SavedData()
        {
            PrintLine();
        }

        public static SavedData GetInstance() {
            PrintLine();
            if (SavedData.Instance == null)
            {
                SavedData.Instance = new SavedData();
            }
            return SavedData.Instance;
        }

        public bool Contains(string key)
        {
            PrintLine();
            return GetValues().ContainsKey(key);
        }
        
        public int GetIntValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToInt32(GetValues()[key]);
            }
            return 0;
        }

        public decimal GetDecimalValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToDecimal(GetValues()[key]);
            }
            return 0;
        }

        public string GetStringValue(string key)
        {
            PrintLine();
            if (Contains(key))
            {
                return Convert.ToString(GetValues()[key]);
            }
            return "";
        }

        public void SaveIntValue(string key, int value)
        {
            PrintLine();
            SaveStringValue(key, Convert.ToString(value));
        }

        public void SaveDecimalValue(string key, decimal value)
        {
            PrintLine();
            SaveStringValue(key, Convert.ToString(value));
        }

        public void SaveStringValue(string key, string value)
        {
            PrintLine();
            GetValues()[key] = value;
        }

        #endregion

        #region private methods

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
