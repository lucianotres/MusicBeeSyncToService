using MusicBeePlugin;
using MusicBeePlugin.Models;
using MusicBeePlugin.Services;
using MusicBeePlugin.WPF;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static MusicBeePlugin.Plugin;

namespace MBSyncToServiceUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool IncludeFolders { get { return IncludeFoldersCheckBox.IsChecked.HasValue && IncludeFoldersCheckBox.IsChecked.Value; } } 
        private bool IncludeZ { get { return IncludeZCheckBox.IsChecked.HasValue && IncludeZCheckBox.IsChecked.Value; } }
        private bool SyncToService { get { return SyncToServiceRadioButton.IsChecked.HasValue && SyncToServiceRadioButton.IsChecked.Value; } }
        private MusicBeeSyncHelper MusicBee;
        private SpotifySyncHelper Spotify;

        public ObservableCollection<CheckedListItem<MusicBeePlaylist>> MusicBeePlaylists { get; set; }
        public ObservableCollection<CheckedListItem<SpotifyPlaylist>> SpotifyPlaylists { get; set; }

        public MainWindow(Plugin.MusicBeeApiInterface apiInterface)
        {
            InitializeComponent();

            int backColor = apiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputPanel, ElementState.ElementStateDefault, ElementComponent.ComponentBackground);
            int foreColor = apiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputPanel, ElementState.ElementStateDefault, ElementComponent.ComponentForeground);
            int backCtrlColor = apiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl , ElementState.ElementStateDefault, ElementComponent.ComponentBackground);
            int foreCtrlColor = apiInterface.Setting_GetSkinElementColour(SkinElement.SkinInputControl, ElementState.ElementStateDefault, ElementComponent.ComponentForeground);

            Resources["backColor"] = new SolidColorBrush(IntToColor(backColor));
            Resources["foreColor"] = new SolidColorBrush(IntToColor(foreColor));
            Resources["backCtrlColor"] = new SolidColorBrush(IntToColor(backCtrlColor));
            Resources["foreCtrlColor"] = new SolidColorBrush(IntToColor(foreCtrlColor));

            MusicBeePlaylists = new ObservableCollection<CheckedListItem<MusicBeePlaylist>>();
            SpotifyPlaylists = new ObservableCollection<CheckedListItem<SpotifyPlaylist>>();

            MusicBee = new MusicBeeSyncHelper(apiInterface);
            RefreshMusicBeePlaylists();

            Action<string> log = (s) => Dispatcher.Invoke(() => { Log(s); });
            Spotify = new SpotifySyncHelper(log);
        }

        private static Color IntToColor(int color)
        {
            var drawingColor = System.Drawing.Color.FromArgb(color);
            return Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
        }


        #region MusicBee

        private List<MusicBeePlaylist> GetMusicBeePlaylistsToSync()
        {
            List<MusicBeePlaylist> results = new List<MusicBeePlaylist>();
            foreach (var listItem in MusicBeePlaylists)
            {
                if (listItem.IsChecked)
                {
                    results.Add(listItem.Item);
                }
            }

            return results;
        }

        private void RefreshMusicBeePlaylists()
        {
            MusicBeePlaylists.Clear();
            MusicBee.RefreshMusicBeePlaylists();
            MusicBee.Playlists.ForEach(x => MusicBeePlaylists.Add(new CheckedListItem<MusicBeePlaylist>(x)));
            MusicBeeListBox.ItemsSource = MusicBeePlaylists;
        }

        #endregion MusicBee

        #region Spotify

        private async void SpotifyLoginButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            Log("Opening browser to log in to Spotify...");
            SpotifyLoginButton.IsEnabled = false;

            bool success = false;
            try
            {
                success = await Spotify.LoginAsync();
            }
            catch (Exception ex)
            {
                Log($"Error when trying to login to Spotify: {ex.Message}");
            }

            if (success)
            {
                Log("Logged into Spotify successfully.");
                Log("Fetching Spotify Playlists...");
                SpotifySelectAllButton.IsEnabled = true;
                SpotifySyncButton.IsEnabled = true;
                await RefreshSpotifyPlaylists();
            }
            else
            {
                Log("Error when trying to login to Spotify.");
                SpotifyLoginButton.IsEnabled = true;
            }
        }

        private async void SpotifySyncButton_Click(object sender, RoutedEventArgs e)
        {
            List<IPlaylistSyncError> errors = new List<IPlaylistSyncError>();
            string direction = (SyncToService ? "to" : "from");
            Log($"Starting sync {direction} Spotify...");
            SpotifySelectAllButton.IsEnabled = false;
            SpotifySyncButton.IsEnabled = false;

            try
            {
                if (SyncToService)
                {
                    List<MusicBeePlaylist> mbPlaylistsToSync = GetMusicBeePlaylistsToSync();

                    SyncToSpotifySettings settings = new SyncToSpotifySettings()
                    {
                        IncludeFoldersInPlaylistName = IncludeFolders,
                        IncludeZAtStartOfDatePlaylistName = IncludeZ,
                    };

                    errors = await Spotify.SyncToSpotify(MusicBee, mbPlaylistsToSync, settings);
                    await RefreshSpotifyPlaylists();
                }
                else
                {
                    List<SimplePlaylist> spotifyPlaylistsToSync = GetSpotifyPlaylistsToSync();
                    errors = await Spotify.SyncToMusicBee(MusicBee, spotifyPlaylistsToSync);
                    RefreshMusicBeePlaylists();
                }

                if (errors.Count > 0)
                {
                    foreach (IPlaylistSyncError error in errors)
                    {
                        Log(error.GetMessage());
                    }
                    Log("See errors above");
                }
                else
                {
                    Log($"Successfully synced playlists {direction} Spotify");
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }


            SpotifySelectAllButton.IsEnabled = true;
            SpotifySyncButton.IsEnabled = true;
            Log($"Finished sync {direction} Spotify.");
        }
        private List<SimplePlaylist> GetSpotifyPlaylistsToSync()
        {
            List<SimplePlaylist> results = new List<SimplePlaylist>();
            foreach (var listItem in SpotifyPlaylists)
            {
                if (listItem.IsChecked)
                {
                    results.Add(listItem.Item.Playlist);
                }
            }
            return results;
        }

        private async Task RefreshSpotifyPlaylists()
        {
            List<SimplePlaylist> spotifyPlaylists = await Spotify.RefreshPlaylists();
            spotifyPlaylists = spotifyPlaylists.OrderBy(p => p.Name).ToList();
            SpotifyPlaylists.Clear();
            spotifyPlaylists.ForEach(x => SpotifyPlaylists.Add(new CheckedListItem<SpotifyPlaylist>(new SpotifyPlaylist(x))));
            SpotifyPlaylistListBox.ItemsSource = SpotifyPlaylists;
        }

        private void SpotifySelectAllButton_Checked(object sender, RoutedEventArgs e)
        {
            ChangeStateOfAllCheckBoxes(SpotifyPlaylists, true);
        }

        private void SpotifySelectAllButton_Unchecked(object sender, RoutedEventArgs e)
        {

            ChangeStateOfAllCheckBoxes(SpotifyPlaylists, false);
        }

        #endregion Spotify

        #region Helpers

        public void Log(string line)
        {
            OutputTextBox.Text += $"{line}\n";
            OutputTextBox.ScrollToEnd();
        }

        private void ChangeStateOfAllCheckBoxes<T>(ObservableCollection<CheckedListItem<T>> list, bool isChecked)
        {
            foreach (var item in list)
            {
                item.IsChecked = isChecked;
            }
        }
        #endregion

    }
}
