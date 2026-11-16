using System;
using System.Collections.Generic;
using System.IO;
using Svg;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Threading;
using TheArtOfDev.HtmlRenderer.Core.Entities;
using static com.insanitydesign.MarkdownViewerPlusPlus.MarkdownViewer;

/// <summary>
///
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus.Forms
{
    /// <summary>
    ///
    /// </summary>
    public class MarkdownViewerRenderer : AbstractRenderer
    {
        /// <summary>
        /// Upper bound for the size of rasterized SVG images
        /// </summary>
        protected const int MaxSvgDimension = 4096;

        /// <summary>
        ///
        /// </summary>
        public MarkdownViewerHtmlPanel markdownViewerHtmlPanel;

        /// <summary>
        /// Incremented with every render; images loaded for an older render are discarded
        /// </summary>
        protected int renderGeneration = 0;

        /// <summary>
        /// Images handed to the HTML panel for the current render. HtmlRenderer does not dispose images
        /// provided via callback, so they are disposed once the next render replaced them.
        /// </summary>
        protected List<Image> renderedImages = new List<Image>();

        /// <summary>
        ///
        /// </summary>
        /// <param name="markdownViewer"></param>
        public MarkdownViewerRenderer(MarkdownViewer markdownViewer) : base(markdownViewer)
        {
        }

        /// <summary>
        ///
        /// </summary>
        protected override void Init()
        {
            base.Init();
            //
            this.markdownViewerHtmlPanel = new MarkdownViewerHtmlPanel();
            //Add a custom image and stylesheet loader
            this.markdownViewerHtmlPanel.ImageLoad += OnImageLoad;
            this.markdownViewerHtmlPanel.StylesheetLoad += OnStylesheetLoad;
            //Add to view
            this.Controls.Add(this.markdownViewerHtmlPanel);
            this.Controls.SetChildIndex(this.markdownViewerHtmlPanel, 0);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="text"></param>
        /// <param name="fileInfo"></param>
        public override void Render(string text, FileInformation fileInfo)
        {
            base.Render(text, fileInfo);

            //Start a new image generation before the panel requests the images of the new document
            this.renderGeneration++;
            List<Image> previousImages = this.renderedImages;
            this.renderedImages = new List<Image>();

            this.markdownViewerHtmlPanel.Text = BuildHtml(ConvertedText, fileInfo.FileName);

            //The panel no longer references the images of the previous document
            previousImages.ForEach(image => image.Dispose());
        }

        /// <summary>
        /// Scroll the rendered panel vertically based on the given ration
        /// taken from Notepad++
        /// </summary>
        /// <param name="scrollRatio"></param>
        public override void ScrollByRatioVertically(double scrollRatio)
        {
            this.markdownViewerHtmlPanel.ScrollByRatioVertically(scrollRatio);
        }

        /// <summary>
        /// Custom image loader: Local files (relative to the document, without locking them), SVG images
        /// (not supported natively) and the remote image policy.
        /// @see https://htmlrenderer.codeplex.com/wikipage?title=Rendering%20SVG%20images
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="imageLoadEvent"></param>
        protected void OnImageLoad(object sender, HtmlImageLoadEventArgs imageLoadEvent)
        {
            string src = imageLoadEvent.Src;
            if (string.IsNullOrWhiteSpace(src)) return;

            if (IsRemote(src))
            {
                if (!this.markdownViewer.Options.allowRemoteImages)
                {
                    //Blocked: Nothing is loaded, the (empty) callback shows the "image not available" icon
                    imageLoadEvent.Handled = true;
                    imageLoadEvent.Callback();
                    return;
                }
                //HtmlRenderer loads remote raster images itself, SVGs (also from "badge" services) are converted here
                Uri uri;
                if (Uri.TryCreate(src.StartsWith("//") ? "https:" + src : src, UriKind.Absolute, out uri)
                    && (IsSvg(uri.AbsolutePath) || uri.Query.Contains("svg=")))
                {
                    imageLoadEvent.Handled = true;
                    DownloadSvg(uri, imageLoadEvent);
                }
                return;
            }

            string localPath = ResolveLocalPath(src);
            if (localPath != null)
            {
                //Handled, so no error border is drawn while loading in the background
                imageLoadEvent.Handled = true;
                int generation = this.renderGeneration;
                ThreadPool.QueueUserWorkItem(state => LoadImageFromFile(localPath, imageLoadEvent, generation));
            }
            //Anything else (e.g. data: URIs) is handled by HtmlRenderer
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        protected static bool IsSvg(string path)
        {
            return string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runs on a thread pool thread
        /// </summary>
        /// <param name="path"></param>
        /// <param name="imageLoadEvent"></param>
        /// <param name="generation"></param>
        protected void LoadImageFromFile(string path, HtmlImageLoadEventArgs imageLoadEvent, int generation)
        {
            Image image = null;
            try
            {
                //Read into memory, so the file is not locked while displayed
                byte[] data = File.ReadAllBytes(path);
                using (MemoryStream stream = new MemoryStream(data))
                {
                    if (IsSvg(path))
                    {
                        image = ConvertSvgToBitmap(SvgDocument.Open<SvgDocument>(stream));
                    }
                    else
                    {
                        using (Image loaded = Image.FromStream(stream, true, true))
                        {
                            image = new Bitmap(loaded);
                        }
                    }
                }
            }
            catch (Exception) //Never throw on a thread pool thread, it would terminate Notepad++
            {
                image = null;
            }
            DeliverImage(image, imageLoadEvent, generation);
        }

        /// <summary>
        /// Downloads an SVG image (only called if remote images are allowed)
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="imageLoadEvent"></param>
        protected void DownloadSvg(Uri uri, HtmlImageLoadEventArgs imageLoadEvent)
        {
            int generation = this.renderGeneration;
            WebClient webClient = new WebClient();
            webClient.DownloadDataCompleted += (downloadSender, downloadEvent) =>
            {
                //Dispose the client only once the download completed
                webClient.Dispose();
                Image image = null;
                if (downloadEvent.Error == null && !downloadEvent.Cancelled)
                {
                    ThreadPool.QueueUserWorkItem(state =>
                    {
                        try
                        {
                            using (MemoryStream stream = new MemoryStream(downloadEvent.Result))
                            {
                                image = ConvertSvgToBitmap(SvgDocument.Open<SvgDocument>(stream));
                            }
                        }
                        catch (Exception) //Never throw on a thread pool thread, it would terminate Notepad++
                        {
                            image = null;
                        }
                        DeliverImage(image, imageLoadEvent, generation);
                    });
                }
                else
                {
                    DeliverImage(null, imageLoadEvent, generation);
                }
            };
            try
            {
                webClient.DownloadDataAsync(uri);
            }
            catch (Exception exception) when (exception is WebException || exception is NotSupportedException || exception is ArgumentException)
            {
                webClient.Dispose();
                DeliverImage(null, imageLoadEvent, generation);
            }
        }

        /// <summary>
        /// Hands a loaded image (or the failure) to the HTML panel on the UI thread
        /// </summary>
        /// <param name="image">Null if loading failed</param>
        /// <param name="imageLoadEvent"></param>
        /// <param name="generation"></param>
        protected void DeliverImage(Image image, HtmlImageLoadEventArgs imageLoadEvent, int generation)
        {
            Action deliver = () =>
            {
                //Outdated (the document has been re-rendered meanwhile) or closed
                if (generation != this.renderGeneration || this.IsDisposed)
                {
                    image?.Dispose();
                    return;
                }
                if (image != null)
                {
                    this.renderedImages.Add(image);
                    imageLoadEvent.Callback(image);
                }
                else
                {
                    //Signal the failure, so HtmlRenderer can draw its error image
                    imageLoadEvent.Callback();
                }
            };

            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke(deliver);
                    return;
                }
            }
            catch (Exception) { } //Handle destroyed meanwhile; never throw on a thread pool thread
            image?.Dispose();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="svgDocument"></param>
        /// <returns>The rendered SVG</returns>
        protected Bitmap ConvertSvgToBitmap(SvgDocument svgDocument)
        {
            //Width/Height may be relative (e.g. 100%) -> use the computed dimensions (viewbox) instead
            SizeF dimensions = svgDocument.GetDimensions();
            int width = (int)Math.Ceiling(dimensions.Width), height = (int)Math.Ceiling(dimensions.Height);
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("SVG image has no size");
            }
            //Scale down huge images instead of allocating huge bitmaps
            double scale = Math.Min(1.0, (double)MaxSvgDimension / Math.Max(width, height));
            width = Math.Max(1, (int)(width * scale));
            height = Math.Max(1, (int)(height * scale));

            Bitmap svgImage = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                if (scale < 1.0)
                {
                    svgDocument.Width = new SvgUnit(SvgUnitType.Pixel, width);
                    svgDocument.Height = new SvgUnit(SvgUnitType.Pixel, height);
                }
                svgDocument.Draw(svgImage);
                return svgImage;
            }
            catch
            {
                svgImage.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Release the custom loader
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.markdownViewerHtmlPanel != null)
                {
                    this.markdownViewerHtmlPanel.ImageLoad -= OnImageLoad;
                    this.markdownViewerHtmlPanel.StylesheetLoad -= OnStylesheetLoad;
                }
                base.Dispose(disposing);
                //Dispose the images only after the panel (disposed by the base) stopped using them
                this.renderedImages.ForEach(image => image.Dispose());
                this.renderedImages.Clear();
                return;
            }
            base.Dispose(disposing);
        }
    }
}
