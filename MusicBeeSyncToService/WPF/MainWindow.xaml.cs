using MusicBeePlugin;
using MusicBeePlugin.Models;
using MusicBeePlugin.Services;
using MusicBeePlugin.WPF;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
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

        public ObservableCollection<MusicBeeSongSearch> LibrarySearchResults { get; set; }
        public MusicBeeSongSearch LibrarySearchResultsSelected { get; set; }

        public PopulatedSong PopulatedSongFromSpotifySelected { get; set; }

        public MainWindow(Plugin.MusicBeeApiInterface apiInterface)
        {
            InitializeComponent();

            ElementHost.EnableModelessKeyboardInterop(this);

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
            LibrarySearchResults = new ObservableCollection<MusicBeeSongSearch>();

            FindResultsFromLibrary.ItemsSource = LibrarySearchResults;
            FindResultsFromLibrary.SetBinding(ListBox.SelectedItemProperty, new Binding("LibrarySearchResultsSelected") { Source = this, Mode = BindingMode.TwoWay });

            MusicBee = new MusicBeeSyncHelper(apiInterface);
            RefreshMusicBeePlaylists();

            Action<string> log = (s) => Dispatcher.Invoke(() => { Log(s); });
            Spotify = new SpotifySyncHelper(log, MusicBee);

            ClearPanelsVisibility();
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

        private void ButtonBackToPlaylistSelect_Click(object sender, RoutedEventArgs e)
        {
            ClearPanelsVisibility();
        }
        private void ButtonContinueToSinc_Click(object sender, RoutedEventArgs e)
        {
            _ = SpotifyToMbSavePopulatedList();
        }

        private void ClearPanelsVisibility()
        {
            FirstSectionPanel.Visibility = Visibility.Visible;
            SecondSectionPanelToMB.Visibility = Visibility.Hidden;
            SecondSectionPanelToSpotify.Visibility = Visibility.Hidden;

            MBSongConferenceListBox.ItemsSource = null;
        }

        private async void SpotifySyncButton_Click(object sender, RoutedEventArgs e)
        {
            string direction = (SyncToService ? "to" : "from");
            Log($"Starting sync {direction} Spotify...");
            SpotifySelectAllButton.IsEnabled = false;
            SpotifySyncButton.IsEnabled = false;

            int toSyncCount = (SyncToService ? GetMusicBeePlaylistsToSync().Count : GetSpotifyPlaylistsToSync().Count);

            if (toSyncCount == 1)
            {
                FirstSectionPanel.Visibility = Visibility.Hidden;
                if (SyncToService)
                {
                    SecondSectionPanelToSpotify.Visibility = Visibility.Visible;
                }
                else
                {
                    SecondSectionPanelToMB.Visibility = Visibility.Visible;
                    await SpotifyToMbPopulateSongsOfSelectedPlaylist();
                    Log($"Finished to populate the list.");
                }
            }
            else if(toSyncCount > 1)
            {
                if (MessageBox.Show($"Are you really sure that you want to sync {toSyncCount} playlists automatic?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    await SpotifySyncMultiplePlaylists();
                    Log($"Finished sync {direction} Spotify.");
                }
            }
            else
            {
                Log("Nothing done, select one playlist or more.");
            }

            SpotifySelectAllButton.IsEnabled = true;
            SpotifySyncButton.IsEnabled = true;            
        }

        private async Task SpotifyToMbPopulateSongsOfSelectedPlaylist()
        {
            MBSongConferenceListBox.ItemsSource = null;
            try
            {
                var spotifyPlaylistsToSync = GetSpotifyPlaylistsToSync().First();

                Spotify.PopulateSpotifyPlaylist = spotifyPlaylistsToSync;
                await Spotify.PopulateMbPlaylistSongsFromSpotifyPlaylist();

                MBSongConferenceListBox.ItemsSource = Spotify.PopulatedSongs;
                MBSongConferenceListBox.SetBinding(ListBox.SelectedItemProperty, new Binding("PopulatedSongFromSpotifySelected") { Source = this, Mode = BindingMode.TwoWay });

                if (Spotify.PopulateErrors.Count > 0)
                {
                    var errors = Spotify
                        .PopulateErrors
                        .Select(s => s.GetMessage())
                        .Aggregate(new StringBuilder(), (sb, s) =>
                        {
                            sb.AppendLine(s);
                            return sb;
                        });

                    Log(errors.ToString());
                    Log("See errors above");
                }
                else
                {
                    Log($"Successfully populated songs from Spotify");
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        private async Task SpotifyToMbSavePopulatedList()
        {
            try
            {
                if (Spotify.PopulatedSongs.Where(w => w.Checked).Count() == 0)
                {
                    MessageBox.Show("No songs selected to save", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await Spotify.SaveMbPlaylistPopulatedFromSpotifyPlaylist();
                Log($"Successfully saved playlist from Spotify");
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        private async Task SpotifySyncMultiplePlaylists()
        {
            var direction = (SyncToService ? "to" : "from");
            var errors = new List<IPlaylistSyncError>();
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

                    await Spotify.SyncToSpotify(mbPlaylistsToSync, settings);
                    errors = Spotify.PopulateErrors;
                    await RefreshSpotifyPlaylists();
                }
                else
                {
                    List<SimplePlaylist> spotifyPlaylistsToSync = GetSpotifyPlaylistsToSync();
                    await Spotify.SyncToMusicBee(spotifyPlaylistsToSync);
                    errors = Spotify.PopulateErrors;
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


        private void ChangePopuplatedSongFromSpotifyLibraryRelated(MusicBeeSongSearch changeTo)
        {
            if (PopulatedSongFromSpotifySelected != null)
            {
                PopulatedSongFromSpotifySelected.MbSong = changeTo.Song;
                PopulatedSongFromSpotifySelected.Checked = true;

                MBSongConferenceListBox.Items.Refresh();
            }    
        }

        private void SearchOnLibrary(string text)
        {
            LibrarySearchResults.Clear();

            try
            {
                text = text.Trim();
                if (text.Length <= 2)
                    return;

                var words  = text
                    .Split(' ')
                    .Select(s=> s.ToLower())
                    .ToArray();

                var results = MusicBee.FindSongsByWords(words);

                foreach (var result in results)
                    LibrarySearchResults.Add(result);
            }
            catch 
            {
                Log("Something goes wrong at search.");
            }
            finally
            {
                FindResultsFromLibrary.Visibility = LibrarySearchResults.Any() ? Visibility.Visible : Visibility.Hidden;
            }
        }
        private void ClearFindResultsFromLibrary()
        {
            FindResultsFromLibrary.Visibility = Visibility.Hidden;
            LibrarySearchResultsSelected = null;
            LibrarySearchResults.Clear();
        }

        private DispatcherTimer searchLibraryDelayTimer = null;

        private void LibrarySearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (searchLibraryDelayTimer == null)
            {
                searchLibraryDelayTimer = new DispatcherTimer();
                searchLibraryDelayTimer.Interval = TimeSpan.FromMilliseconds(300);
                searchLibraryDelayTimer.Tick += new EventHandler((s, ee) =>
                {
                    searchLibraryDelayTimer.Stop();
                    SearchOnLibrary(LibrarySearchBox.Text);
                });
            }

            searchLibraryDelayTimer.Stop();
            searchLibraryDelayTimer.Start();
        }

        private void LibrarySearchBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;

                if (LibrarySearchResults.Any())
                {
                    var changeTo = LibrarySearchResults.First();
                    ChangePopuplatedSongFromSpotifyLibraryRelated(changeTo);
                }

                ClearFindResultsFromLibrary();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                ClearFindResultsFromLibrary();
                LibrarySearchBox.Text = "";
            }
            else if (e.Key == System.Windows.Input.Key.Down)
            {
                e.Handled = true;
                if (FindResultsFromLibrary.IsVisible)
                {
                    if (LibrarySearchResults.Count > 1)
                    {
                        FindResultsFromLibrary.SelectedIndex = 1;
                        FindResultsFromLibrary.UpdateLayout();
                        FindResultsFromLibrary.Focus();
                    }
                    else
                        FindResultsFromLibrary.Focus();
                }
            }
        }

        private void FindResultsFromLibrary_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                ClearFindResultsFromLibrary();
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                
                if (LibrarySearchResultsSelected != null)
                    ChangePopuplatedSongFromSpotifyLibraryRelated(LibrarySearchResultsSelected);

                ClearFindResultsFromLibrary();
            }
        }
    }
}
