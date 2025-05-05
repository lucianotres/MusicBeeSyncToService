using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace MusicBeePlugin.WPF
{
    public class FullTrackStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var FullTrack = value as SpotifyAPI.Web.FullTrack;

            if (FullTrack == null)
                return "";

            return String.Join("; ", FullTrack.Artists.Select(a => a.Name)) + " - " + FullTrack.Name;         
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
