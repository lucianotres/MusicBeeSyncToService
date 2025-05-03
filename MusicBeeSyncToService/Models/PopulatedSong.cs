using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBeePlugin.Models
{
    public class PopulatedSong
    {
        public MusicBeeSong MbSong { get; set; }
        public FullTrack SpotifySong { get; set; }
        
        private bool checkedValue = false;
        public bool Checked 
        {
            get => checkedValue;
            set
            {
                if ((MbSong == null) || (SpotifySong == null))
                    checkedValue = false;
                else
                    checkedValue = value;
            }
        }

        public string MbSongDisplayName
        {
            get
            {
                if (MbSong == null)
                    return "";
                return MbSong.Artist + " - " + MbSong.Title;
            }
        }

        public string SpotifySongDisplayName
        {
            get
            {
                if (SpotifySong == null)
                    return "";
                return String.Join(", ", SpotifySong.Artists.Select(a => a.Name)) + " - " + SpotifySong.Name;
            }
        }
    }


    public static class PopulatedSongsForDesign
    {
        public static PopulatedSong[] Songs
        {
            get
            {
                return new PopulatedSong[]
                {
                    new PopulatedSong()
                    {
                        MbSong = new MusicBeeSong()
                        {
                            Artist = "Artist 1",
                            Title = "Title 1",
                            Album = "Album 1"
                        },
                        SpotifySong = new FullTrack()
                        {
                            Name = "Title 1",
                            Artists = new List<SimpleArtist>()
                            {
                                new SimpleArtist() { Name = "Artist 1" }
                            }
                        }
                    },
                    new PopulatedSong()
                    {
                        MbSong = new MusicBeeSong()
                        {
                            Artist = "Artist 2",
                            Title = "Title 2",
                            Album = "Album 2"
                        },
                        SpotifySong = new FullTrack()
                        {
                            Name = "Title 2",
                            Artists = new List<SimpleArtist>()
                            {
                                new SimpleArtist() { Name = "Artist 2" }
                            }
                        }
                    },
                    new PopulatedSong()
                    {
                        MbSong = new MusicBeeSong()
                        {
                            Artist = "Artist 3",
                            Title = "Title 3 with long name to test how wrap will work with the exceeding words",
                            Album = "Album 3"
                        },
                        SpotifySong = new FullTrack()
                        {
                            Name = "Title 3 with long name to test how wrap will work with the exceeding words",
                            Artists = new List<SimpleArtist>()
                            {
                                new SimpleArtist() { Name = "Artist 3" }
                            }
                        }
                    }
                };
            }
        }
    }
}
