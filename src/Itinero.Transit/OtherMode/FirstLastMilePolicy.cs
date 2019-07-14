using System;
using System.Collections.Generic;
using Itinero.Transit.Data;
using Itinero.Transit.Data.Core;

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
        private readonly HashSet<StopId> _firstMileStops;
        private readonly HashSet<StopId> _lastMileStops;

        public FirstLastMilePolicy(
            IOtherModeGenerator otherModeGeneratorImplementation,
            IOtherModeGenerator firstMile, IEnumerable<StopId> firstMileStops,
            IOtherModeGenerator lastMile, IEnumerable<StopId> lastMileStops)
        {
            _firstMile = firstMile;
            _firstMileStops = new HashSet<StopId>(firstMileStops);
            _lastMile = lastMile;
            _lastMileStops = new HashSet<StopId>(lastMileStops);
            _defaultWalk = otherModeGeneratorImplementation;
            _range = Math.Max(firstMile.Range(),
                Math.Max(lastMile.Range(), _defaultWalk.Range()));
        }

        public uint TimeBetween(IStop from, IStop to)
        {
            return GeneratorFor(from.Id, to.Id).TimeBetween(from, to);
        }

        public Dictionary<StopId, uint> TimesBetween(IStop @from,
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

        public IOtherModeGenerator GeneratorFor(StopId from, StopId to)
        {
            if (_firstMileStops.Contains(from))
            {
                return _firstMile;
            }

            if (_lastMileStops.Contains(to))
            {
                return _lastMile;
            }

            return _defaultWalk;
        }

        public string OtherModeIdentifier()
        {
            return
                $"firstLastMile" +
                $"&default={Uri.EscapeUriString(_defaultWalk.OtherModeIdentifier())}" +
                $"&firstMile={Uri.EscapeUriString(_firstMile.OtherModeIdentifier())}" +
                $"&lastMile={Uri.EscapeUriString(_lastMile.OtherModeIdentifier())}"
                ;
                
        }
    }
}