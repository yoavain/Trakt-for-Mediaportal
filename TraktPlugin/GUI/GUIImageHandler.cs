﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using MediaPortal.GUI.Library;
using MediaPortal.Util;

namespace TraktPlugin.GUI
{
    public enum OverlayImage
    {
        Seenit,
        Library,
        Watchlist,
        None
    }

    public static class GUIImageHandler
    {
        /// <summary>
        /// Download an image if it does not exist locally
        /// </summary>
        /// <param name="url">Online URL of image to download</param>
        /// <param name="localFile">Local filename to save image</param>
        /// <returns>true if image downloads successfully or loads from disk successfully</returns>
        public static bool DownloadImage(string url, string localFile)
        {
            WebClient webClient = new WebClient();
            webClient.Headers.Add("user-agent", TraktSettings.UserAgent);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFile));
                if (!File.Exists(localFile) || ImageFast.FromFile(localFile) == null)
                {
                    TraktLogger.Debug("Downloading new image from: {0}", url);
                    webClient.DownloadFile(url, localFile);
                }
                return true;
            }
            catch (Exception)
            {
                TraktLogger.Info("Image download failed from '{0}' to '{1}'", url, localFile);
                try { if (File.Exists(localFile)) File.Delete(localFile); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Gets a MediaPortal texture identifier from filename
        /// </summary>
        /// <param name="filename">Filename to generate texture</param>
        /// <returns>MediaPortal texture identifier</returns>
        public static string GetTextureIdentFromFile(string filename)
        {
            return GetTextureIdentFromFile(filename, string.Empty);
        }
        
        public static string GetTextureIdentFromFile(string filename, string suffix)
        {
            return "[Trakt:" + (filename + suffix).GetHashCode() + "]";
        }

        /// <summary>
        /// Draws a trakt overlay, library/seen/watchlist icon on a poster
        /// This is done in memory and wont touch the existing file
        /// </summary>
        /// <param name="origPoster">Filename of the untouched poster</param>
        /// <param name="type">Overlay type enum</param>
        /// <returns>An image with overlay added to poster</returns>
        public static Bitmap DrawOverlayOnPoster(string origPoster, OverlayImage type)
        {
            string overlayImage = GUIGraphicsContext.Skin + string.Format(@"\Media\trakt{0}.png", Enum.GetName(typeof(OverlayImage), type));
            Bitmap poster = new Bitmap(ImageFast.FromFile(origPoster));
            Graphics gph = Graphics.FromImage(poster);

            if (File.Exists(overlayImage))
            {
                Bitmap newPoster = new Bitmap(ImageFast.FromFile(overlayImage));
                
                // set position to be right aligned
                // poster is 300px wide, overlays are 55x55px
                // later allow skinner to define this by skin settings
                gph.DrawImage(newPoster, 245, 0);           
            }           
            gph.Dispose();
            return poster;
        }
    }
}