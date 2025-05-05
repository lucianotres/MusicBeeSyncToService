using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicBeePlugin.Services
{
    public class MusicBeeSyncHelper
    {

        public Plugin.MusicBeeApiInterface MbApiInterface;
        public List<MusicBeePlaylist> Playlists { get; private set; } = new List<MusicBeePlaylist>();
        public List<MusicBeeSong> Songs { get; private set; } = new List<MusicBeeSong>();

        public MusicBeeSyncHelper(Plugin.MusicBeeApiInterface apiInterface)
        {
            MbApiInterface = apiInterface;
            RefreshMusicBeePlaylists();
            RefreshMusicBeeSongs();
        }

        public void RefreshMusicBeePlaylists()
        {
            Playlists.Clear();
            Playlists = GetMusicBeePlaylists();
        }

        public void RefreshMusicBeeSongs()
        {
            Songs.Clear();
            Songs = GetMusicBeeSongs();
        }

        private List<MusicBeePlaylist> GetMusicBeePlaylists()
        {
            List<MusicBeePlaylist> MbPlaylists = new List<MusicBeePlaylist>();
            MbApiInterface.Playlist_QueryPlaylists();
            string playlist = MbApiInterface.Playlist_QueryGetNextPlaylist();
            while (playlist != null)
            {
                string playlistName = MbApiInterface.Playlist_GetName(playlist);
                MusicBeePlaylist MbPlaylist = new MusicBeePlaylist();
                MbPlaylist.mbName = playlist;
                MbPlaylist.Name = playlistName;

                // get playlist tracks
                string[] playlistFiles = null;
                if (MbApiInterface.Playlist_QueryFiles(playlist))
                {
                    bool success = MbApiInterface.Playlist_QueryFilesEx(playlist, ref playlistFiles);
                    if (!success)
                        throw new Exception("Couldn't get playlist files");
                }
                else
                {
                    playlistFiles = new string[0];
                }

                foreach (string file in playlistFiles)
                {
                    string title = MbApiInterface.Library_GetFileTag(file, Plugin.MetaDataType.TrackTitle);
                    string artist = MbApiInterface.Library_GetFileTag(file, Plugin.MetaDataType.Artist);
                    string album = MbApiInterface.Library_GetFileTag(file, Plugin.MetaDataType.Album);

                    var song = new MusicBeeSong()
                    {
                        Album = album,
                        Artist = artist,
                        Title = title,
                        Filename = file,
                    };

                    MbPlaylist.Songs.Add(song);
                }

                MbPlaylists.Add(MbPlaylist);

                // Query the next mbPlaylist to start again
                playlist = MbApiInterface.Playlist_QueryGetNextPlaylist();
            }

            MbPlaylists = MbPlaylists.OrderBy(p => p.Name).ToList();
            return MbPlaylists;
        }

        private List<MusicBeeSong> GetMusicBeeSongs()
        {
            string[] files = null;
            List<MusicBeeSong> allMbSongs = new List<MusicBeeSong>();

            if (MbApiInterface.Library_QueryFiles("domain=library"))
            {
                // Old (deprecated)
                //public char[] filesSeparators = { '\0' };
                //files = _mbApiInterface.Library_QueryGetAllFiles().Split(filesSeparators, StringSplitOptions.RemoveEmptyEntries);
                MbApiInterface.Library_QueryFilesEx("domain=library", ref files);
            }
            else
            {
                files = new string[0];
            }

            foreach (string path in files)
            {
                MusicBeeSong thisSong = new MusicBeeSong();
                thisSong.Filename = path;
                thisSong.Artist = MbApiInterface.Library_GetFileTag(path, Plugin.MetaDataType.Artist);
                thisSong.Title = MbApiInterface.Library_GetFileTag(path, Plugin.MetaDataType.TrackTitle);
                thisSong.Album = MbApiInterface.Library_GetFileTag(path, Plugin.MetaDataType.Album);
                allMbSongs.Add(thisSong);
            }
            return allMbSongs;
        }


        public IEnumerable<MusicBeeSongSearch> FindSongsByWords(params string[] words)
        {
            int wordsCount = words.Length;

            if (words == null || wordsCount == 0)
                return new List<MusicBeeSongSearch>(0);

            return Songs
                .Select(s =>
                {
                    int score = 0;
                    var title = s.Title.ToLower();
                    var artist = s.Artist.ToLower();
                    var album = s.Album.ToLower();

                    foreach (var word in words)
                    {
                        if (title.Contains(word))
                        {
                            score += 10;
                        }
                        if (artist.Contains(word))
                        {
                            score += 5;
                        }
                        if (album.Contains(word))
                        {
                            score += 1;
                        }
                    }

                    return new MusicBeeSongSearch
                    {
                        Song = s,
                        SearchScore = score
                    };
                })
                .Where(s => s.SearchScore > (wordsCount * 5)) //minimum score should match the number of words for artist at least
                .OrderByDescending(s => s.SearchScore)
                .Take(10)
                .ToList();
        }
    }
}
