using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// NavaidPoint.cs
namespace Thesis_testing_1
{
    public sealed class NavaidPoint
    {
        // EN: The identifier of the navigation point, for example a VOR, NDB or waypoint name.
        // HU: A navigációs pont azonosítója, például VOR, NDB vagy waypoint neve.
        public string Ident { get; set; }

        // EN: Latitude of the navigation point.
        // HU: A navigációs pont földrajzi szélessége.
        public double Lat { get; set; }

        // EN: Longitude of the navigation point.
        // HU: A navigációs pont földrajzi hosszúsága.
        public double Lon { get; set; }

        // EN: Type code of the navigation point.
        // HU: A navigációs pont típusát jelölő kód.
        public int Type { get; set; }

        // EN: Full name of the navigation point, if it is available.
        // HU: A navigációs pont teljes neve, ha rendelkezésre áll.
        public string Name { get; set; }
    }
}
