using Itinero.Transit.Data.Attributes;

namespace Itinero.Transit.Data
{
    /// <inheritdoc />
    /// <summary>
    /// Representation of a stop.
    /// </summary>
    public class Stop : IStop
    {
        internal Stop(IStop stop)
        {
            GlobalId = stop.GlobalId;
            Id = stop.Id;
            Longitude = stop.Longitude;
            Latitude = stop.Latitude;
            if (Attributes != null)
            {
                Attributes = new AttributeCollection(Attributes);
            }
        }
        
        internal Stop(string globalId, LocationId id,
            double longitude, double latitude, IAttributeCollection attributes)
        {
            GlobalId = globalId;
            Id = id;
            Longitude = longitude;
            Latitude = latitude;
            if (attributes != null)
            {
                Attributes = new AttributeCollection(Attributes);
            }
        }
        
        /// <summary>
        /// Gets the global id.
        /// </summary>
        public string GlobalId { get; }

        /// <summary>
        /// Gets the id.
        /// </summary>
        public LocationId Id { get; }

        /// <summary>
        /// Gets the longitude.
        /// </summary>
        public double Longitude { get; }

        /// <summary>
        /// Gets the latitude.
        /// </summary>
        public double Latitude { get; }
        
        /// <summary>
        /// Gets the attributes.
        /// </summary>
        public IAttributeCollection Attributes { get; }

        public override string ToString()
        {
            return $"{GlobalId} ({Id}-[{Longitude},{Latitude}]) {Attributes}";
        }
    }
}