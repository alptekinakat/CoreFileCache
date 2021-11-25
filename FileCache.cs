using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreFileCache
{

    public sealed class FileCache : IDistributedCache, IDisposable, IAsyncDisposable
    {
        private readonly FileCacheOptions _Options;
        private readonly string _PathCache;
        private readonly string _PathMeta;
        private readonly Timer _CleanupTimer;
        private readonly Thread _RefreshTimer;
        private readonly CancellationTokenSource _RefreshToken;
        private readonly ConcurrentDictionary<string, object> _LockFiles;
        private readonly ConcurrentQueue<string> _RefreshList;
        private readonly object _ErrLogLock = new object();

        public FileCache(IOptions<FileCacheOptions> options) : this(options.Value)
        {
        }
        public FileCache(FileCacheOptions options)
        {
            _Options = options;
            _PathCache = System.IO.Path.Combine(_Options.CachePath, "cache");
            _PathMeta = System.IO.Path.Combine(_Options.CachePath, "meta");
            if (!System.IO.Directory.Exists(_Options.CachePath))
            {
                System.IO.Directory.CreateDirectory(_Options.CachePath);
            }
            if (!System.IO.Directory.Exists(_PathCache))
            {
                System.IO.Directory.CreateDirectory(_PathCache);
            }
            if (!System.IO.Directory.Exists(_PathMeta))
            {
                System.IO.Directory.CreateDirectory(_PathMeta);
            }
            _LockFiles = new ConcurrentDictionary<string, object>();
            _RefreshList = new ConcurrentQueue<string>();
            _RefreshToken = new CancellationTokenSource();
            _CleanupTimer = new Timer(_ =>
            {
                RemoveExipred();
            }, null, TimeSpan.Zero, _Options.CleanupInterval);

            _RefreshTimer = new Thread(() => { RefreshSessions(_RefreshToken.Token); });
            _RefreshTimer.Start();
        }



        public void Dispose()
        {
            _CleanupTimer.Dispose();
            _LockFiles.Clear();
            _RefreshToken.Cancel();
            _RefreshList.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            _LockFiles.Clear();
            _RefreshList.Clear();
            _RefreshToken.Cancel();
            await _CleanupTimer.DisposeAsync();
        }

        public byte[] Get(string key)
        {
            try
            {
                if (!_LockFiles.ContainsKey(key)) // Session Not Found
                {
                    return new byte[] { };
                }
                else //Session Found
                {
                    _LockFiles.TryGetValue(key, out object file_lock);
                    lock (file_lock)
                    {
                        var model = ReadFromFile(key);
                        if (DateTime.Now > model.ExpireTime)  //Expire Time
                        {
                            DeleteFiles(key);
                            _LockFiles.Remove(key, out object dump);
                            return new byte[] { };
                        }
                        else
                        {
                            return ReadDataFromFile(key);
                        }
                    }
                }
            }
            catch (Exception Ex)
            {
                LogErr("Get", Ex);
                return new byte[] { };
            }
        }

        public Task<byte[]> GetAsync(string key, CancellationToken token = default)
        {
            try
            {
                var t = new Task<byte[]>(() => { return Get(key); });
                t.RunSynchronously();
                return t;

            }
            catch (Exception Ex)
            {
                LogErr("GetAsync", Ex);
                var t = new Task<byte[]>(() => { return new byte[] { }; });
                t.RunSynchronously();
                return t;
            }
        }

        public void Refresh(string key)
        {
            try
            {
                if (!_RefreshList.Contains(key))
                {
                    _RefreshList.Enqueue(key);
                }
            }
            catch (Exception Ex)
            {
                LogErr("Refresh", Ex);
            }
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            try
            {
                var t = new Task(() => { Refresh(key); }, token);
                t.RunSynchronously();
                return t;
            }
            catch (Exception Ex)
            {
                LogErr("RefreshAsync", Ex);
                var t = new Task(() => { return; }, token);
                t.RunSynchronously();
                return t;
            }
        }

        public void Remove(string key)
        {
            try
            {
                if (!_LockFiles.ContainsKey(key)) //Yoksa Ekle
                {
                    _LockFiles.TryAdd(key, new object());
                }
                _LockFiles.TryRemove(key, out object file_lock); //Session Remove
                lock (file_lock)
                {
                    DeleteFiles(key);
                }
                file_lock = null;
            }
            catch (Exception Ex)
            {
                LogErr("Remove", Ex);
            }
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            try
            {
                var t = new Task(() => { Remove(key); }, token);
                t.RunSynchronously();
                return t;
            }
            catch (Exception Ex)
            {
                LogErr("RemoveAsync", Ex);
                var t = new Task(() => { return; }, token);
                t.RunSynchronously();
                return t;
            }
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            try
            {
                if (!_LockFiles.ContainsKey(key))
                {
                    _LockFiles.TryAdd(key, new object());
                }
                FileCacheModel model = new FileCacheModel()
                {
                    ExpireTime = DateTime.Now,
                    RefreshTime = DateTime.Now,
                    SlidingExprireSecond = 0
                };

                if (options.AbsoluteExpiration.HasValue)
                {
                    model.ExpireTime = (DateTime)options.AbsoluteExpiration?.DateTime;
                    model.AbsoluteExpireTime = (DateTime)options.AbsoluteExpiration?.DateTime;
                }
                else if (options.AbsoluteExpirationRelativeToNow.HasValue)
                {
                    model.ExpireTime = DateTimeOffset.Now.Add(options.AbsoluteExpirationRelativeToNow.Value).DateTime;
                }
                else if (options.SlidingExpiration.HasValue)
                {
                    model.ExpireTime = DateTimeOffset.Now.Add(options.SlidingExpiration.Value).DateTime;
                    model.SlidingExprireSecond = (long)options.SlidingExpiration.Value.TotalSeconds;
                }

                _LockFiles.TryGetValue(key, out object file_lock);
                lock (file_lock)
                {
                    WriteToFile(key, model);
                    WriteDataToFile(key, value);
                }
            }
            catch (Exception Ex)
            {
                LogErr("Set", Ex);
            }
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            try
            {
                var t = new Task(() =>
                {
                    Set(key, value, options);
                }, token);
                t.RunSynchronously();
                return t;
            }
            catch (Exception Ex)
            {
                LogErr("SetAsync", Ex);
                var t = new Task(() => { return; }, token);
                t.RunSynchronously();
                return t;
            }
        }

        private void RemoveExipred()
        {
            try
            {
                var files = System.IO.Directory.GetFiles(_PathCache, "*.cache");
                foreach (var file in files)
                {
                    string key = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!_LockFiles.ContainsKey(key)) //Sessionlara Bak Yoksa Ekle
                    {
                        _LockFiles.TryAdd(key, new object());
                    }
                    _LockFiles.TryGetValue(key, out object file_lock);
                    lock (file_lock)
                    {
                        var model = ReadFromFile(key);
                        if (DateTime.Now > model.ExpireTime) //Expire Olanları Sil
                        {
                            DeleteFiles(key);
                            _LockFiles.TryRemove(key, out object dump);
                        }
                    }
                }
            }
            catch (Exception Ex)
            {
                LogErr("RemoveExipred", Ex);
            }
        }


        private void RefreshSessions(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_RefreshList.TryDequeue(out string key))
                    {
                        if (_LockFiles.TryGetValue(key, out object file_lock))
                        {
                            lock (file_lock)
                            {
                                var model = ReadFromFile(key);
                                model.RefreshTime = DateTime.Now;
                                model.ExpireTime = DateTime.Now.AddSeconds(model.SlidingExprireSecond);
                                WriteToFile(key, model);
                            }
                        }
                        Thread.Sleep(10); 
                    } else
                    {
                        Thread.Sleep(1000);
                    }
                    
                }
                catch (Exception Ex)
                {
                    LogErr("RefreshSessions", Ex);
                }
            }
        }
        private void WriteToFile(string key, FileCacheModel model)
        {
            string file = System.IO.Path.Combine(_PathCache, key + ".cache");
            System.IO.File.WriteAllText(file, JsonConvert.SerializeObject(model));
        }
        private FileCacheModel ReadFromFile(string key)
        {
            string file = System.IO.Path.Combine(_PathCache, key + ".cache");
            return JsonConvert.DeserializeObject<FileCacheModel>(System.IO.File.ReadAllText(file));
        }

        private void WriteDataToFile(string key, byte[] data)
        {
            string file = System.IO.Path.Combine(_PathMeta, key + ".meta");
            System.IO.File.WriteAllBytes(file, data);
        }
        private byte[] ReadDataFromFile(string key)
        {
            string file = System.IO.Path.Combine(_PathMeta, key + ".meta");
            return System.IO.File.ReadAllBytes(file);
        }

        private void DeleteFiles(string key)
        {
            string fcache = System.IO.Path.Combine(_PathCache, key + ".cache");
            string fmeta = System.IO.Path.Combine(_PathMeta, key + ".meta");
            if (System.IO.File.Exists(fcache))
            {
                System.IO.File.Delete(fcache);
            }
            if (System.IO.File.Exists(fmeta))
            {
                System.IO.File.Delete(fmeta);
            }
        }

        private void LogErr(string Method, Exception Ex)
        {
            try
            {
                string file = System.IO.Path.Combine(_Options.CachePath, DateTime.Now.ToString("yyyyMMdd") + "_ERR.log");
                lock (_ErrLogLock)
                {
                    string err = DateTime.Now.ToString("HH:mm:ss") + "\t" + Method + "\t" + Ex.ToString();
                    System.IO.File.AppendAllText(file, err);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
