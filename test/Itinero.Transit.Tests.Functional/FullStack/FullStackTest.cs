using System.Linq;
using Itinero.Transit.Algorithms.Filter;
using Itinero.Transit.Data;
using Itinero.Transit.IO.OSM;
using Itinero.Transit.IO.OSM.Data;
using Itinero.Transit.Journey.Metric;
using Itinero.Transit.OtherMode;

namespace Itinero.Transit.Tests.Functional.FullStack
{
    public class FullStackTest : FunctionalTest<object, object>
    {
        protected override object Execute(object input)
        {
            var from = Constants.NearStationBruggeLatLon;
            var to = Constants.Gent;

            var tdbsNmbs = TransitDb.ReadFrom(Constants.Nmbs, 0);


            var defaultRealLifeProfile = new Profile<TransferMetric>(
                new InternalTransferGenerator(),
                new OsmTransferGenerator().UseCache(),
                TransferMetric.Factory,
                TransferMetric.ParetoCompare,
                new CancelledConnectionFilter(),
                new MaxNumberOfTransferFilter(8)
            );


            var calculator = tdbsNmbs.SelectProfile(defaultRealLifeProfile)
                .UseOsmLocations()
                .SelectStops(from, to)
                .SelectTimeFrame(Constants.TestDate.AddHours(9), Constants.TestDate.AddHours(14));

            NotNull(calculator.EarliestArrivalJourney());
            NotNull(calculator.LatestDepartureJourney());
            var all = calculator.AllJourneys();
            NotNull(all);
            True(all.Any());

            return null;
        }
    }
}