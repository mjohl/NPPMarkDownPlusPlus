using Kbg.NppPluginNET.PluginInfrastructure;
using PdfSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

/// <summary>
///
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus
{
    /// <summary>
    /// Loads and saves the plugin options from/to the plugin's ini file in the Notepad++ plugin config directory
    /// </summary>
    public class MarkdownViewerConfiguration
    {
        /// <summary>
        /// All plugin options. The (public and private) field names are the keys in the ini file.
        /// Field initializers are the defaults used for keys not (yet) present in the ini file.
        /// </summary>
        public class Options
        {
            /// <summary>
            /// Scroll the viewer along with the editor
            /// </summary>
            public bool synchronizeScrolling = false;
            /// <summary>
            /// Comma-separated list of rendered file extensions, empty for all files
            /// </summary>
            public string fileExtensions = "";
            /// <summary>
            /// Render unsaved "new X" files although they have no extension
            /// </summary>
            public bool inclNewFiles = true;
            /// <summary>
            /// Load images (and stylesheets) from http/https sources referenced by the document
            /// </summary>
            public bool allowRemoteImages = false;
            /// <summary>
            /// Custom CSS, see <see cref="HtmlCssStyle"/>
            /// </summary>
            private string htmlCssStyle = "";
            /// <summary>
            /// Custom CSS added to the rendered HTML
            /// </summary>
            public string HtmlCssStyle {
                get { return this.htmlCssStyle ?? ""; }
                set { this.htmlCssStyle = value ?? ""; }
            }
            /// <summary>
            ///
            /// </summary>
            public PageOrientation pdfOrientation = PageOrientation.Portrait;
            /// <summary>
            ///
            /// </summary>
            public PageSize pdfPageSize = PageSize.A4;
            /// <summary>
            /// PDF margins in mm as "left,top,right,bottom"
            /// </summary>
            public string margins = "5,5,5,5";
            /// <summary>
            ///
            /// </summary>
            public bool pdfOpenExport = false;
            /// <summary>
            ///
            /// </summary>
            public bool htmlOpenExport = false;

            /// <summary>
            ///
            /// </summary>
            /// <returns>The margins (left, top, right, bottom) in mm</returns>
            public int[] GetMargins()
            {
                try
                {
                    int[] values = (this.margins ?? "").Split(',').Select(x => int.Parse(x.Trim())).ToArray();
                    if (values.Length == 4 && values.All(value => value >= 0)) return values;
                }
                catch (FormatException) { }
                catch (OverflowException) { }
                //Default to 5mm
                return new int[] { 5, 5, 5, 5 };
            }
        }

        /// <summary>
        /// Prefix marking a base64 encoded value in the ini file (used for multi-line values)
        /// </summary>
        protected const string Base64Prefix = "base64:";

        /// <summary>
        /// Legacy (&lt;= 0.8.2) encoding of line breaks in the CSS option
        /// </summary>
        protected const string LegacyNewLine = @" \n ";

        /// <summary>
        /// Value returned by GetPrivateProfileString for missing keys
        /// </summary>
        protected const string MissingValue = "\u0001__missing__\u0001";

        /// <summary>
        ///
        /// </summary>
        protected string iniFilePath = null;

        /// <summary>
        ///
        /// </summary>
        protected string assemblyName = "";

        /// <summary>
        ///
        /// </summary>
        public Options options = new Options();

        /// <summary>
        ///
        /// </summary>
        public MarkdownViewerConfiguration()
        {
            this.assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            Init();
        }

        /// <summary>
        /// Used for initing and re-initing
        /// </summary>
        public void Init()
        {
            //
            StringBuilder sbIniFilePath = new StringBuilder(Win32.MAX_PATH);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_GETPLUGINSCONFIGDIR, Win32.MAX_PATH, sbIniFilePath);
            string configDirectory = sbIniFilePath.ToString();

            // if config path doesn't exist, we create it
            if (!string.IsNullOrEmpty(configDirectory) && !Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            //
            this.iniFilePath = Path.Combine(configDirectory, this.assemblyName + ".ini");
            Load();
        }

        /// <summary>
        /// Load all configuration settings, keeping the defaults for settings not present in the ini file
        /// </summary>
        public void Load()
        {
            Options loaded = new Options();
            foreach (FieldInfo field in typeof(Options).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string value = ReadValue(field.Name);
                if (value == null) continue;

                if (field.FieldType == typeof(bool))
                {
                    int intValue;
                    if (int.TryParse(value.Trim(), out intValue)) field.SetValue(loaded, intValue != 0);
                }
                else if (field.FieldType == typeof(string))
                {
                    field.SetValue(loaded, DecodeString(value));
                }
                else if (field.FieldType.IsEnum)
                {
                    try
                    {
                        field.SetValue(loaded, Enum.Parse(field.FieldType, value.Trim()));
                    }
                    catch (ArgumentException) { } //Keep the default
                }
            }
            this.options = loaded;
        }

        /// <summary>
        /// Save all made changes to the configuration
        /// </summary>
        public void Save()
        {
            foreach (FieldInfo field in typeof(Options).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = field.GetValue(this.options);
                if (field.FieldType == typeof(bool))
                {
                    Win32.WritePrivateProfileString(this.assemblyName, field.Name, ((bool)value) ? "1" : "0", this.iniFilePath);
                }
                else if (field.FieldType == typeof(string))
                {
                    Win32.WritePrivateProfileString(this.assemblyName, field.Name, EncodeString((string)value), this.iniFilePath);
                }
                else if (field.FieldType.IsEnum)
                {
                    Win32.WritePrivateProfileString(this.assemblyName, field.Name, value.ToString(), this.iniFilePath);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="key"></param>
        /// <returns>The raw ini value or null if the key does not exist</returns>
        protected string ReadValue(string key)
        {
            StringBuilder sbValue = new StringBuilder(32767);
            Win32.GetPrivateProfileString(this.assemblyName, key, MissingValue, sbValue, (uint)sbValue.Capacity, this.iniFilePath);
            string value = sbValue.ToString();
            return value == MissingValue ? null : value;
        }

        /// <summary>
        /// Ini values are single-line, so values with line breaks are stored base64 encoded
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string EncodeString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { '\r', '\n' }) < 0 && !value.StartsWith(Base64Prefix) && value.Trim() == value) return value;
            return Base64Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Decodes values written by <see cref="EncodeString(string)"/> and the legacy " \n " line break encoding
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string DecodeString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.StartsWith(Base64Prefix))
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(Base64Prefix.Length)));
                }
                catch (FormatException)
                {
                    return value;
                }
            }
            return value.Replace(LegacyNewLine, Environment.NewLine);
        }

        /// <summary>
        /// Checks whether a file should be rendered based on the configured file extensions
        /// </summary>
        /// <param name="fileExtension">The extension without leading dot</param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public bool ValidateFileExtension(string fileExtension, string fileName = "")
        {
            string[] extensions = ParseFileExtensions(this.options.fileExtensions);
            //Nothing set -> Render all
            if (extensions.Length == 0) return true;
            //Something set but nothing given, check for "new " files (dirty dirty ^^)
            if (string.IsNullOrEmpty(fileExtension)) return this.options.inclNewFiles && (fileName ?? "").StartsWith("new ");
            //Otherwise check for an exact (case-insensitive) match
            string extension = fileExtension.TrimStart('.');
            return extensions.Any(entry => string.Equals(entry, extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Splits a list like "md, .markdown;txt" into its extensions without leading dots
        /// </summary>
        /// <param name="fileExtensions"></param>
        /// <returns></returns>
        public static string[] ParseFileExtensions(string fileExtensions)
        {
            return (fileExtensions ?? "")
                .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim().TrimStart('.'))
                .Where(entry => entry.Length > 0)
                .ToArray();
        }
    }
}
