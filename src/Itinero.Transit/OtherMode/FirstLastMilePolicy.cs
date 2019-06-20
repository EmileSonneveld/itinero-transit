using System;
using System.Collections.Generic;
using Itinero.Transit.Data;
using Itinero.Transit.Journey;

namespace Itinero.Transit.OtherMode
{
    /// <summary>
    /// Applies a different walk policy depending on first mile/last mile
    /// </summary>
    public class FirstLastMilePolicy : IOtherModeGenerator
    {
        private readonly IOtherModeGenerator _defaultWalk;
        private readonly IOtherModeGenerator _firstMile;
        private readonly IOtherModeGenerator _lastMile;
        private readonly float _range;
        private readonly HashSet<LocationId> _firstMileStops;
        private readonly HashSet<LocationId> _lastMileStops;

        public static IOtherModeGenerator CreateFrom<T>(Profile<T> profile,
         IEnumerable<LocationId> firstMileStops,
            IEnumerable<LocationId> lastMileStops) where T : IJourneyMetric<T>
        {
            if (profile.FirstMileWalksGenerator == profile.WalksGenerator &&
                profile.LastMileWalksGenerator == profile.WalksGenerator)
            {
                return profile.WalksGenerator;
            }
            
            return new FirstLastMilePolicy(
                profile.WalksGenerator,
                profile.FirstMileWalksGenerator, firstMileStops,
                profile.LastMileWalksGenerator, lastMileStops
                );
            
            
            
        }

        public FirstLastMilePolicy(
            IOtherModeGenerator otherModeGeneratorImplementation,
            IOtherModeGenerator firstMile, IEnumerable<LocationId> firstMileStops,
            IOtherModeGenerator lastMile, IEnumerable<LocationId> lastMileStops)
        {
            _firstMile = firstMile;
            _firstMileStops = new HashSet<LocationId>(firstMileStops);
            _lastMile = lastMile;
            _lastMileStops = new HashSet<LocationId>(lastMileStops);
            _defaultWalk = otherModeGeneratorImplementation;
            _range = Math.Max(firstMile.Range(),
                Math.Max(lastMile.Range(), _defaultWalk.Range()));
        }

        public uint TimeBetween(IStop from, IStop to)
        {
            if (_firstMileStops.Contains(from.Id))
            {
                return _firstMile.TimeBetween(from, to);
            }

            if (_lastMileStops.Contains(to.Id))
            {
                return _lastMile.TimeBetween(from, to);
            }

            return _defaultWalk.TimeBetween(@from, to);
        }

        public Dictionary<LocationId, uint> TimesBetween(IStop @from,
            IEnumerable<IStop> to)
        {
            if (_firstMileStops.Contains(from.Id))
            {
                return _firstMile.TimesBetween(from, to);
            }

            var tosDefault = new List<IStop>();
            var tosLastMile = new List<IStop>();

            foreach (var stop in to)
            {
                if (_lastMileStops.Contains(stop.Id))
                {
                    tosLastMile.Add(stop);
                }
                else
                {
                    tosDefault.Add(stop);
                }
            }

            if (tosLastMile.Count > 0 && tosDefault.Count > 0)
            {
                var a = _lastMile.TimesBetween(from, tosLastMile);
                var b = _defaultWalk.TimesBetween(from, tosDefault);

                if (b.Count < a.Count)
                {
                    var c = a;
                    a = b;
                    b = c;
                }

                foreach (var kv in b)
                {
                    a.Add(kv.Key, kv.Value);
                }

                return a;
            }

            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (tosLastMile.Count > 0)
            {
                // No default walks
                return _lastMile.TimesBetween(from, tosLastMile);
            }

            return _defaultWalk.TimesBetween(@from, tosDefault);
        }

        public float Range()
        {
            return _range;
        }
    }
}