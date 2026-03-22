using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace adrilight
{
    class UserSettingsManager
    {
        private readonly string _settingsFolder;

        public UserSettingsManager(string settingsFolder = null)
        {
            _settingsFolder = settingsFolder
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "adrilight");
        }

        private string JsonPath => _settingsFolder;

        private string JsonFileNameAndPath => Path.Combine(JsonPath, "adrilight-settings.json");

        private void Save(IUserSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            Directory.CreateDirectory(JsonPath);
            File.WriteAllText(JsonFileNameAndPath, json);
        }

        public IUserSettings LoadIfExists()
        {
            if (!File.Exists(JsonFileNameAndPath)) return null;

            var json = File.ReadAllText(JsonFileNameAndPath);

            var settings = JsonConvert.DeserializeObject<UserSettings>(json);
            ApplyMigrations(settings);
            settings.PropertyChanged += (_, __) => Save(settings);

            HandleAutostart(settings);
            return settings;
        }

        /// <summary>
        /// Applies any pending config schema migrations based on ConfigFileVersion.
        /// Each migration step increments ConfigFileVersion so it only runs once.
        /// Add new migrations as additional if-blocks below — never change existing ones.
        /// </summary>
        public void ApplyMigrations(IUserSettings settings)
        {
            if (settings.ConfigFileVersion == 1)
            {
                // v1 → v2: SpotsY was over-counted by 2 in the old grid layout
                settings.SpotsX = Math.Max(1, settings.SpotsX);
                settings.SpotsY = Math.Max(1, settings.SpotsY - 2);
                settings.ConfigFileVersion = 2;
            }

            if (settings.ConfigFileVersion == 2)
            {
                // v2 → v3: clamp any out-of-range whitebalance values that may have been written
                // by a MaterialDesignDiscreteSlider discrete-snap bug (values above Maximum=100).
                // Setter-level clamping (added in 3.4.1) will prevent recurrence, but this step
                // repairs any already-corrupt JSON on first load after upgrading.
                settings.WhitebalanceRed   = settings.WhitebalanceRed;
                settings.WhitebalanceGreen = settings.WhitebalanceGreen;
                settings.WhitebalanceBlue  = settings.WhitebalanceBlue;
                settings.AltWhitebalanceRed   = settings.AltWhitebalanceRed;
                settings.AltWhitebalanceGreen = settings.AltWhitebalanceGreen;
                settings.AltWhitebalanceBlue  = settings.AltWhitebalanceBlue;
                settings.ConfigFileVersion = 3;
            }

            // Add future migrations here:
            // if (settings.ConfigFileVersion == 3) { ... settings.ConfigFileVersion = 4; }
        }

        public IUserSettings MigrateOrDefault()
        {
            var settings = new UserSettings();
            settings.PropertyChanged += (_, __) => Save(settings);

            var legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "adrilight");
            if (!Directory.Exists(legacyPath)) return settings;

            var legacyFiles = Directory.GetFiles(legacyPath, "user.config", SearchOption.AllDirectories);

            var file = legacyFiles
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTimeUtc)
                        .FirstOrDefault();

            if(file != null)
            {
                var xdoc = XDocument.Load(file.FullName);

                //migrate old values
                ReadAndApply(xdoc, settings, "SPOTS_X", s => s.SpotsX);
                ReadAndApply(xdoc, settings, "SPOTS_Y", s => s.SpotsY);
                ReadAndApply(xdoc, settings, "SPOT_WIDTH", s => s.SpotWidth);
                ReadAndApply(xdoc, settings, "SPOT_HEIGHT", s => s.SpotHeight);
                ReadAndApply(xdoc, settings, "BORDER_DISTANCE_X", s => s.BorderDistanceX);
                ReadAndApply(xdoc, settings, "BORDER_DISTANCE_Y", s => s.BorderDistanceY);
                ReadAndApply(xdoc, settings, "USE_LINEAR_LIGHTING", s => s.UseLinearLighting);
                ReadAndApply(xdoc, settings, "COM_PORT", s => s.ComPort);
                ReadAndApply(xdoc, settings, "SATURATION_TRESHOLD", s => s.SaturationTreshold);
                ReadAndApply(xdoc, settings, "MIRROR_X", s => s.MirrorX);
                ReadAndApply(xdoc, settings, "MIRROR_Y", s => s.MirrorY);
                ReadAndApply(xdoc, settings, "OFFSET_LED", s => s.OffsetLed);

                ReadAndApply(xdoc, settings, "AUTOSTART", s => s.Autostart);
                //migrate actual autostart registry stuff as well
                HandleAutostart(settings);
            }

            return settings;
        }

        private static void HandleAutostart(UserSettings settings)
        {
            if (settings.Autostart)
            {
                StartUpManager.AddApplicationToCurrentUserStartup();
            }
            else
            {
                StartUpManager.RemoveApplicationFromCurrentUserStartup();
            }
        }

        private void ReadAndApply<T>(XDocument xdoc, IUserSettings settings, string settingName, Expression<Func<IUserSettings, T>> targetProperty)
        {
            var content = xdoc.XPathSelectElement($"//setting[@name='{settingName}']/value");
            if (content == null) return;


            var text = content.Value;
            var propertyExpression = (MemberExpression)targetProperty.Body;
            var member = (PropertyInfo)propertyExpression.Member;

            object targetValue;

            if (typeof(T) == typeof(int))
            {
                //int
                targetValue = Convert.ToInt32(text);
            }
            else if (typeof(T) == typeof(byte))
            {
                //byte
                targetValue = Convert.ToByte(text);
            }
            else if (typeof(T) == typeof(bool))
            {
                //bool
                targetValue = Convert.ToBoolean(text);
            }
            else if (typeof(T) == typeof(string))
            {
                //string
                targetValue = text;
            }
            else
            {
                throw new NotImplementedException($"converting to {typeof(T).FullName} is not implemented");
            }

            member.SetValue(settings, targetValue);
        }
    }
}
