﻿using GilesTrinity.DbProvider;
using GilesTrinity.Settings.Combat;
using GilesTrinity.Settings.Loot;
using GilesTrinity.Technicals;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Zeta;
using System.Xml;

namespace GilesTrinity.Settings
{
    [DataContract]
    public class TrinitySetting : ITrinitySetting<TrinitySetting>, INotifyPropertyChanged
    {
        #region Fields
        private CombatSetting _Combat;
        private WorldObjectSetting _WorldObject;
        private ItemSetting _Loot;
        private AdvancedSetting _Advanced;
        private NotificationSetting _Notification; 
        #endregion Fields

        #region Events
        /// <summary>
        /// Occurs when property changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion Events

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="TrinitySetting" /> class.
        /// </summary>
        public TrinitySetting()
        {
            Combat = new CombatSetting();
            WorldObject = new WorldObjectSetting();
            Loot = new ItemSetting();
            Advanced = new AdvancedSetting();
            Notification = new NotificationSetting();
        }
        #endregion Constructors

        #region Properties
        [DataMember(IsRequired = false)]
        public CombatSetting Combat
        {
            get
            {
                return _Combat;
            }
            set
            {
                if (_Combat != value)
                {
                    _Combat = value;
                    OnPropertyChanged("Combat");
                }
            }
        }

        [DataMember(IsRequired = false)]
        public WorldObjectSetting WorldObject
        {
            get
            {
                return _WorldObject;
            }
            set
            {
                if (_WorldObject != value)
                {
                    _WorldObject = value;
                    OnPropertyChanged("WorldObject");
                }
            }
        }

        [DataMember(IsRequired = false)]
        public ItemSetting Loot
        {
            get
            {
                return _Loot;
            }
            set
            {
                if (_Loot != value)
                {
                    _Loot = value;
                    OnPropertyChanged("Loot");
                }
            }
        }

        [DataMember(IsRequired = false)]
        public AdvancedSetting Advanced
        {
            get
            {
                return _Advanced;
            }
            set
            {
                if (_Advanced != value)
                {
                    _Advanced = value;
                    OnPropertyChanged("Advanced");
                }
            }
        }

        [DataMember(IsRequired = false)]
        public NotificationSetting Notification
        {
            get
            {
                return _Notification;
            }
            set
            {
                if (_Notification != value)
                {
                    _Notification = value;
                    OnPropertyChanged("Notification");
                }
            }
        }
        #endregion Properties

        #region Methods
        public void Reset()
        {
            TrinitySetting.Reset(this);
        }

        public void CopyTo(TrinitySetting setting)
        {
            TrinitySetting.CopyTo(this, setting);
        }

        public TrinitySetting Clone()
        {
            return (TrinitySetting)TrinitySetting.Clone(this);
        }

        public void Load()
        {
            string filename = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings", ZetaDia.Service.CurrentHero.BattleTagName, "GilesTrinity.xml");
            lock (this)
            {
                try
                {
                    if (!File.Exists(filename))
                    {
                        DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "No Config file for BattleTag, General file is considered.");
                        filename = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings", "GilesTrinity.xml");
                    }

                    DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Load Config file : {0}", filename);
                    if (File.Exists(filename))
                    {
                        using (Stream stream = File.Open(filename, FileMode.Open))
                        {

                            DataContractSerializer serializer = new DataContractSerializer(typeof(TrinitySetting));

                            TrinitySetting loadedSetting = (TrinitySetting)serializer.ReadObject(stream);
                            stream.Close();
                            loadedSetting.CopyTo(this);
                            DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Configuration file loaded");
                        }
                    }
                    else
                    {
                        DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Configuration file not found.");
                        Reset();
                    }
                }
                catch (Exception ex)
                {
                    DbHelper.Log(TrinityLogLevel.Critical, LogCategory.UserInformation, "Error while loading Config file:{0}{1}", Environment.NewLine, ex);
                }
            }
        }

