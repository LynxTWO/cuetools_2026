using System;
using System.IO;
using System.Text;

namespace CUETools.Processor.Settings
{
    public class SettingsWriter : IDisposable
    {
        private StreamWriter _sw;
        private readonly string _path;
        private readonly string _temporaryPath;
        private bool _committed;

        public SettingsWriter(string appName, string fileName, string appPath)
        {
            _path = Path.Combine(SettingsShared.GetProfileDir(appName, appPath), fileName);
            _temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            _sw = new StreamWriter(
                new FileStream(_temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None),
                Encoding.UTF8);
        }

        public void Save(string name, string value)
        {
            _sw.WriteLine(name + "=" + value);
        }

        public void SaveText(string name, string value)
        {
            _sw.Write(name);
            if (value == "")
            {
                _sw.WriteLine("=");
                return;
            }
            using (StringReader sr = new StringReader(value))
            {
                string lineStr;
                while ((lineStr = sr.ReadLine()) != null)
                    _sw.WriteLine("=" + lineStr);
            }
        }

        public void Save(string name, bool value)
        {
            Save(name, value ? "1" : "0");
        }

        public void Save(string name, int value)
        {
            Save(name, value.ToString());
        }

        public void Save(string name, uint value)
        {
            Save(name, value.ToString());
        }

        public void Save(string name, long value)
        {
            Save(name, value.ToString());
        }

        public void Save(string name, DateTime value)
        {
            Save(name, value.ToBinary());
        }

        public void Close()
        {
            if (_committed)
                return;

            _sw.Flush();
            _sw.Dispose();
            _sw = null;

            try
            {
                if (File.Exists(_path))
                    File.Replace(_temporaryPath, _path, null);
                else
                    File.Move(_temporaryPath, _path);
                _committed = true;
            }
            finally
            {
                if (!_committed)
                    DeleteTemporaryFile();
            }
        }

        public void Dispose()
        {
            if (_sw != null)
            {
                _sw.Dispose();
                _sw = null;
            }

            if (!_committed)
                DeleteTemporaryFile();
        }

        private void DeleteTemporaryFile()
        {
            try
            {
                if (File.Exists(_temporaryPath))
                    File.Delete(_temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
