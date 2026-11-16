using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using com.insanitydesign.MarkdownViewerPlusPlus.Windows;
using Kbg.NppPluginNET;
using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static com.insanitydesign.MarkdownViewerPlusPlus.Windows.WindowsMessage;
using TheArtOfDev.HtmlRenderer.PdfSharp;
using TheArtOfDev.HtmlRenderer.Core.Entities;
using System.Xml.Linq;
using PdfSharp.Pdf;
using System.IO;
using System.Net;
using com.insanitydesign.MarkdownViewerPlusPlus.Helper;
using static com.insanitydesign.MarkdownViewerPlusPlus.MarkdownViewer;
using Markdig;
using System.Text.RegularExpressions;
using System.Linq;

/// <summary>
///
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus.Forms
{
    /// <summary>
    ///
    /// </summary>
    public abstract partial class AbstractRenderer : Form
    {
        /// <summary>
        /// Outlook OlItemType.olMailItem
        /// </summary>
        private const int OutlookMailItem = 0;

        /// <summary>
        /// Outlook OlBodyFormat.olFormatPlain / olFormatHTML
        /// </summary>
        private const int OutlookFormatPlain = 1, OutlookFormatHtml = 2;

        /// <summary>
        ///
        /// </summary>
        protected Icon toolbarIcon;

        /// <summary>
        /// Native icon handle backing <see cref="toolbarIcon"/> (owned by us, see <see cref="DisposeResources"/>)
        /// </summary>
        protected IntPtr toolbarIconHandle = IntPtr.Zero;

        /// <summary>
        ///
        /// </summary>
        protected MarkdownViewer markdownViewer;

        /// <summary>
        ///
        /// </summary>
        protected string assemblyTitle;

        /// <summary>
        ///
        /// </summary>
        protected virtual string RawText { get; set; }

        /// <summary>
        ///
        /// </summary>
        protected virtual string ConvertedText { get; set; }

        /// <summary>
        ///
        /// </summary>
        protected virtual FileInformation FileInfo { get; set; }

        /// <summary>
        ///
        /// </summary>
        protected MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        /// <summary>
        ///
        /// </summary>
        protected static readonly Regex patternCSSImportStatements = new Regex("(@import\\s.+;)", RegexOptions.Compiled);

        /// <summary>
        /// The custom CSS the cached <see cref="cssCache"/> was built from
        /// </summary>
        private string cssCacheSource;

        /// <summary>
        ///
        /// </summary>
        private string cssCache;

        /// <summary>
        /// Lazily determined, Outlook is looked up in the registry only once
        /// </summary>
        private readonly Lazy<bool> outlookInstalled = new Lazy<bool>(() =>
        {
            try
            {
                return Type.GetTypeFromProgID("Outlook.Application") != null;
            }
            catch
            {
                return false;
            }
        });

        /// <summary>
        /// Browser used for the print preview, kept until the next print or until disposed
        /// </summary>
        private WebBrowser printBrowser;

        /// <summary>
        ///
        /// </summary>
        /// <param name="markdownViewer"></param>
        public AbstractRenderer(MarkdownViewer markdownViewer)
        {
            this.markdownViewer = markdownViewer;
            InitializeComponent();
            Init();
        }

        /// <summary>
        ///
        /// </summary>
        protected virtual void Init()
        {
            //Create the tab icon (fuchsia is the transparent color of the bitmap)
            using (Bitmap newBmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(newBmp))
                using (ImageAttributes attr = new ImageAttributes())
                {
                    ColorMap[] colorMap = { new ColorMap { OldColor = Color.Fuchsia, NewColor = Color.FromKnownColor(KnownColor.ButtonFace) } };
                    attr.SetRemapTable(colorMap);
                    g.DrawImage(Resources.markdown_16x16_solid_bmp, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, attr);
                }
                this.toolbarIconHandle = newBmp.GetHicon();
                this.toolbarIcon = Icon.FromHandle(this.toolbarIconHandle);
            }
            //Get the AssemblyTitle
            this.assemblyTitle = ((AssemblyTitleAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyTitleAttribute), false)).Title;
            //
            NppTbData _nppTbData = new NppTbData();
            _nppTbData.hClient = this.Handle;
            _nppTbData.pszName = this.assemblyTitle;
            _nppTbData.dlgID = this.markdownViewer.commandId;
            _nppTbData.uMask = NppTbMsg.DWS_DF_CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR;
            _nppTbData.hIconTab = (uint)toolbarIcon.Handle;
            _nppTbData.pszModuleName = Main.PluginName;
            //Notepad++ keeps referencing this data (and the marshalled strings) while the panel is registered,
            //so it is intentionally not freed
            IntPtr _ptrNppTbData = Marshal.AllocHGlobal(Marshal.SizeOf(_nppTbData));
            Marshal.StructureToPtr(_nppTbData, _ptrNppTbData, false);
            //Register dockable window and hide initially
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_DMMREGASDCKDLG, 0, _ptrNppTbData);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_DMMHIDE, 0, this.Handle);

            //Hide the E-mail items if Outlook is not installed
            this.sendAsTextMail.Visible = IsOutlookInstalled();
            this.sendAsHTMLMail.Visible = IsOutlookInstalled();
        }

        /// <summary>
        /// Releases native and lazily created resources, called from <see cref="Dispose(bool)"/>
        /// </summary>
        protected virtual void DisposeResources()
        {
            if (this.printBrowser != null)
            {
                this.printBrowser.Dispose();
                this.printBrowser = null;
            }
            if (this.toolbarIconHandle != IntPtr.Zero)
            {
                DestroyIcon(this.toolbarIconHandle);
                this.toolbarIconHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="hIcon"></param>
        /// <returns></returns>
        [DllImport("user32.dll", SetLastError = true)]
        protected static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        ///
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m)
        {
            //Listen for the closing of the dockable panel to toggle the toolbar icon
            switch (m.Msg)
            {
                case (int)WM_NOTIFY:
                    var notify = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR));
                    if (notify.code == (int)DockMgrMsg.DMN_CLOSE)
                    {
                        this.markdownViewer.ToggleToolbarIcon(false);
                    }
                    break;
            }
            //Continue the processing, as we only toggle
            base.WndProc(ref m);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="text"></param>
        /// <param name="fileInfo"></param>
        public virtual void Render(string text, FileInformation fileInfo)
        {
            FileInfo = fileInfo;
            RawText = text;
            ConvertedText = Markdown.ToHtml(text ?? "", this.markdownPipeline);
        }

        /// <summary>
        /// Runs a user action and reports errors instead of letting them crash Notepad++
        /// </summary>
        /// <param name="action"></param>
        /// <param name="description">What failed, e.g. "export the PDF"</param>
        protected void RunSafely(Action action, string description)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"Could not {description}:{Environment.NewLine}{exception.Message}", Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.markdownViewer.Update(true, true);
        }

        /// <summary>
        /// Scroll the rendered panel vertically based on the given ration
        /// taken from Notepad++
        /// </summary>
        /// <param name="scrollRatio"></param>
        public abstract void ScrollByRatioVertically(double scrollRatio);

        /// <summary>
        ///
        /// </summary>
        /// <param name="html"></param>
        /// <param name="title"></param>
        /// <returns></returns>
        protected string BuildHtml(string html = "", string title = "")
        {
            //
            if (string.IsNullOrEmpty(title)) title = this.assemblyTitle;
            //
            return $@"
<!DOCTYPE html>
<html>
    <head>
        <meta charset=""UTF-8"" />
        <meta name=""author"" content=""{WebUtility.HtmlEncode(this.assemblyTitle)}"" />
        <title>{WebUtility.HtmlEncode(title)}</title>
        <style type=""text/css"">
            {this.getCSS()}
        </style>
      </head>
    <body>
        {html}
    </body>
</html>
            ";
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void sendAsTextMail_Click(object sender, EventArgs e)
        {
            RunSafely(() => SendMail(OutlookFormatPlain, RawText), "create the e-mail");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void sendAsHTMLMail_Click(object sender, EventArgs e)
        {
            RunSafely(() => SendMail(OutlookFormatHtml, BuildHtml(HtmlSanitizer.Sanitize(ConvertedText), this.FileInfo.FileName)), "create the e-mail");
        }

        /// <summary>
        /// Opens a new Outlook mail (late bound, so no Office interop assembly is needed)
        /// </summary>
        /// <param name="bodyFormat"></param>
        /// <param name="body"></param>
        protected void SendMail(int bodyFormat, string body)
        {
            //Double-check
            if (!IsOutlookInstalled()) return;

            object outlook = null, message = null;
            try
            {
                outlook = Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application"));
                message = ((dynamic)outlook).CreateItem(OutlookMailItem);
                dynamic mail = message;
                mail.Subject = this.FileInfo.FileName;
                mail.BodyFormat = bodyFormat;
                if (bodyFormat == OutlookFormatHtml)
                {
                    mail.HTMLBody = body;
                }
                else
                {
                    mail.Body = body;
                }
                mail.Display(false);
            }
            finally
            {
                if (message != null) Marshal.ReleaseComObject(message);
                if (outlook != null) Marshal.ReleaseComObject(outlook);
            }
        }

        /// <summary>
        /// Shows a save dialog for an export
        /// </summary>
        /// <param name="extension">e.g. "pdf"</param>
        /// <param name="filter"></param>
        /// <returns>The chosen file or null if cancelled</returns>
        protected string AskForExportFile(string extension, string filter)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                //Default name of the file is the editor file name
                saveFileDialog.FileName = Path.GetFileNameWithoutExtension(this.FileInfo.FileName ?? "") + "." + extension;
                //The current path
                saveFileDialog.InitialDirectory = this.markdownViewer.Notepad.GetCurrentDirectory();
                saveFileDialog.Filter = filter;
                saveFileDialog.OverwritePrompt = true;
                return saveFileDialog.ShowDialog(this) == DialogResult.OK ? saveFileDialog.FileName : null;
            }
        }

        /// <summary>
        /// Opens an exported file with its default application
        /// </summary>
        /// <param name="fileName"></param>
        protected void OpenExport(string fileName)
        {
            RunSafely(() => Process.Start(fileName), $"open '{fileName}'");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void exportAsHTMLMenuItem_Click(object sender, EventArgs e)
        {
            string fileName = AskForExportFile("html", "HTML files (*.html)|*.html|All files (*.*)|*.*");
            if (fileName == null) return;

            bool exported = false;
            RunSafely(() =>
            {
                string html = BuildHtml(ConvertedText, this.FileInfo.FileName);
                try
                {
                    //Pretty print if the HTML happens to be well-formed XML
                    html = XDocument.Parse(html).ToString();
                }
                catch (System.Xml.XmlException) { }
                File.WriteAllText(fileName, html);
                exported = true;
            }, $"export the HTML to '{fileName}'");

            //Open if requested
            if (exported && this.markdownViewer.Options.htmlOpenExport)
            {
                OpenExport(fileName);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void exportAsPDFMenuItem_Click(object sender, EventArgs e)
        {
            string fileName = AskForExportFile("pdf", "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*");
            if (fileName == null) return;

            bool exported = false;
            RunSafely(() =>
            {
                //Build a config based on made settings
                PdfGenerateConfig pdfConfig = new PdfGenerateConfig();
                pdfConfig.PageOrientation = this.markdownViewer.Options.pdfOrientation;
                pdfConfig.PageSize = this.markdownViewer.Options.pdfPageSize;
                //Set margins
                int[] margins = this.markdownViewer.Options.GetMargins();
                pdfConfig.MarginLeft = MilimiterToPoint(margins[0]);
                pdfConfig.MarginTop = MilimiterToPoint(margins[1]);
                pdfConfig.MarginRight = MilimiterToPoint(margins[2]);
                pdfConfig.MarginBottom = MilimiterToPoint(margins[3]);
                //Generate PDF and save
                using (PdfDocument pdf = PdfGenerator.GeneratePdf(BuildHtml(ConvertedText, this.FileInfo.FileName), pdfConfig,
                    PdfGenerator.ParseStyleSheet(this.getCSS()), OnStylesheetLoad, OnPdfImageLoad))
                {
                    pdf.Save(fileName);
                }
                exported = true;
            }, $"export the PDF to '{fileName}'");

            //Open if requested
            if (exported && this.markdownViewer.Options.pdfOpenExport)
            {
                OpenExport(fileName);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void sendToPrinter_Click(object sender, EventArgs e)
        {
            RunSafely(() =>
            {
                //Only one print preview browser at a time
                if (this.printBrowser != null)
                {
                    this.printBrowser.Dispose();
                }
                this.printBrowser = new WebBrowser();
                this.printBrowser.ScriptErrorsSuppressed = true;
                this.printBrowser.AllowWebBrowserDrop = false;
                this.printBrowser.IsWebBrowserContextMenuEnabled = false;
                this.printBrowser.WebBrowserShortcutsEnabled = false;
                this.printBrowser.Parent = this;
                this.printBrowser.DocumentCompleted += (browser, webBrowserEvent) =>
                {
                    WebBrowser webBrowser = (WebBrowser)browser;
                    //Do not follow links clicked in the preview
                    webBrowser.AllowNavigation = false;
                    webBrowser.Size = webBrowser.MaximumSize;
                    webBrowser.ShowPrintPreviewDialog();
                };
                //The IE engine executes scripts in raw HTML, so remove all active content first
                this.printBrowser.DocumentText = BuildHtml(HtmlSanitizer.Sanitize(ConvertedText), this.FileInfo.FileName);
            }, "print the document");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void sendToClipboard_Click(object sender, EventArgs e)
        {
            RunSafely(() => ClipboardHelper.CopyToClipboard(BuildHtml(ConvertedText, this.FileInfo.FileName), ConvertedText), "copy to the clipboard");
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns>The base CSS with the user's custom CSS (cached until the custom CSS changes)</returns>
        protected virtual String getCSS()
        {
            string customCss = this.markdownViewer.Options.HtmlCssStyle;
            if (this.cssCache == null || this.cssCacheSource != customCss)
            {
                //Get all @import statements from the custom CSS, they have to come first
                string cssImportStatements = string.Join(Environment.NewLine,
                    patternCSSImportStatements.Matches(customCss).Cast<Match>().Select(match => match.Value));

                //Return a CSS with the @import statements in front, the base MarkdownViewer++ CSS and the rest of the custom CSS from the user
                this.cssCache = cssImportStatements + Environment.NewLine + Resources.MarkdownViewerHTML + Environment.NewLine + patternCSSImportStatements.Replace(customCss, "").Trim();
                this.cssCacheSource = customCss;
            }
            return this.cssCache;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="src"></param>
        /// <returns>True for http(s) URLs</returns>
        protected static bool IsRemote(string src)
        {
            return src != null && (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("//"));
        }

        /// <summary>
        /// Remote stylesheets are only loaded if allowed or referenced by the user's own custom CSS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="stylesheetLoadEvent"></param>
        protected void OnStylesheetLoad(object sender, HtmlStylesheetLoadEventArgs stylesheetLoadEvent)
        {
            string src = stylesheetLoadEvent.Src;
            if (IsRemote(src) && !this.markdownViewer.Options.allowRemoteImages
                && this.markdownViewer.Options.HtmlCssStyle.IndexOf(src, StringComparison.OrdinalIgnoreCase) < 0)
            {
                stylesheetLoadEvent.SetStyleSheet = "";
            }
        }

        /// <summary>
        /// Image handling for PDF exports: Resolve paths relative to the document and block remote images unless allowed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="imageLoadEvent"></param>
        protected void OnPdfImageLoad(object sender, HtmlImageLoadEventArgs imageLoadEvent)
        {
            string src = imageLoadEvent.Src;
            if (IsRemote(src))
            {
                if (!this.markdownViewer.Options.allowRemoteImages) imageLoadEvent.Handled = true;
                return;
            }
            string localPath = ResolveLocalPath(src);
            if (localPath != null && File.Exists(localPath))
            {
                imageLoadEvent.Callback(localPath);
            }
        }

        /// <summary>
        /// Resolves "file:" URIs, absolute and relative (to the current document) paths
        /// </summary>
        /// <param name="src"></param>
        /// <returns>The local file path or null for other sources (e.g. data: or remote URIs)</returns>
        protected string ResolveLocalPath(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;
            Uri uri;
            if (Uri.TryCreate(src, UriKind.Absolute, out uri))
            {
                //file:///C:/image.png, C:\image.png, \\server\share\image.png
                if (uri.IsFile) return uri.LocalPath;
                //http:, data:, mailto: ... (but "file:relative/image.png" is resolved below)
                if (!src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
            }

            //Relative paths: Strip a "file:" scheme, query and fragment
            string path = Regex.Replace(src, "^file:/*", "", RegexOptions.IgnoreCase);
            path = path.Split('?', '#')[0];
            try
            {
                path = Uri.UnescapeDataString(path).Replace('/', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(path))
                {
                    if (string.IsNullOrEmpty(this.FileInfo.FileDirectory)) return null;
                    path = Path.Combine(this.FileInfo.FileDirectory, path);
                }
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="mm"></param>
        /// <returns></returns>
        protected int MilimiterToPoint(int mm)
        {
            return (int)(mm * 2.834646);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public bool IsOutlookInstalled()
        {
            return this.outlookInstalled.Value;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.markdownViewer.OptionsCommand();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void aboutToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.markdownViewer.AboutCommand();
        }
    }
}
