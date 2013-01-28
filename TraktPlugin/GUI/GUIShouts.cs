﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MediaPortal.Configuration;
using MediaPortal.GUI.Library;
using MediaPortal.Video.Database;
using MediaPortal.GUI.Video;
using Action = MediaPortal.GUI.Library.Action;
using MediaPortal.Util;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    public class GUIShouts : GUIWindow
    {
        #region Skin Controls

        [SkinControl(50)]
        protected GUIFacadeControl Facade = null;

        [SkinControl(2)]
        protected GUICheckButton hideSpoilersButton = null;

        [SkinControl(3)]
        protected GUIButtonControl nextEpisodeButton = null;

        [SkinControl(4)]
        protected GUIButtonControl prevEpisodeButton = null;

        #endregion

        #region Enums
         
        enum ContextMenuItem
        {
            Shout,
            Spoilers,
            NextEpisode,
            PrevEpisode
        }

        public enum ShoutTypeEnum
        {
            movie,
            show,
            episode
        }

        #endregion

        #region Constructor

        public GUIShouts() { }        

        #endregion

        #region Private Properties

        bool StopDownload { get; set; }
        bool ExitIfNoShoutsFound { get; set; }

        #endregion

        #region Public Properties

        public static ShoutTypeEnum ShoutType { get; set; }
        public static MovieShout MovieInfo { get; set; }
        public static ShowShout ShowInfo { get; set; }
        public static EpisodeShout EpisodeInfo { get; set; }
        public static string Fanart { get; set; }
        public static string OnlineFanart { get; set; }

        #endregion

        #region Base Overrides

        public override int GetID
        {
            get
            {
                return 87280;
            }
        }

        public override bool Init()
        {
            return Load(GUIGraphicsContext.Skin + @"\Trakt.Shouts.xml");
        }

        protected override void OnPageLoad()
        {
            base.OnPageLoad();

            // Clear GUI properties
            ClearProperties();

            // Requires Login
            if (!GUICommon.CheckLogin()) return;

            // Initialize
            InitProperties();
            
            // Enable/Disable GUI prev/next buttons
            EnableGUIButtons();

            // Load Shouts for Selected item
            LoadShoutsList();
        }

        protected override void OnPageDestroy(int new_windowId)
        {
            StopDownload = true;
            ClearProperties();

            if (hideSpoilersButton != null)
            {
                TraktSettings.HideSpoilersOnShouts = hideSpoilersButton.Selected;
            }

            base.OnPageDestroy(new_windowId);
        }

        protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
        {
            // wait for any background action to finish
            if (GUIBackgroundTask.Instance.IsBusy) return;

            switch (controlId)
            {
                // Hide Spoilers Button
                case (2):
                    TraktSettings.HideSpoilersOnShouts = !TraktSettings.HideSpoilersOnShouts;
                    PublishShoutSkinProperties(Facade.SelectedListItem.TVTag as TraktShout);
                    break;

                // Next Episode
                case (3):
                    GetNextEpisodeShouts();
                    break;

                // Previous Episode
                case (4):
                    GetPrevEpisodeShouts();
                    break;

                default:
                    break;
            }
            base.OnClicked(controlId, control, actionType);
        }

        protected override void OnShowContextMenu()
        {
            if (GUIBackgroundTask.Instance.IsBusy) return;

            GUIListItem selectedItem = this.Facade.SelectedListItem;
            if (selectedItem == null) return;

            TraktShout selectedShout = (TraktShout)selectedItem.TVTag;

            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            if (dlg == null) return;

            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem listItem = null;

            if (ShoutType == ShoutTypeEnum.episode)
            {
                listItem = new GUIListItem(Translation.NextEpisode);
                dlg.Add(listItem);
                listItem.ItemId = (int)ContextMenuItem.NextEpisode;

                if (int.Parse(EpisodeInfo.EpisodeIdx) > 1)
                {
                    listItem = new GUIListItem(Translation.PreviousEpisode);
                    dlg.Add(listItem);
                    listItem.ItemId = (int)ContextMenuItem.PrevEpisode;
                }
            }

            listItem = new GUIListItem(TraktSettings.HideSpoilersOnShouts ? Translation.ShowSpoilers : Translation.HideSpoilers);
            dlg.Add(listItem);
            listItem.ItemId = (int)ContextMenuItem.Spoilers;
  
            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)ContextMenuItem.Spoilers):
                    TraktSettings.HideSpoilersOnShouts = !TraktSettings.HideSpoilersOnShouts;
                    if (hideSpoilersButton != null) hideSpoilersButton.Selected = TraktSettings.HideSpoilersOnShouts;
                    PublishShoutSkinProperties(selectedShout);
                    break;

                case ((int)ContextMenuItem.NextEpisode):
                    GetNextEpisodeShouts();
                    break;

                case ((int)ContextMenuItem.PrevEpisode):
                    GetPrevEpisodeShouts();
                    break;

                default:
                    break;
            }

            base.OnShowContextMenu();
        }

        #endregion

        #region Private Methods

        private void GetNextEpisodeShouts()
        {
            if (ShoutType != ShoutTypeEnum.episode) return;

            var episodeIndex = int.Parse(EpisodeInfo.EpisodeIdx);
            var seasonIndex = int.Parse(EpisodeInfo.SeasonIdx);

            // increment by 1 episode
            EpisodeInfo.EpisodeIdx = (episodeIndex + 1).ToString();

            // flag to indicate we dont want to exit if no shouts found
            ExitIfNoShoutsFound = false;

            LoadShoutsList();

            // set focus back to facade
            GUIControl.FocusControl(GetID, Facade.GetID);
        }

        private void GetPrevEpisodeShouts()
        {
            if (ShoutType != ShoutTypeEnum.episode) return;

            var episodeIndex = int.Parse(EpisodeInfo.EpisodeIdx);
            var seasonIndex = int.Parse(EpisodeInfo.SeasonIdx);

            // there is no episode 0
            if (episodeIndex == 1) return;

            // decrement by 1 episode
            EpisodeInfo.EpisodeIdx = (episodeIndex - 1).ToString();

            // flag to indicate we dont want to exit if no shouts found
            ExitIfNoShoutsFound = false;

            LoadShoutsList();

            // set focus back to facade
            GUIControl.FocusControl(GetID, Facade.GetID);
        }

        private void EnableGUIButtons()
        {
            if (nextEpisodeButton == null || prevEpisodeButton == null) return;

            // only enable episode buttons for episode shouts
            if (ShoutType != ShoutTypeEnum.episode)
            {
                GUIControl.DisableControl(GetID, nextEpisodeButton.GetID);
                GUIControl.DisableControl(GetID, prevEpisodeButton.GetID);
                return;
            }

            // we could get the max episode number and disable next button
            // on last episode, for now, lets not do the extra request and 
            // rely on notify to popup indicating no more shouts.
            GUIControl.EnableControl(GetID, nextEpisodeButton.GetID);
            GUIControl.EnableControl(GetID, prevEpisodeButton.GetID);

            // if episode one, then disable prev button
            if (int.Parse(EpisodeInfo.EpisodeIdx) <= 1)
            {
                GUIControl.DisableControl(GetID, prevEpisodeButton.GetID);
            }
        }

        private void LoadShoutsList()
        {
            GUIUtils.SetProperty("#Trakt.Items", string.Empty);

            GUIBackgroundTask.Instance.ExecuteInBackgroundAndCallback(() =>
            {
                switch (ShoutType)
                {
                    case ShoutTypeEnum.movie:
                        if (MovieInfo == null) return null;
                        GUIUtils.SetProperty("#Trakt.Shout.CurrentItem", MovieInfo.Title);
                        return GetMovieShouts();

                    case ShoutTypeEnum.show:
                        if (ShowInfo == null) return null;
                        GUIUtils.SetProperty("#Trakt.Shout.CurrentItem", ShowInfo.Title);
                        return GetShowShouts();

                    case ShoutTypeEnum.episode:
                        if (EpisodeInfo == null) return null;
                        GUIUtils.SetProperty("#Trakt.Shout.CurrentItem", EpisodeInfo.ToString());
                        return GetEpisodeShouts();

                    default:
                        return null;
                }
            },
            delegate(bool success, object result)
            {
                if (success)
                {
                    SendShoutsToFacade(result as IEnumerable<TraktShout>);
                }
            }, Translation.GettingShouts, true);
        }

        private IEnumerable<TraktShout> GetMovieShouts()
        {
            string title = string.Empty;
            if (!string.IsNullOrEmpty(MovieInfo.IMDbId))
                title = MovieInfo.IMDbId;
            else if(!string.IsNullOrEmpty(MovieInfo.TMDbId))
                title = MovieInfo.TMDbId;
            else
                title = string.Format("{0}-{1}", MovieInfo.Title, MovieInfo.Year).Replace(" ", "-");

            return TraktAPI.TraktAPI.GetMovieShouts(title);
        }

        private IEnumerable<TraktShout> GetShowShouts()
        {
            string title = string.Empty;
            if (!string.IsNullOrEmpty(ShowInfo.TVDbId))
                title = ShowInfo.TVDbId;
            else if (!string.IsNullOrEmpty(ShowInfo.IMDbId))
                title = ShowInfo.IMDbId;
            else
                title = ShowInfo.Title.Replace(" ", "-");

            return TraktAPI.TraktAPI.GetShowShouts(title);
        }

        private IEnumerable<TraktShout> GetEpisodeShouts()
        {
            string title = string.Empty;
            if (!string.IsNullOrEmpty(EpisodeInfo.TVDbId))
                title = EpisodeInfo.TVDbId;
            else if (!string.IsNullOrEmpty(EpisodeInfo.IMDbId))
                title = EpisodeInfo.IMDbId;
            else
                title = EpisodeInfo.Title.Replace(" ", "-");

            return TraktAPI.TraktAPI.GetEpisodeShouts(title, EpisodeInfo.SeasonIdx, EpisodeInfo.EpisodeIdx);
        }

        private void SendShoutsToFacade(IEnumerable<TraktShout> shouts)
        {
            // clear facade
            GUIControl.ClearControl(GetID, Facade.GetID);

            if (shouts == null || shouts.Count() == 0)
            {
                if (shouts != null)
                {
                    string title = string.Empty;
                    switch (ShoutType)
                    {
                        case ShoutTypeEnum.movie:
                            title = MovieInfo.Title;
                            break;
                        case ShoutTypeEnum.show:
                            title = ShowInfo.Title;
                            break;
                        case ShoutTypeEnum.episode:
                            title = string.Format(EpisodeInfo.ToString());
                            break;
                    }
                    ClearProperties();
                    GUIUtils.ShowNotifyDialog(GUIUtils.PluginName(), string.Format(Translation.NoShoutsForItem, title));
                }
                if (ExitIfNoShoutsFound)
                {
                    GUIWindowManager.ShowPreviousWindow();
                    return;
                }
            }

            GUIUtils.SetProperty("#itemcount", shouts.Count().ToString());
            GUIUtils.SetProperty("#Trakt.Items", string.Format("{0} {1}", shouts.Count(), shouts.Count() > 1 ? Translation.Shouts : Translation.Shout));            

            int id = 0;
            List<TraktUser> users = new List<TraktUser>();

            // Add each user that shouted to the list
            foreach (var shout in shouts)
            {
                GUITraktShoutListItem shoutItem = new GUITraktShoutListItem(shout.User.Username);

                shoutItem.Label2 = shout.InsertedDate.FromEpoch().ToShortDateString();
                shoutItem.Item = shout.User;
                shoutItem.TVTag = shout;
                shoutItem.ItemId = id++;
                shoutItem.IconImage = "defaultTraktUser.png";
                shoutItem.IconImageBig = "defaultTraktUserBig.png";
                shoutItem.ThumbnailImage = "defaultTraktUserBig.png";
                shoutItem.OnItemSelected += OnShoutSelected;
                Utils.SetDefaultIcons(shoutItem);
                Facade.Add(shoutItem);

                users.Add(shout.User);
            }

            // Enable / Disable GUI Controls
            EnableGUIButtons();

            // Set Facade Layout
            if (Facade.Count > 0)
            {
                Facade.SetCurrentLayout("List");
                GUIControl.FocusControl(GetID, Facade.GetID);

                Facade.SelectedListItemIndex = 0;
            }
            else
            {
                GUIControl.FocusControl(GetID, nextEpisodeButton.GetID);
            }

            // Download avatars Async and set to facade            
            GetImages(users);
        }

        private void SetProperty(string property, string value)
        {
            string propertyValue = string.IsNullOrEmpty(value) ? "N/A" : value;
            GUIUtils.SetProperty(property, propertyValue);
        }

        private void InitProperties()
        {
            ExitIfNoShoutsFound = true;

            // only set property if file exists
            // if we set now and download later, image will not set to skin
            if (File.Exists(Fanart))
                GUIUtils.SetProperty("#Trakt.Shout.Fanart", Fanart);

            if (hideSpoilersButton != null)
            {
                hideSpoilersButton.Label = Translation.HideSpoilers;
                hideSpoilersButton.Selected = TraktSettings.HideSpoilersOnShouts;
            }
        }

        private void ClearProperties()
        {
            GUIUtils.SetProperty("#Trakt.Shouts.CurrentItem", string.Empty);
            GUIUtils.SetProperty("#Trakt.Shout.Inserted", string.Empty);
            GUIUtils.SetProperty("#Trakt.Shout.Spoiler", "false");
            GUIUtils.SetProperty("#Trakt.Shout.Text", string.Empty);

            GUICommon.ClearUserProperties();
        }

        private void PublishShoutSkinProperties(TraktShout shout)
        {
            if (shout == null) return;

            GUICommon.SetUserProperties(shout.User);

            SetProperty("#Trakt.Shout.Inserted", shout.InsertedDate.FromEpoch().ToLongDateString());
            SetProperty("#Trakt.Shout.Spoiler", shout.Spoiler.ToString());
            if (TraktSettings.HideSpoilersOnShouts && shout.Spoiler)
            {
                SetProperty("#Trakt.Shout.Text", Translation.HiddenToPreventSpoilers);
            }
            else
            {
                SetProperty("#Trakt.Shout.Text", System.Web.HttpUtility.HtmlDecode(shout.Shout.RemapHighOrderChars()).StripHTML());
            }
        }

        private void OnShoutSelected(GUIListItem item, GUIControl parent)
        {
            PublishShoutSkinProperties(item.TVTag as TraktShout);
        }

        private void GetImages(List<TraktUser> itemsWithThumbs)
        {
            StopDownload = false;

            new Thread((o) =>
                {
                    // download fanart if we need to
                    if (!File.Exists(Fanart) && !string.IsNullOrEmpty(OnlineFanart) && TraktSettings.DownloadFanart)
                    {
                        if (GUIImageHandler.DownloadImage(OnlineFanart, Fanart))
                        {
                            // notify that image has been downloaded
                            GUIUtils.SetProperty("#Trakt.Shout.Fanart", Fanart);
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "ImageDownloader"
                }.Start();
            
            // split the downloads in 5+ groups and do multithreaded downloading
            int groupSize = (int)Math.Max(1, Math.Floor((double)itemsWithThumbs.Count / 5));
            int groups = (int)Math.Ceiling((double)itemsWithThumbs.Count() / groupSize);

            for (int i = 0; i < groups; i++)
            {
                List<TraktUser> groupList = new List<TraktUser>();
                for (int j = groupSize * i; j < groupSize * i + (groupSize * (i + 1) > itemsWithThumbs.Count ? itemsWithThumbs.Count - groupSize * i : groupSize); j++)
                {
                    groupList.Add(itemsWithThumbs[j]);
                }

                new Thread(delegate(object o)
                {
                    List<TraktUser> items = (List<TraktUser>)o;
                    foreach (var item in items)
                    {
                        // stop download if we have exited window
                        if (StopDownload) break;

                        string remoteThumb = item.Avatar;
                        string localThumb = item.AvatarFilename;

                        if (!string.IsNullOrEmpty(remoteThumb) && !string.IsNullOrEmpty(localThumb))
                        {
                            if (GUIImageHandler.DownloadImage(remoteThumb, localThumb))
                            {
                                // notify that image has been downloaded
                                item.NotifyPropertyChanged("AvatarFilename");
                            }
                        }                        
                    }
                })
                {
                    IsBackground = true,
                    Name = "ImageDownloader" + i.ToString()
                }.Start(groupList);
            }
        }

        #endregion

    }

    public class GUITraktShoutListItem : GUIListItem
    {
        public GUITraktShoutListItem(string strLabel) : base(strLabel) { }

        public object Item
        {
            get { return _Item; }
            set
            {
                _Item = value;
                INotifyPropertyChanged notifier = value as INotifyPropertyChanged;
                if (notifier != null) notifier.PropertyChanged += (s, e) =>
                {
                    if (s is TraktUser && e.PropertyName == "AvatarFilename")
                        SetImageToGui((s as TraktUser).AvatarFilename);
                };
            }
        } protected object _Item;

        /// <summary>
        /// Loads an Image from memory into a facade item
        /// </summary>
        /// <param name="imageFilePath">Filename of image</param>
        protected void SetImageToGui(string imageFilePath)
        {
            if (string.IsNullOrEmpty(imageFilePath)) return;

            ThumbnailImage = imageFilePath;
            IconImage = imageFilePath;
            IconImageBig = imageFilePath;
         
            // if selected and is current window force an update of thumbnail
            this.UpdateItemIfSelected((int)TraktGUIWindows.Shouts, ItemId);
        }
    }

    public class MovieShout
    {
        public string Title { get; set; }
        public string Year { get; set; }
        public string IMDbId { get; set; }
        public string TMDbId { get; set; }
    }

    public class ShowShout
    {
        public string Title { get; set; }
        public string IMDbId { get; set; }
        public string TVDbId { get; set; }
    }

    public class EpisodeShout
    {
        public string Title { get; set; }
        public string IMDbId { get; set; }
        public string TVDbId { get; set; }
        public string SeasonIdx { get; set; }
        public string EpisodeIdx { get; set; }

        public override string ToString()
        {
            return string.Format("{0} - {1}x{2}", Title, SeasonIdx, EpisodeIdx);
        }
    }
}