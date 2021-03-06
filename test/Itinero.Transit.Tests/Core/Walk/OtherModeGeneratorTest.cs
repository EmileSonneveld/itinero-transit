using System;
using System.Collections.Generic;
using System.Threading;
using Itinero.Transit.Data;
using Itinero.Transit.Data.Core;
using Itinero.Transit.Journey;
using Itinero.Transit.Journey.Metric;
using Itinero.Transit.OtherMode;
using Itinero.Transit.Utils;
using Xunit;

namespace Itinero.Transit.Tests.Core.Walk
{
    public class OtherModeGeneratorTest
    {
        [Fact]
        public void FirstLastMilePolicy_TimeBetween_MultipleStops_ExpectsCorrectTimings()
        {
            var stop0 = new Stop("0", new StopId(0, 0, 0), 0, 0, null);
            var stop1 = new Stop("1", new StopId(0, 0, 1), 0.0001, 0, null);
            var stop2 = new Stop("2", new StopId(0, 0, 2), 0.0002, 0, null);

            var mixed = new FirstLastMilePolicy(
                new FixedGenerator(1),
                new FixedGenerator(2),
                new List<StopId> {stop1.Id},
                new FixedGenerator(3),
                new List<StopId> {stop2.Id}
            );


            // Normal situation
            Assert.Equal((uint) 1, mixed.TimeBetween(stop0, stop1));
            // First mile
            Assert.Equal((uint) 2, mixed.TimeBetween(stop1, stop0));


            // Normal situation
            Assert.Equal((uint) 1, mixed.TimeBetween(stop2, stop1));
            // last mile
            Assert.Equal((uint) 3, mixed.TimeBetween(stop0, stop2));

            var timesBetween = mixed.TimesBetween(stop0, new List<IStop>
            {
                stop1,
                stop2
            });

            Assert.Equal((uint) 1,
                timesBetween[stop1.Id]);
            Assert.Equal((uint) 3,
                timesBetween[stop2.Id]);


            timesBetween = mixed.TimesBetween(stop1, new List<IStop>
            {
                stop0,
                stop2
            });

            Assert.Equal((uint) 2,
                timesBetween[stop0.Id]);
            Assert.Equal((uint) 2,
                timesBetween[stop2.Id]);
        }

        [Fact]
        public void CrowsFlight_TImesBetween_ExpectsCorrectTimes()
        {
            var tdb = new TransitDb(0);
            var wr = tdb.GetWriter();
            var stop0 = wr.AddOrUpdateStop("0", 6, 50);
            var stop1 = wr.AddOrUpdateStop("1", 6.001, 50);
            wr.Close();

            var stops = tdb.Latest.StopsDb.GetReader();

            var exp = (uint) DistanceEstimate.DistanceEstimateInMeter(50, 6, 50, 6.001);

            var crow = new CrowsFlightTransferGenerator(speed: 1.0f);
            Assert.Equal(exp, crow.TimeBetween(stops, stop0, stop1));

            stops.MoveTo(stop1);

            var from = new Stop("qsdf", new StopId(0, 0, 0), 6, 50, null);
            var all = crow.TimesBetween(from, new List<IStop> {stops});
            Assert.Single(all);
            Assert.Equal(exp, all[stop1]);
        }

        [Fact]
        public void ChainForward_WithCrowsFlight_ExpectsCorrectTime()
        {
            var tdb = new TransitDb(0);
            var wr = tdb.GetWriter();
            var stop0 = wr.AddOrUpdateStop("0", 6, 50);
            var stop1 = wr.AddOrUpdateStop("1", 6.001, 50);
            wr.Close();

            var stops = tdb.Latest.StopsDb.GetReader();

            var exp = (uint) DistanceEstimate.DistanceEstimateInMeter(50, 6, 50, 6.001);

            var crow = new CrowsFlightTransferGenerator(speed: 1.0f);
            Assert.Equal(exp, crow.TimeBetween(stops, stop0, stop1));


            var genesis = new Journey<TransferMetric>(stop0, 0, TransferMetric.Factory);
            var j = genesis.ChainForwardWith(stops, crow, stop1);
            Assert.Equal(exp, j.Time);
        }


        [Fact]
        public void UseCache_TImeBetween_ExpectsCacheIsUsed()
        {
            var verySlow = new VerySlowOtherModeGenerator().UseCache();


            var time = DateTime.Now;

            var stop0 = new StopId(0, 0, 0);
            var stop1 = new StopId(0, 0, 1);
            var diff = verySlow.TimeBetween(new DummyReader(), stop0, stop1);
            var timeMid = DateTime.Now;
            Assert.True((timeMid - time).TotalMilliseconds >= 999);

            var diff0 = verySlow.TimeBetween(new DummyReader(), stop0, stop1);
            var timeEnd = DateTime.Now;
            Assert.Equal(diff, diff0);
            Assert.True((timeEnd - timeMid).TotalMilliseconds < 100);
        }
    }

    internal class VerySlowOtherModeGenerator : IOtherModeGenerator
    {
        public uint TimeBetween(IStop __, IStop ___)
        {
            Thread.Sleep(1000);
            return 50;
        }

        public Dictionary<StopId, uint> TimesBetween(IStop @from,
            IEnumerable<IStop> to)
        {
            return this.DefaultTimesBetween(from, to);
        }

        public Dictionary<StopId, uint> TimesBetween(IEnumerable<IStop> @from, IStop to)
        {
            return this.DefaultTimesBetween(from, to);
        }

        public uint Range()
        {
            return 1000;
        }

        public string OtherModeIdentifier()
        {
            return "test";
        }

        public IOtherModeGenerator GetSource(StopId @from, StopId to)
        {
            return this;
        }
    }

    class FixedGenerator : IOtherModeGenerator
    {
        private readonly uint _time;

        public FixedGenerator(uint time)
        {
            _time = time;
        }

        public uint TimeBetween(IStop @from, IStop to)
        {
            return _time;
        }

        public Dictionary<StopId, uint> TimesBetween(IStop @from, IEnumerable<IStop> to)
        {
            return this.DefaultTimesBetween(from, to);
        }

        public Dictionary<StopId, uint> TimesBetween(IEnumerable<IStop> @from, IStop to)
        {
            return this.DefaultTimesBetween(from, to);
        }

        public uint Range()
        {
            return uint.MaxValue;
        }

        public string OtherModeIdentifier()
        {
            return "test";
        }

        public IOtherModeGenerator GetSource(StopId @from, StopId to)
        {
            return this;
        }
    }
}