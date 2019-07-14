using System.Diagnostics.CodeAnalysis;

namespace Itinero.Transit.Data.Core
{
    public class Connection
    {
        public const ushort ModeNormal = 0;
        public const ushort ModeGetOnOnly = 1;
        public const ushort ModeGetOffOnly = 2;
        public const ushort ModeCantGetOnOff = 3;

        public const ushort ModeCancelled = 4;

        public Connection()
        {
        }

        public Connection(
            ConnectionId id,
            string globalId,
            StopId departureStop,
            StopId arrivalStop,
            ulong departureTime,
            ushort travelTime,
            ushort arrivalDelay,
            ushort departureDelay,
            ushort mode,
            TripId tripId
        )
        {
            Id = id;
            DepartureTime = departureTime;
            TravelTime = travelTime;
            ArrivalDelay = arrivalDelay;
            DepartureDelay = departureDelay;
            Mode = mode;
            TripId = tripId;
            GlobalId = globalId;
            DepartureStop = departureStop;
            ArrivalStop = arrivalStop;
            ArrivalTime = departureTime + travelTime;
        }

        public ConnectionId Id { get; set; }
        public string GlobalId { get; set; }

        public ulong ArrivalTime { get; set; }

        public ulong DepartureTime { get; set; }

        public ushort TravelTime { get; set; }

        public ushort ArrivalDelay { get; set; }

        public ushort DepartureDelay { get; set; }

        public ushort Mode { get; set; }

        public TripId TripId { get; set; }

        public StopId DepartureStop { get; set; }

        public StopId ArrivalStop { get; set; }


        public bool CanGetOn()
        {
            var m = (Mode % 4);
            return m == 0 || m == 1;
        }

        public bool CanGetOff()
        {
            var m = (Mode % 4);
            return m == 0 || m == 2;
        }

        public bool IsCancelled()
        {
            return (Mode & ModeCancelled) == ModeCancelled;
        }


        public override bool Equals(object obj)
        {
            if (obj is Connection c)
            {
                return Equals(this, c);
            }

            return false;
        }


        public bool Equals(Connection x, Connection y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Id.Equals(y.Id) && string.Equals(x.GlobalId, y.GlobalId) && x.ArrivalTime == y.ArrivalTime &&
                   x.DepartureTime == y.DepartureTime && x.TravelTime == y.TravelTime &&
                   x.ArrivalDelay == y.ArrivalDelay && x.DepartureDelay == y.DepartureDelay && x.Mode == y.Mode &&
                   x.TripId.Equals(y.TripId) && x.DepartureStop.Equals(y.DepartureStop) &&
                   x.ArrivalStop.Equals(y.ArrivalStop);
        }

        [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id.GetHashCode();
                hashCode = (hashCode * 397) ^ GlobalId.GetHashCode();
                hashCode = (hashCode * 397) ^ ArrivalTime.GetHashCode();
                hashCode = (hashCode * 397) ^ DepartureTime.GetHashCode();
                hashCode = (hashCode * 397) ^ TravelTime.GetHashCode();
                hashCode = (hashCode * 397) ^ ArrivalDelay.GetHashCode();
                hashCode = (hashCode * 397) ^ DepartureDelay.GetHashCode();
                hashCode = (hashCode * 397) ^ Mode.GetHashCode();
                hashCode = (hashCode * 397) ^ TripId.GetHashCode();
                hashCode = (hashCode * 397) ^ DepartureStop.GetHashCode();
                hashCode = (hashCode * 397) ^ ArrivalStop.GetHashCode();
                return hashCode;
            }
        }

    }
}