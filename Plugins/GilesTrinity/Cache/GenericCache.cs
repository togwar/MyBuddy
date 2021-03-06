﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using GilesTrinity.Technicals;

namespace GilesTrinity
{
    internal class GenericCache
    {
        //private static HashSet<GenericCacheObject> CacheList = new HashSet<GenericCacheObject>();

        private static Dictionary<string, GenericCacheObject> _dataCache = new Dictionary<string, GenericCacheObject>();
        private static Dictionary<DateTime, string> _expireCache = new Dictionary<DateTime, string>();

        private static readonly object _Synchronizer = new object();

        private static Thread Manager;

        public static bool AddToCache(GenericCacheObject obj)
        {
            if (obj.Key == "")
                return false;

            lock (_Synchronizer)
            {
                if (!ContainsKey(obj.Key))
                {
                    _dataCache.Add(obj.Key, obj);
                    _expireCache.Add(obj.Expires, obj.Key);
                    return true;
                }
                return false;
            }
        }

        public static bool UpdateObject(GenericCacheObject obj)
        {
            if (obj.Key == "")
                return false;
            try
            {
                lock (_Synchronizer)
                {
                    if (RemoveObject(obj.Key))
                    {
                        _dataCache.Add(obj.Key, obj);
                        _expireCache.Add(obj.Expires, obj.Key);

                        return true;
                    }
                    else
                    {
                        DbHelper.LogDebug("Unable to update Generic Cache Object {0}", obj);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                DbHelper.LogDebug("Unable to update Generic Cache Object {0}", obj);
                DbHelper.LogDebug(ex.ToString(), false);
                return false;
            }
        }

        public static bool RemoveObject(string key)
        {
            if (key == "")
                return false;

            lock (_Synchronizer)
            {
                if (ContainsKey(key))
                {
                    GenericCacheObject oldObj = _dataCache[key];
                    _dataCache.Remove(key);
                    _expireCache.Remove(oldObj.Expires);
                    return true;
                }
                return false;
            }
        }

        public static bool ContainsKey(string key)
        {
            if (key == "")
                return false;

            lock (_Synchronizer)
            {
                return _dataCache.ContainsKey(key); 
            }
        }

        public static bool Contains(GenericCacheObject obj)
        {
            if (obj.Key == "")
                return false;

            lock (_Synchronizer)
            {
                return ContainsKey(obj.Key);
            }
        }

        public static GenericCacheObject GetObject(string key)
        {
            lock (_Synchronizer)
            {
                if (ContainsKey(key))
                {
                    return _dataCache[key];
                }
                else
                    return new GenericCacheObject();
            }
        }

        public static void MaintainCache()
        {
            if (Manager == null || (Manager != null && !Manager.IsAlive))
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Starting up Generic Cache Manage thread");
                Manager = new Thread(Manage)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };
                Manager.Start();
            }
        }

        private static void Manage()
        {
            try
            {
                while (true)
                {
                    long NowTicks = DateTime.Now.Ticks;

                    lock (_Synchronizer)
                    {
                        foreach (KeyValuePair<DateTime, string> kv in _expireCache.Where(o => o.Key.Ticks < NowTicks).ToList())
                        {
                            if (kv.Key.Ticks < NowTicks)
                            {
                                _expireCache.Remove(kv.Key);
                                _dataCache.Remove(kv.Value);
                            }
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Exception in Generic Cache Manager");
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, ex.ToString());
            }
        }

        public static void Shutdown()
        {
            if (Manager != null)
            {
                Manager.Abort();
            }
        }

        public static void ClearCache()
        {
            lock (_Synchronizer)
            {
                _dataCache.Clear();
                _expireCache.Clear();
            }
        }
    }

    internal class GenericCacheObject 
    {
        public string Key { get; set; }
        public object Value { get; set; }
        public DateTime Expires { get; set; }

        public GenericCacheObject() { }

        public GenericCacheObject(string key, object value, TimeSpan expirationDuration)
        {
            Key = key;
            Value = value;
            Expires = DateTime.Now.Add(expirationDuration);
        }

        public override bool Equals(object obj)
        {
            var other = obj as GenericCacheObject;
            if (other == null)
                return false;
            if (other.Key.Trim() == String.Empty)
                return false;

            return this.Key == other.Key;
        }

        public override int GetHashCode()
        {
            return this.Key.GetHashCode();
        }

        public override string ToString()
        {
            return String.Format("Key={0} Value={1} Expires={2}", Key, Value, Expires);
        }
    }
}
