using System;
using System.Collections.Generic;
using System.Linq;
using System.IO.IsolatedStorage;
using System.Text;
using System.Threading.Tasks;

namespace equalizerapo_connect_silverlight
{
    class SavedData
    {
        #region constants

        enum DTYPE { INT, DEC, STR };
        public const string KEY_VALUE_STORE = "KeyValueStore.txt";

        #endregion

        #region fields

        public Dictionary<string,string> KeyValuePairs;
        private static SavedData Instance;

        #endregion

        #region public methods

        public SavedData()
        {
            PrintLine();
            KeyValuePairs = new Dictionary<string, string>();
            LoadKeyValuePairs();
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
            return KeyValuePairs.ContainsKey(key);
        }
        
        public int GetIntValue(string key)
        {
            PrintLine();
            string value;
            if (KeyValuePairs.TryGetValue(key, out value))
            {
                return Convert.ToInt32(value);
            }
            return 0;
        }

        public decimal GetDecimalValue(string key)
        {
            PrintLine();
            string value;
            if (KeyValuePairs.TryGetValue(key, out value))
            {
                return Convert.ToInt32(value);
            }
            return 0;
        }

        public string GetStringValue(string key)
        {
            PrintLine();
            string value = "";
            KeyValuePairs.TryGetValue(key, out value);
            return value;
        }

        public void SaveIntValue(string key, int value)
        {
            PrintLine();
            SaveStringValue(key, Convert.ToString(value));
            SaveKeyValuePairs();
        }

        public void SaveDecimalValue(string key, decimal value)
        {
            PrintLine();
            SaveStringValue(key, Convert.ToString(value));
            SaveKeyValuePairs();
        }

        public void SaveStringValue(string key, string value)
        {
            PrintLine();
            KeyValuePairs[key] = value;
            SaveKeyValuePairs();
        }

        #endregion

        #region private methods

        private IsolatedStorageFileStream GetFilestream()
        {
            PrintLine();
            IsolatedStorageFile file =
                IsolatedStorageFile.GetUserStoreForApplication();
            return file.OpenFile(KEY_VALUE_STORE, System.IO.FileMode.OpenOrCreate);
        }

        private void LoadKeyValuePairs()
        {
            PrintLine();
            // get the file
            IsolatedStorageFileStream infile = GetFilestream();
            var buffer = new byte[infile.Length];
            infile.Read(buffer, 0, buffer.Length);
            infile.Dispose();

            // parse into lines
            string[] lines = 
                System.Text.Encoding.UTF8.GetString(buffer, 0, buffer.Length).Split(
                    new char[] { '\n' });

            // parse into key/value pairs
            KeyValuePairs.Clear();
            foreach (string line in lines)
            {
                if (line.Contains(':'))
                {
                    string[] parts = line.Split(new char[] { ':' });
                    KeyValuePairs.Add(parts[0], parts[1]);
                }
            }

            // close the stream
            infile.Close();
        }

        private void SaveKeyValuePairs()
        {
            PrintLine();
            // get the file
            IsolatedStorageFileStream outfile = GetFilestream();

            // create the buffer and fill it
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string,string> pair in KeyValuePairs)
            {
                builder.Append(pair.Key);
                builder.Append(":");
                builder.Append(pair.Value);
                builder.Append("\n");
            }
            char[] chars = builder.ToString().ToCharArray();
            builder.Clear();
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(chars);

            // save the buffer to the file
            outfile.Write(buffer, 0, buffer.Length);

            // close the stream
            outfile.Close();
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