        public void Save()
        {
            lock (this)
            {
                string filename = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Settings", ZetaDia.Service.CurrentHero.BattleTagName, "GilesTrinity.xml");
                try
                {
                    DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Saving Config file");
                    using (Stream stream = File.Open(filename, FileMode.Create))
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(TrinitySetting));

                        var xmlWriterSettings = new XmlWriterSettings { Indent = true };
                        using (var xmlWriter = XmlWriter.Create(stream, xmlWriterSettings))
                        {
                            serializer.WriteObject(xmlWriter, this);
                        }
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    DbHelper.Log(TrinityLogLevel.Critical, LogCategory.UserInformation, "Error while saving Config file '{0}' :{1}{2}", filename, Environment.NewLine, ex);
                }
            }
        }

        /// <summary>
        /// Called when property changed.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion Methods

        #region Static Methods
        internal static void Reset<T>(ITrinitySetting<T> setting) where T : class, ITrinitySetting<T>
        {
            try
            {
                Type type = typeof(T);
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Starting Reset Object {0}", type.Name);
                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.SetProperty | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.PropertyType.IsValueType || prop.PropertyType == typeof(string))
                    {
                        Attribute[] decorators = prop.GetCustomAttributes(typeof(DefaultValueAttribute), true) as Attribute[];
                        if (decorators != null && decorators.Length > 0)
                        {
                            DefaultValueAttribute defaultValue = decorators[0] as DefaultValueAttribute;
                            if (defaultValue != null)
                            {
                                prop.SetValue(setting, defaultValue.Value, null);
                            }
                        }
                    }
                    else
                    {
                        object value = prop.GetValue(setting, null);
                        if (value != null)
                        {
                            MethodBase method = prop.PropertyType.GetMethod("Reset");
                            if (method != null)
                            {
                                method.Invoke(value, new object[] { });
                            }
                        }
                    }
                }
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "End Reset Object {0}", type.Name);
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Error, LogCategory.UserInformation, "Error while Reset Setting {1} : {0}", ex.Message, typeof(T).Name);
            }
        }

        internal static void CopyTo<T>(ITrinitySetting<T> source, ITrinitySetting<T> destination) where T : class, ITrinitySetting<T>
        {
            try
            {
                Type type = typeof(T);
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Starting CopyTo Object {0}", type.Name);
                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.SetProperty | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.PropertyType.IsValueType || prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(destination, prop.GetValue(source, null), null);
                    }
                    else
                    {
                        object destinationValue = prop.GetValue(destination, null);
                        object sourceValue = prop.GetValue(source, null);
                        if (destinationValue != null)
                        {
                            MethodBase method = prop.PropertyType.GetMethod("CopyTo", new[] { prop.PropertyType });
                            if (method != null)
                            {
                                method.Invoke(sourceValue, new[] { destinationValue });
                            }
                        }
                        else if (destinationValue != null)
                        {
                            MethodBase method = prop.PropertyType.GetMethod("Clone", null);
                            if (method != null)
                            {
                                prop.SetValue(destination, method.Invoke(sourceValue, null), null);
                            }
                        }
                    }
                }
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "End CopyTo Object {0}", type.Name);
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Error, LogCategory.UserInformation, "Error while CopyTo Setting {1} : {0}", ex.Message, typeof(T).Name);
            }
        }

        internal static T Clone<T>(ITrinitySetting<T> setting) where T : class, ITrinitySetting<T>
        {
            try
            {
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "Starting Clone Object {0}", typeof(T).Name);
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(T));
                    serializer.WriteObject(ms, setting);
                    //ms.Seek(0, SeekOrigin.Begin);
                    return (T)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Error, LogCategory.UserInformation, "Error while Clone Setting {1} : {0}", ex.Message, typeof(T).Name);
                return null;
            }
            finally
            {
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Configuration, "End Clone Object {0}", typeof(T).Name);
            }
        }
        #endregion Static Methods
    }
}
