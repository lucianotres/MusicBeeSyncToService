using MusicBeePlugin.Models;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using static Swan.Terminal;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace MusicBeePlugin.Services
{
    public class SpotifySyncHelper
    {
        private PrivateUser Profile;
        private SpotifyClient Spotify;
        private Action<string> Log;
        private EmbedIOAuthServer Server;
        private MusicBeeSyncHelper musicBeeSyncHelper;

        public List<SimplePlaylist> Playlists { get; set; } = new List<SimplePlaylist>();

        public SpotifySyncHelper(Action<string> log, MusicBeeSyncHelper mb)
        {
            if (mb == null)
            {
                throw new ArgumentNullException(nameof(mb), "MusicBeeSyncHelper cannot be null");
            }

            Log = log;
            musicBeeSyncHelper = mb;
        }

        public async Task<bool> LoginAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            await LoginToSpotify(tcs);
            return await tcs.Task;
        }

        private async Task LoginToSpotify(TaskCompletionSource<bool> tcs)
        {
            Server = new EmbedIOAuthServer(new Uri("http://localhost:5000/callback"), 5000);
            await Server.Start();

            Server.ImplictGrantReceived += async (object sender, ImplictGrantResponse response) =>
            {
                await Server.Stop();
                if (response.AccessToken != null)
                {
                    Spotify = new SpotifyClient(response.AccessToken);
                    Profile = await Spotify.UserProfile.Current();
                    tcs.SetResult(Spotify != null);
                }
                else
                {
                    Log("Error when attempting to log in");
                    tcs.SetResult(false);
                }
            };

            var request = new LoginRequest(Server.BaseUri, SpotifySecrets.CLIENT_ID, LoginRequest.ResponseType.Token)
            {
                Scope = new List<string> 
                {
                    Scopes.UserLibraryRead,
                    Scopes.PlaylistModifyPublic
                }
            };
            BrowserUtil.Open(request.ToUri());
        }


        public bool IsLoggedIn()
        {
            return Spotify != null;
        }

        public async Task<List<SimplePlaylist>> RefreshPlaylists()
        {
            Playlists.Clear();
            Paging<SimplePlaylist> response = await Spotify.Playlists.GetUsers(Profile.Id);
            var all = await Spotify.PaginateAll(response);
            foreach (var item in all)
            {
                Playlists.Add(item);
            }

            return Playlists;
        }

        public async Task SyncToSpotify(List<MusicBeePlaylist> mbPlaylistsToSync,
            SyncToSpotifySettings settings)
        {
            PopulateErrors.Clear();

            foreach (MusicBeePlaylist playlist in mbPlaylistsToSync)
            {
                PopulateMusicBeePlaylist = playlist;
                await PopulateSpotifyPlaylistSongsFromMbPlaylist(false);

                await SaveSpotifyPlaylistPopulatedFromMbPlaylist(settings);
            }
        }

        public async Task SyncToMusicBee(List<SimplePlaylist> playlists)
        {
            PopulateErrors.Clear();

            // Go through each playlist we want to sync in turn
            foreach (SimplePlaylist playlist in playlists)
            {
                PopulateSpotifyPlaylist = playlist;
                await PopulateMbPlaylistSongsFromSpotifyPlaylist(false);

                await SaveMbPlaylistPopulatedFromSpotifyPlaylist();
            }

            // Get the local playlists again
            musicBeeSyncHelper.RefreshMusicBeePlaylists();
        }

        private async Task<List<FullTrack>> GetFullTracksFromSpotifyPlaylist(SimplePlaylist playlist)
        {
            var tracks = new List<FullTrack>();

            // Get all tracks for playlist
            var fp = await Spotify.Playlists.Get(playlist.Id);
            var allTracks = await Spotify.PaginateAll(fp.Tracks);
            
            foreach (var t in allTracks)
            {
                if (t.Track is FullTrack track)
                {
                    tracks.Add(track);
                }
            }

            return tracks;
        }

        private MusicBeeSong FindMusicBeeSongFromSpotifyTrack(FullTrack track)
        {
            foreach (MusicBeeSong mbSong in musicBeeSyncHelper.Songs)
            {
                // Titles, artists, and albums could not be exact matches
                bool titleMatches = FlexibleStringMatch(track.Name, mbSong.Title);
                bool artistMatches = (track.Artists.Exists(a => FlexibleStringMatch(a.Name, mbSong.Artist)));
                bool albumMatches = FlexibleStringMatch(track.Album.Name, mbSong.Album);
                if (titleMatches && artistMatches && albumMatches)
                {
                    return mbSong;
                }
            }

            return null;
        }

        public List<IPlaylistSyncError> PopulateErrors { get; private set; } = new List<IPlaylistSyncError>();
        public List<PopulatedSong> PopulatedSongs { get; private set; } = new List<PopulatedSong>();
        public SimplePlaylist PopulateSpotifyPlaylist { get; set; } = null;
        public MusicBeePlaylist PopulateMusicBeePlaylist { get; set; } = null;

        public async Task PopulateMbPlaylistSongsFromSpotifyPlaylist(bool clearErrors = true)
        {
            if (PopulateSpotifyPlaylist == null)
            {
                throw new ArgumentNullException(nameof(PopulateSpotifyPlaylist), "PopulateSpotifyPlaylist cannot be null");
            }

            if (clearErrors)
            {
                PopulateErrors.Clear();
            }

            PopulatedSongs.Clear();

            // Get all tracks for playlist
            var tracks = await GetFullTracksFromSpotifyPlaylist(PopulateSpotifyPlaylist);

            PopulatedSongs.AddRange(tracks.Select(t => new PopulatedSong()
            {
                MbSong = null,
                SpotifySong = t
            }));

            foreach (var song in PopulatedSongs)
            {
                var trackToAdd = FindMusicBeeSongFromSpotifyTrack(song.SpotifySong);

                if (trackToAdd != null)
                {
                    song.MbSong = trackToAdd;
                    song.Checked = true;
                }
                else
                {
                    PopulateErrors.Add(new UnableToFindSpotifyTrackError()
                    {
                        AlbumName = song.SpotifySong.Album.Name,
                        ArtistName = song.SpotifySong.Artists.FirstOrDefault().Name,
                        PlaylistName = PopulateSpotifyPlaylist.Name,
                        TrackName = song.SpotifySong.Name,
                        SearchedService = false
                    });
                }
            }
        }

        public async Task SaveMbPlaylistPopulatedFromSpotifyPlaylist() =>
            await Task.Run(() =>
            {
                if (PopulateSpotifyPlaylist == null)
                {
                    throw new ArgumentNullException(nameof(PopulateSpotifyPlaylist), "PopulateSpotifyPlaylist cannot be null");
                }

                //mbAPI expects a string array of song filenames to create a playlist
                var mbPlaylistSongFiles = PopulatedSongs
                    .Where(w => w.Checked && w.MbSong != null)
                    .Select(s => s.MbSong.Filename)
                    .ToArray();

                // Now we need to delete any existing playlist with matching name
                MusicBeePlaylist localPlaylist = musicBeeSyncHelper.Playlists.FirstOrDefault(p => p.Name == PopulateSpotifyPlaylist.Name);
                if (localPlaylist != null)
                {
                    musicBeeSyncHelper.MbApiInterface.Playlist_DeletePlaylist(localPlaylist.mbName);
                }

                // Create the playlist locally
                string playlistRelativeDir = "";
                string playlistName = PopulateSpotifyPlaylist.Name;

                // if it's a date playlist, remove first Z
                if (playlistName.StartsWith("Z "))
                {
                    playlistName = playlistName.Skip(2).ToString();
                }

                string[] itemsInPath = PopulateSpotifyPlaylist.Name.Split('\\');
                if (itemsInPath.Length > 1)
                {
                    // Creates a playlist at top level directory
                    musicBeeSyncHelper.MbApiInterface.Playlist_CreatePlaylist("", playlistName, mbPlaylistSongFiles);
                }

                musicBeeSyncHelper.MbApiInterface.Playlist_CreatePlaylist(playlistRelativeDir, playlistName, mbPlaylistSongFiles);
            });

        public async Task PopulateSpotifyPlaylistSongsFromMbPlaylist(bool clearErrors = true)
        {
            if (PopulateMusicBeePlaylist == null)
            {
                throw new ArgumentNullException(nameof(PopulateMusicBeePlaylist), "PopulateMusicBeePlaylist cannot be null");
            }

            if (clearErrors)
            {
                PopulateErrors.Clear();
            }
            
            PopulatedSongs.Clear();

            PopulatedSongs.AddRange(PopulateMusicBeePlaylist.Songs.Select(s => new PopulatedSong()
            {
                MbSong = s,
                SpotifySong = null
            }));

            foreach (var song in PopulatedSongs)
            {
                string title = song.MbSong.Title;
                string artist = song.MbSong.Artist;
                string album = song.MbSong.Album;

                string artistEsc = EscapeChar(artist.ToLower());
                string titleEsc = EscapeChar(title.ToLower());
                string searchStr = $"artist:{artistEsc} track:{titleEsc}";
                var request = new SearchRequest(SearchRequest.Types.Track, searchStr);
                SearchResponse search = await Spotify.Search.Item(request);

                if (search.Tracks == null || search.Tracks.Items == null)
                {
                    Log($"Could not find track on Spotify '{searchStr}' for '{title}' by '{artist}'");
                    continue;
                }

                if (search.Tracks.Items.Count == 0)
                {
                    Log($"Found 0 results on Spotify for: {searchStr} for '{title}' by '{artist}'");
                    continue;
                }

                // try to find track matching artist and title
                FullTrack trackToAdd = null;
                foreach (FullTrack track in search.Tracks.Items)
                {
                    // Titles, artists, and albums could not be exact matches
                    bool titleMatches = FlexibleStringMatch(track.Name, title);
                    bool artistMatches = (track.Artists.Exists(a => FlexibleStringMatch(a.Name, artist)));
                    bool albumMatches = FlexibleStringMatch(track.Album.Name, album);
                    if (titleMatches && artistMatches && albumMatches)
                    {
                        trackToAdd = track;
                        break;
                    }
                }

                if (trackToAdd == null)
                {
                    trackToAdd = search.Tracks.Items.FirstOrDefault();
                    Log($"Didn't find a perfect match for {searchStr} for '{title}' by '{artist}', so using '{trackToAdd.Name}' by '{trackToAdd.Artists.FirstOrDefault().Name}' instead");
                }

                song.SpotifySong = trackToAdd;
                song.Checked = true;
            }
        }

        public async Task SaveSpotifyPlaylistPopulatedFromMbPlaylist(SyncToSpotifySettings settings)
        {
            if (PopulateMusicBeePlaylist == null)
            {
                throw new ArgumentNullException(nameof(PopulateMusicBeePlaylist), "PopulateMusicBeePlaylist cannot be null");
            }

            // Use LINQ to check for a playlist with the same name
            // If there is one, clear it's contents, otherwise create one
            // Unless it's been deleted, in which case pretend it doesn't exist.
            // I'm not sure how to undelete a playlist, or even if you can
            string spotifyPlaylistName = null;
            if (settings.IncludeFoldersInPlaylistName)
            {
                spotifyPlaylistName = PopulateMusicBeePlaylist.Name;
            }
            else
            {
                spotifyPlaylistName = PopulateMusicBeePlaylist.Name.Split('\\').Last();
            }

            if (settings.IncludeZAtStartOfDatePlaylistName)
            {
                // if it starts with a 2, it's a date playlist
                if (spotifyPlaylistName.StartsWith("2"))
                {
                    spotifyPlaylistName = $"Z {spotifyPlaylistName}";
                }
            }

            // If Spotify playlist with same name already exists, clear it.
            // Otherwise create one
            SimplePlaylist thisPlaylist = Playlists.FirstOrDefault(p => p.Name == spotifyPlaylistName);
            string thisPlaylistId;
            if (thisPlaylist != null)
            {
                var request = new PlaylistReplaceItemsRequest(new List<string>() { });
                var success = await Spotify.Playlists.ReplaceItems(thisPlaylist.Id, request);
                if (!success)
                {
                    Log("Error while trying to clear playlist before syncing new tracks");
                    return;
                }
                thisPlaylistId = thisPlaylist.Id;
            }
            else
            {
                var request = new PlaylistCreateRequest(spotifyPlaylistName);
                FullPlaylist newPlaylist = await Spotify.Playlists.Create(Profile.Id, request);
                thisPlaylistId = newPlaylist.Id;
            }

            var uris = PopulatedSongs
                .Where(w => w.Checked && w.SpotifySong != null)
                .Select(s => s.SpotifySong.Uri)
                .ToList();

            while (uris.Count > 0)
            {
                List<string> currUris = uris.Take(75).ToList();
                if (currUris.Count == 0)
                {
                    break;
                }

                uris.RemoveRange(0, currUris.Count);
                var request = new PlaylistAddItemsRequest(currUris);
                var resp = await Spotify.Playlists.AddItems(thisPlaylistId, request);
                if (resp == null)
                {
                    Log("Error while trying to update playlist with track uris");
                    return;
                }
            }
        }

        private string EscapeChar(string input)
        {
            int openIndex = input.IndexOf("(f");
            int closeIndex = input.IndexOf(")");
            if (openIndex >= 0 && closeIndex > 0 && openIndex < closeIndex)
            {
                input = input.Remove(openIndex, closeIndex-openIndex);
            }

            return input.Replace(",", " ")
                .Replace(".", " ")
                .Replace("!", " ")
                .Replace("?", " ")
                .Replace("'",  "")
                .Replace(")", " ")
                .Replace("(", " ")
                .Replace("#", " ")
                .Replace("\\"," ")
                .Replace("/", " ")
                .Replace("&", " ")
                .Replace(":", " ")
                .Replace(";", " ")
                .Replace(" ", " ");
        }

        // artist, title, albums sometimes contain something like "featuring ..." which can cause a mismatch between services
        // so we'll be more flexible in what we consider a match
        private bool FlexibleStringMatch(string a, string b)
        {
            var x = a.ToLower();
            var y = b.ToLower();

            return x == y
                || x.Contains(y)
                || y.Contains(x);
        }
    }
}
