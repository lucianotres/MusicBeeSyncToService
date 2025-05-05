using MusicBeePlugin;
using MusicBeePlugin.Models;
using MusicBeePlugin.Services;
using MusicBeePlugin.WPF;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
using static MusicBeePlugin.Plugin;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

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

        public ObservableCollection<FullTrack> SpotifySongSearchResults { get; set; }
        public FullTrack SpotifySongSearchResultsSelected { get; set; }

        public PopulatedSong PopulatedSongSelected { get; set; }

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
            SpotifySongSearchResults = new ObservableCollection<FullTrack>();

            MusicBee = new MusicBeeSyncHelper(apiInterface);
            RefreshMusicBeePlaylists();

            Action<string> log = (s) => Dispatcher.Invoke(() => { Log(s); });
            Spotify = new SpotifySyncHelper(log, MusicBee);

            SongConferenceListBox.ItemsSource = Spotify.PopulatedSongs;
            SongConferenceListBox.SetBinding(ListBox.SelectedItemProperty, new Binding("PopulatedSongSelected") { Source = this, Mode = BindingMode.TwoWay });

            FindResultsFromLibrary.ItemsSource = LibrarySearchResults;
            FindResultsFromLibrary.SetBinding(ListBox.SelectedItemProperty, new Binding("LibrarySearchResultsSelected") { Source = this, Mode = BindingMode.TwoWay });

            FindResultsFromSpotify.ItemsSource = SpotifySongSearchResults;
            FindResultsFromSpotify.SetBinding(ListBox.SelectedItemProperty, new Binding("SpotifySongSearchResultsSelected") { Source = this, Mode = BindingMode.TwoWay });
            
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
        private async void ButtonContinueToSinc_Click(object sender, RoutedEventArgs e)
        {
            if (SyncToService)
            {
                await MbToSpotifySavePopulatedList();
            }
            else
            {
                await SpotifyToMbSavePopulatedList();
            }

            ClearPanelsVisibility();
        }

        private void ClearPanelsVisibility()
        {
            FirstSectionPanel.Visibility = Visibility.Visible;
            SecondSectionPanel.Visibility = Visibility.Hidden;

            SongConferenceListBox.Items.Refresh();
        }

        private async void SpotifySyncButton_Click(object sender, RoutedEventArgs e)
        {
            string direction = (SyncToService ? "to" : "from");
            Log($"Starting sync {direction} Spotify...");
            SpotifySelectAllButton.IsEnabled = false;
            SpotifySyncButton.IsEnabled = false;
            BtnBackSelection.IsEnabled = false;
            BtnContinueSync.IsEnabled = false;
            SongSearchBox.IsEnabled = false;

            try
            {
                int toSyncCount = (SyncToService ? GetMusicBeePlaylistsToSync().Count : GetSpotifyPlaylistsToSync().Count);

                if (toSyncCount == 1)
                {
                    FirstSectionPanel.Visibility = Visibility.Hidden;
                    SecondSectionPanel.Visibility = Visibility.Visible;
                    ServiceTypeLabel.Content = SyncToService ? "Sync MB playlist to Spotify" : "Sync Spotify playlist to MB";
                    SpotifySearchHint.Visibility = SyncToService ? Visibility.Visible : Visibility.Hidden;
                    SongConferenceListBox.Items.Refresh();

                    if (SyncToService)
                    {
                        await MbToSpotifyPopulateSongsOfSelectedPlaylist();
                    }
                    else
                    {
                        await SpotifyToMbPopulateSongsOfSelectedPlaylist();
                    }
                }
                else if (toSyncCount > 1)
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

            }
            finally
            {
                SpotifySelectAllButton.IsEnabled = true;
                SpotifySyncButton.IsEnabled = true;
                BtnBackSelection.IsEnabled = true;
                BtnContinueSync.IsEnabled = true;
                SongSearchBox.IsEnabled = true;
            }
        }

        private async Task SpotifyToMbPopulateSongsOfSelectedPlaylist()
        {
            try
            {
                var spotifyPlaylistsToSync = GetSpotifyPlaylistsToSync().First();

                Spotify.PopulateSpotifyPlaylist = spotifyPlaylistsToSync;
                await Spotify.PopulateMbPlaylistSongsFromSpotifyPlaylist();

                SongConferenceListBox.Items.Refresh();

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
                    Log("Populated songs from Spotify with errors above");
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

        private async Task MbToSpotifySavePopulatedList()
        {
            try
            {
                if (Spotify.PopulatedSongs.Where(w => w.Checked).Count() == 0)
                {
                    MessageBox.Show("No songs selected to save", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SyncToSpotifySettings settings = new SyncToSpotifySettings()
                {
                    IncludeFoldersInPlaylistName = IncludeFolders,
                    IncludeZAtStartOfDatePlaylistName = IncludeZ,
                };

                await Spotify.SaveSpotifyPlaylistPopulatedFromMbPlaylist(settings);
                Log($"Successfully saved playlist to Spotify");
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        private async Task MbToSpotifyPopulateSongsOfSelectedPlaylist()
        {
            try
            {
                var MbPlaylistsToSync = GetMusicBeePlaylistsToSync().First();

                Spotify.PopulateMusicBeePlaylist = MbPlaylistsToSync;
                await Spotify.PopulateSpotifyPlaylistSongsFromMbPlaylist();

                SongConferenceListBox.Items.Refresh();

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
                    Log("Populated songs from MusicBee playlist with errors above");
                }
                else
                {
                    Log($"Successfully populated songs from MusicBee playlist");
                }
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


        private void ChangePopuplatedSongWithMBLibraryRelated(MusicBeeSongSearch changeTo)
        {
            if (PopulatedSongSelected != null)
            {
                PopulatedSongSelected.MbSong = changeTo.Song;
                PopulatedSongSelected.Checked = true;

                SongConferenceListBox.Items.Refresh();
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

                LibrarySearchResultsSelected = LibrarySearchResults.FirstOrDefault();
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
        private void ClearSearchResults()
        {
            if (SyncToService)
            {
                FindResultsFromSpotify.Visibility = Visibility.Hidden;
                SpotifySongSearchResultsSelected = null;
                SpotifySongSearchResults.Clear();
            }
            else
            {
                FindResultsFromLibrary.Visibility = Visibility.Hidden;
                LibrarySearchResultsSelected = null;
                LibrarySearchResults.Clear();
            }
        }
        private void UseFirstSearchResult()
        {
            if (SyncToService)
            {
                if (SpotifySongSearchResults.Any())
                {
                    var changeTo = SpotifySongSearchResults.First();
                    ChangePopuplatedSongWithSpotifySongRelated(changeTo);
                }
            }
            else
            {
                if (LibrarySearchResults.Any())
                {
                    var changeTo = LibrarySearchResults.First();
                    ChangePopuplatedSongWithMBLibraryRelated(changeTo);
                }
            }
        }

        private DispatcherTimer searchLibraryDelayTimer = null;

        private void SongSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (searchLibraryDelayTimer == null)
            {
                searchLibraryDelayTimer = new DispatcherTimer();
                searchLibraryDelayTimer.Interval = TimeSpan.FromMilliseconds(300);
                searchLibraryDelayTimer.Tick += new EventHandler((s, ee) =>
                {
                    searchLibraryDelayTimer.Stop();

                    if (SyncToService)
                    {
                        _ = SearchOnSpotify(SongSearchBox.Text);
                    }
                    else
                    {
                        SearchOnLibrary(SongSearchBox.Text);
                    }
                });
            }

            searchLibraryDelayTimer.Stop();
            searchLibraryDelayTimer.Start();
        }

        private void SongSearchBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;

                UseFirstSearchResult();
                ClearSearchResults();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                ClearSearchResults();
                SongSearchBox.Text = "";
            }
            else if (e.Key == System.Windows.Input.Key.Down)
            {
                e.Handled = true;

                if (SyncToService)
                {
                    if (FindResultsFromSpotify.IsVisible)
                    {
                        if (SpotifySongSearchResults.Count > 1)
                        {
                            FindResultsFromSpotify.SelectedIndex = 1;
                            FindResultsFromSpotify.UpdateLayout();
                            FindResultsFromSpotify.Focus();
                        }
                        else
                            FindResultsFromSpotify.Focus();
                    }
                }
                else
                {
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
        }

        private void FindResultsFromLibrary_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                ClearSearchResults();
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                
                if (LibrarySearchResultsSelected != null)
                    ChangePopuplatedSongWithMBLibraryRelated(LibrarySearchResultsSelected);

                ClearSearchResults();
            }
        }

        private void FindResultsFromLibrary_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LibrarySearchResultsSelected != null)
            {
                ChangePopuplatedSongWithMBLibraryRelated(LibrarySearchResultsSelected);
                ClearSearchResults();
            }
        }


        private void ChangePopuplatedSongWithSpotifySongRelated(FullTrack changeTo)
        {
            if (PopulatedSongSelected != null)
            {
                PopulatedSongSelected.SpotifySong = changeTo;
                PopulatedSongSelected.Checked = true;

                SongConferenceListBox.Items.Refresh();
            }
        }

        private async Task SearchOnSpotify(string text)
        {
            SpotifySongSearchResults.Clear();

            try
            {
                text = text.Trim();
                string[] query = text.Split(new[] { " - " }, StringSplitOptions.None);
                
                if (query.Length != 2)
                    return;

                var results = await Spotify.FindSongsInSpotifyDatabase(query[0], query[1]);

                foreach (var result in results.Take(10))
                    SpotifySongSearchResults.Add(result);

                SpotifySongSearchResultsSelected = SpotifySongSearchResults.FirstOrDefault();
            }
            catch
            {
                Log("Something goes wrong at search.");
            }
            finally
            {
                FindResultsFromSpotify.Visibility = SpotifySongSearchResults.Any() ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void FindResultsFromSpotify_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SpotifySongSearchResultsSelected != null)
            {
                ChangePopuplatedSongWithSpotifySongRelated(SpotifySongSearchResultsSelected);
                ClearSearchResults();
            }
        }

        private void FindResultsFromSpotify_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                ClearSearchResults();
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;

                if (SpotifySongSearchResultsSelected != null)
                    ChangePopuplatedSongWithSpotifySongRelated(SpotifySongSearchResultsSelected);

                ClearSearchResults();
            }
        }
    }
}
