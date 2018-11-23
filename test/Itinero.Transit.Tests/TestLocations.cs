//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Itinero.Transit;
//using JsonLD.Core;
//using Xunit;
//using Xunit.Abstractions;
//
//// ReSharper disable PossibleMultipleEnumeration
//
//namespace Itinero.Transit_Tests
//{
//    public class TestLocations
//    {
//        private readonly ITestOutputHelper _output;
//
//        public TestLocations(ITestOutputHelper output)
//        {
//            _output = output;
//        }
//
//
//        [Fact]
//        public void TestDeLijnLocations()
//        {
//            var loader = new Downloader();
//            var uri = new Uri("http://dexagod.github.io/stoplocations/t0.jsonld");
//            var wanted = new Uri("http://dexagod.github.io/stopsdata/d6.jsonld#12006");
//            var fragLoader = new JsonLdProcessor(loader, wanted);
//            var nodeLoader = new JsonLdProcessor(loader, uri);
//
//            var traverser = new RdfTreeTraverser(uri, nodeLoader, fragLoader);
//            var found = traverser.GetLocationsCloseTo(51.21576f, 3.22001f, 250);
//            Assert.Equal(6, found.Count());
//            var names = new HashSet<string>();
//            foreach (var stop in found)
//            {
//                var name = traverser.GetCoordinateFor(stop).Name;
//                names.Add(name);
//                Log($"name: {name}; {stop}");
//            }
//
//            Assert.True(names.Contains("N.Gombertstraat"));
//            Assert.True(names.Contains("Howest"));
//            Assert.True(names.Contains("Ezelpoort"));
//        }
//
//        [Fact]
//        public void TestCloseLocations()
//        {
//            const float lat = 51.21576f;
//            const float lon = 3.22048f;
//
//            var uri = new Uri("http://irail.be/stations");
//            var locations = new LocationsFragment(uri);
//
//
//            // ReSharper disable once RedundantArgumentDefaultValue
//            var loader = new Downloader(false);
//            var proc = new JsonLdProcessor(loader, uri);
//
//            locations.Download(proc);
//
//            var found = (List<Uri>) locations.GetLocationsCloseTo(lat, lon, 5000);
//
//            Assert.True(found.Contains(new Uri("http://irail.be/stations/NMBS/008891009")));
//            Assert.True(found.Contains(new Uri("http://irail.be/stations/NMBS/008891033")));
//            Assert.Equal(2, found.Count);
//        }
//
//
//        [Fact]
//        public void TestDeLijnFragment()
//        {
//            var loader = new Downloader();
//            var uri = new Uri(
//                "https://dexagod.github.io/stopsdata/d2.jsonld");
//            var frag = new LocationsFragment(uri);
//            frag.Download(new JsonLdProcessor(loader, uri));
//            Log(frag.ToString());
//            Assert.True(frag.ToString().Length > 10000);
//            Assert.True(frag.ToString().StartsWith("Location dump with 1044 locations:\n  Location \'Stedestraat\' ("));
//        }
//
//        // ReSharper disable once UnusedMember.Local
//        private void Log(string s)
//        {
//            _output.WriteLine(s);
//        }
//    }
//}