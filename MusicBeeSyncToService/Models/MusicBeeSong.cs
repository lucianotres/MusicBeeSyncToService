using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MusicBeePlugin.Models
{
    public class MusicBeeSong
    {
        public String Filename;
        public String Artist;
        public String Title;
        public String Album;
        
        public override string ToString()
        {
            return Artist + " - " + Title;
        }

    }

    public class MusicBeeSongSearch
    {
        public MusicBeeSong Song { get; set; }
        public int SearchScore { get; set; }
    }
}
