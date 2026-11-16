using com.insanitydesign.MarkdownViewerPlusPlus.Forms;
using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using Kbg.NppPluginNET;
using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static com.insanitydesign.MarkdownViewerPlusPlus.MarkdownViewerConfiguration;
using static Kbg.NppPluginNET.PluginInfrastructure.Win32;

/// <summary>
///
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus
{
    /// <summary>
    ///
    /// </summary>
    public class MarkdownViewer
    {
        /// <summary>
        ///
        /// </summary>
        public struct FileInformation
        {
            /// <summary>
            ///
            /// </summary>
            public string FileDirectory { get; set; }
            /// <summary>
            ///
            /// </summary>
            private string fileName;
            /// <summary>
            ///
            /// </summary>
            public string FileName {
                get {
                    return this.fileName;
                }
                set {
                    this.fileName = value;
                    //The extension without the leading dot ("" for none)
                    string extension = string.IsNullOrEmpty(value) ? "" : Path.GetExtension(value) ?? "";
                    this.FileExtension = extension.TrimStart('.');
                }
            }
            /// <summary>
            ///
            /// </summary>
            public string FileExtension { get; private set; }
        }

        /// <summary>
        /// Delay after the last text change before the document is rendered again
        /// </summary>
        public const int RenderDelayMilliseconds = 250;

        /// <summary>
        ///
        /// </summary>
        public IScintillaGateway Editor { get; protected set; }
        /// <summary>
        ///
        /// </summary>
        public INotepadPPGateway Notepad { get; protected set; }

        /// <summary>
        ///
        /// </summary>
        public FileInformation FileInfo { get; protected set; }

        /// <summary>
        ///
        /// </summary>
        protected AbstractRenderer renderer;

        /// <summary>
        ///
        /// </summary>
        protected bool rendererVisible = false;

        /// <summary>
        /// True if the text changed since the last render
        /// </summary>
        protected bool updateRenderer = true;

        /// <summary>
        /// Debounces renders while typing
        /// </summary>
        protected Timer renderTimer;

        /// <summary>
        ///
        /// </summary>
        public int commandId = 0;

        /// <summary>
        ///
        /// </summary>
        public int commandIdSynchronize = 2;

        /// <summary>
        ///
        /// </summary>
        public int commandIdOptions = 4;

        /// <summary>
        ///
        /// </summary>
        public int commandIdAbout = 5;

        /// <summary>
        ///
        /// </summary>
        protected MarkdownViewerConfiguration configuration;

        /// <summary>
        ///
        /// </summary>
        public Options Options {
            get {
                return this.configuration.options;
            }
        }

        /// <summary>
        ///
        /// </summary>
        public MarkdownViewer()
        {
            //Get some global references to the editor and Notepad++ engines
            this.Editor = new ScintillaGateway(PluginBase.GetCurrentScintilla());
            this.Notepad = new NotepadPPGateway();
            //The renderer (dockable panel) is created on first use, see EnsureRenderer()
            //Render with a short delay while typing
            this.renderTimer = new Timer { Interval = RenderDelayMilliseconds };
            this.renderTimer.Tick += OnRenderTimerTick;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="notification"></param>
        public void OnNotification(ScNotification notification)
        {
            //Listen to any UI update to get informed about all file changes, chars added/removed etc.
            if (this.renderer != null && this.renderer.Visible)
            {
                //Check for updates
                if (notification.Header.Code == (uint)SciMsg.SCN_UPDATEUI)
                {
                    //Update the view
                    Update((notification.Updated & (uint)SciMsg.SC_UPDATE_V_SCROLL) != 0);
                }
                else if (notification.Header.Code == (uint)NppMsg.NPPN_BUFFERACTIVATED)
                {
                    //Update the scintilla handle in all cases to keep track of which instance is active
                    UpdateEditorInformation();
                    this.Editor.CurrentBufferID = notification.Header.IdFrom;
                    Update(true, true);
                }
                else if (notification.Header.Code == (uint)SciMsg.SCN_MODIFIED && !this.updateRenderer)
                {
                    bool isInsert = (notification.ModificationType & (uint)SciMsg.SC_MOD_INSERTTEXT) != 0;
                    bool isDelete = (notification.ModificationType & (uint)SciMsg.SC_MOD_DELETETEXT) != 0;

                    //Track if any text modifications have been made
                    this.updateRenderer = isInsert || isDelete;
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="updateScrollBar"></param>
        /// <param name="updateRenderer">Render immediately (otherwise text changes are rendered after a short delay)</param>
        public void Update(bool updateScrollBar = false, bool updateRenderer = false)
        {
            //Nothing to update before the viewer has been opened once
            if (this.renderer == null) return;
            //Validate that the current file may be rendered
            if (this.configuration.ValidateFileExtension(this.FileInfo.FileExtension, this.FileInfo.FileName))
            {
                if (updateRenderer)
                {
                    this.updateRenderer = true;
                    UpdateMarkdownViewer();
                }
                else if (this.updateRenderer)
                {
                    //Text changed: (re)start the delay, rendering happens once typing pauses
                    this.renderTimer.Stop();
                    this.renderTimer.Start();
                }
                //Update the scroll bar of the Viewer Panel only in case of vertical scrolls
                if (this.configuration.options.synchronizeScrolling && updateScrollBar)
                {
                    UpdateScrollBar();
                }
            }
            else
            {
                this.renderTimer.Stop();
                this.renderer.Render($@"<p>
Your configuration settings do not include the currently selected file extension.<br />
The rendered file extensions are <b>'{WebUtility.HtmlEncode(this.configuration.options.fileExtensions)}'</b>.<br />
The current file is <i>'{WebUtility.HtmlEncode(this.FileInfo.FileName)}'</i>.
                </p>", this.FileInfo);
            }
        }

        /// <summary>
        /// Renders the changes made while typing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void OnRenderTimerTick(object sender, EventArgs e)
        {
            this.renderTimer.Stop();
            if (this.renderer == null || !this.renderer.Visible) return;
            UpdateMarkdownViewer();
            //Re-rendering resets the viewer's scroll position
            if (this.configuration.options.synchronizeScrolling)
            {
                UpdateScrollBar();
            }
        }

        /// <summary>
        ///
        /// </summary>
        protected void UpdateScrollBar()
        {
            if (this.renderer == null) return;
            try
            {
                ScrollInfo scrollInfo = this.Editor.GetScrollInfo(ScrollInfoMask.SIF_RANGE | ScrollInfoMask.SIF_TRACKPOS | ScrollInfoMask.SIF_PAGE, ScrollInfoBar.SB_VERT);
                double totalRange = scrollInfo.nMax - scrollInfo.nMin + 1;
                double scrollRatio;

                // Is "Enable scrolling beyond last line" checked?
                if (Editor.GetEndAtLastLine() == false)
                {
                    var actualThumbHeight = totalRange / (totalRange / scrollInfo.nPage - 1);
                    var actualTrackPos = scrollInfo.nTrackPos * actualThumbHeight / scrollInfo.nPage;

                    scrollRatio = Math.Min(1, actualTrackPos / (totalRange - actualThumbHeight));
                }
                else
                {
                    scrollRatio = scrollInfo.nTrackPos / (totalRange - scrollInfo.nPage);
                }

                //Guard against divisions by zero for short documents
                if (double.IsNaN(scrollRatio) || double.IsInfinity(scrollRatio)) scrollRatio = 0;
                this.renderer.ScrollByRatioVertically(Math.Max(0, Math.Min(1, scrollRatio)));
            }
            catch { }
        }

        /// <summary>
        ///
        /// </summary>
        public void CommandMenuInit()
        {
            this.configuration = new MarkdownViewerConfiguration();

            //Register our commands
            PluginBase.SetCommand(this.commandId, Main.PluginName, MarkdownViewerCommand, new ShortcutKey(true, false, true, System.Windows.Forms.Keys.M));
            //Separator
            PluginBase.SetCommand(this.commandId + 1, "---", null);
            //Synchronized scrolling
            PluginBase.SetCommand(this.commandIdSynchronize, "Synchronize scrolling (Editor -> Viewer)", SynchronizeScrollingCommand, this.configuration.options.synchronizeScrolling);
            //Separator
            PluginBase.SetCommand(this.commandIdSynchronize + 1, "---", null);
            //Options
            PluginBase.SetCommand(this.commandIdOptions, "Options", OptionsCommand);
            //About
            PluginBase.SetCommand(this.commandIdAbout, "About", AboutCommand);
        }

        /// <summary>
        ///
        /// </summary>
        public void OptionsCommand()
        {
            using (MarkdownViewerOptions options = new MarkdownViewerOptions(this.configuration))
            {
                if (options.ShowDialog(Control.FromHandle(PluginBase.GetCurrentScintilla())) == DialogResult.OK)
                {
                    //Update after something potentially changed in the settings dialog
                    Update(true, true);
                }
            }
        }

        /// <summary>
        /// Show the AboutDialog as modal to the Notepad++ window
        /// </summary>
        public void AboutCommand()
        {
            using (AboutDialog about = new AboutDialog())
            {
                about.ShowDialog(Control.FromHandle(PluginBase.GetCurrentScintilla()));
            }
        }

        /// <summary>
        /// Check/Uncheck the configuration item
        /// </summary>
        protected void SynchronizeScrollingCommand()
        {
            this.configuration.options.synchronizeScrolling = !this.configuration.options.synchronizeScrolling;
            Win32.CheckMenuItem(Win32.GetMenu(PluginBase.nppData._nppHandle), PluginBase._funcItems.Items[this.commandIdSynchronize]._cmdID, Win32.MF_BYCOMMAND | (this.configuration.options.synchronizeScrolling ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
            if (this.configuration.options.synchronizeScrolling)
            {
                UpdateScrollBar();
            }
        }

        /// <summary>
        /// Adds the toolbar icon, with separate icons for the Fluent light and dark mode toolbars (Notepad++ 8+).
        /// The handles are used by Notepad++ for its whole lifetime and therefore not released.
        /// </summary>
        public void SetToolBarIcon()
        {
            Bitmap icon = Resources.markdown_16x16_solid;
            int commandId = PluginBase._funcItems.Items[this.commandId]._cmdID;

            toolbarIconsWithDarkMode darkModeIcons = new toolbarIconsWithDarkMode();
            darkModeIcons.hToolbarBmp = icon.GetHbitmap();
            darkModeIcons.hToolbarIcon = icon.GetHicon();
            using (Bitmap darkModeIcon = Recolor(icon, Color.FromArgb(0xE0, 0xE0, 0xE0)))
            {
                darkModeIcons.hToolbarIconDarkMode = darkModeIcon.GetHicon();
            }

            IntPtr pTbIcons = Marshal.AllocHGlobal(Marshal.SizeOf(darkModeIcons));
            try
            {
                Marshal.StructureToPtr(darkModeIcons, pTbIcons, false);
                IntPtr added = Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_ADDTOOLBARICON_FORDARKMODE, commandId, pTbIcons);
                if (added != IntPtr.Zero) return;
            }
            finally
            {
                Marshal.FreeHGlobal(pTbIcons);
            }

            //Notepad++ < 8.0
            toolbarIcons toolbarIcons = new toolbarIcons();
            toolbarIcons.hToolbarBmp = darkModeIcons.hToolbarBmp;
            toolbarIcons.hToolbarIcon = darkModeIcons.hToolbarIcon;
            pTbIcons = Marshal.AllocHGlobal(Marshal.SizeOf(toolbarIcons));
            try
            {
                Marshal.StructureToPtr(toolbarIcons, pTbIcons, false);
                Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_ADDTOOLBARICON, commandId, pTbIcons);
            }
            finally
            {
                Marshal.FreeHGlobal(pTbIcons);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="source"></param>
        /// <param name="color"></param>
        /// <returns>A copy of the image with all pixels in the given color, keeping the transparency</returns>
        protected static Bitmap Recolor(Bitmap source, Color color)
        {
            Bitmap recolored = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int x = 0; x < source.Width; x++)
            {
                for (int y = 0; y < source.Height; y++)
                {
                    recolored.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, color));
                }
            }
            return recolored;
        }

        /// <summary>
        /// Creates the viewer panel. Notepad++ loads plugins before its docking manager exists, so the dockable
        /// panel must not be registered during plugin initialization (it would be ignored) but on first use.
        /// </summary>
        /// <returns></returns>
        protected AbstractRenderer EnsureRenderer()
        {
            if (this.renderer == null)
            {
                this.renderer = new MarkdownViewerRenderer(this);
            }
            return this.renderer;
        }

        /// <summary>
        ///
        /// </summary>
        public void MarkdownViewerCommand()
        {
            EnsureRenderer();
            bool visibility = !this.renderer.Visible;
            ToggleToolbarIcon(visibility);
            //Show
            if (visibility)
            {
                UpdateEditorInformation();
                Update(true, true);
                Editor.SetFocus(true);
            }
            else
            {
                this.renderTimer.Stop();
            }
        }

        /// <summary>
        ///
        /// </summary>
        protected void UpdateEditorInformation()
        {
            this.Editor.SetScintillaHandle(PluginBase.GetCurrentScintilla());
            this.FileInfo = new FileInformation() {
                FileName = this.Notepad.GetCurrentFileName(),
                FileDirectory = this.Notepad.GetCurrentDirectory()
            };
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="show"></param>
        /// <returns></returns>
        public void ToggleToolbarIcon(bool show = true)
        {
            //To show or not to show
            NppMsg msg = show ? NppMsg.NPPM_DMMSHOW : NppMsg.NPPM_DMMHIDE;
            //
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)msg, 0, this.renderer.Handle);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_SETMENUITEMCHECK, PluginBase._funcItems.Items[this.commandId]._cmdID, show ? 1 : 0);
            //
            this.rendererVisible = this.renderer.Visible;
        }

        /// <summary>
        /// Renders the current text if it changed
        /// </summary>
        protected void UpdateMarkdownViewer()
        {
            this.renderTimer.Stop();
            try
            {
                if (this.updateRenderer)
                {
                    this.updateRenderer = false;
                    this.renderer.Render(this.Editor.GetText(this.Editor.GetLength() + 1), this.FileInfo);
                }
            }
            catch
            {
                this.renderer.Render("<p>Couldn't render the currently selected file!</p>", this.FileInfo);
            }
        }

        /// <summary>
        /// Save the configuration
        /// </summary>
        public void PluginCleanUp()
        {
            this.renderTimer.Stop();
            this.configuration.Save();
        }
    }
}
