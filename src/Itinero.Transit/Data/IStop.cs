using Itinero.Transit.Data.Attributes;
using Itinero.Transit.Data.Core;

namespace Itinero.Transit.Data
{
    /// <summary>
    /// Abstract definition of a stop.
    /// </summary>
    public interface IStop
    {
        /// <summary>
        /// Gets the global id.
        /// </summary>
        string GlobalId { get; }
        
        /// <summary>
        /// Gets the id.
        /// </summary>
        StopId Id { get; }
        
        /// <summary>
        /// Gets the longitude.
        /// </summary>
        double Longitude { get; }
        
        /// <summary>
        /// Gets the latitude.
        /// </summary>
        double Latitude { get; }
        
        /// <summary>
        /// Gets the attributes.
        /// </summary>
        IAttributeCollection Attributes { get; }
    }
}