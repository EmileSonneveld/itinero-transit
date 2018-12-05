using System;
using System.Collections.Generic;
using OsmSharp.API;

namespace Itinero.Transit.Data.Walks
{
    using UnixTime =UInt32;
    using LocId = UInt64;
    
    
    /// <summary>
    /// The transfergenerator takes a journey and a next connection.
    /// Using those, it extends the journey if this is possible.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface WalksGenerator<T> where T : IJourneyStats<T>
    {
        /// <summary>
        /// Create a new journey, which extends 'buildOn' with 'nextConnection'
        /// This might return null if the transfer time is too short.
        /// This might involve querying for footpaths
        /// </summary>
        /// <param name="buildOn"></param>
        /// <param name="nextConnection"></param>
        /// <param name="connDeparture"></param>
        /// <param name="connDepartureLoc"></param>
        /// <param name="connArrival"></param>
        /// <param name="connArrLoc"></param>
        /// <param name="tripId"></param>
        /// <returns></returns>
        Journey<T> CreateDepartureTransfer(Journey<T> buildOn, uint nextConnection,
            uint connDeparture, ulong connDepartureLoc,
            uint connArrival, ulong connArrLoc, uint tripId);


        /// <summary>
        /// Reverse add connection. Chains the transfer and connection to the given journey.
        /// However, this is the method to use for journeys which are built backwards in time 
        /// </summary>
        /// <param name="buildOn"></param>
        /// <param name="previousConnection"></param>
        /// <param name="connDeparture"></param>
        /// <param name="connDepartureLoc"></param>
        /// <param name="connArrival"></param>
        /// <param name="connArrLoc"></param>
        /// <param name="tripId"></param>
        /// <param name="connArrivalLoc"></param>
        /// <returns></returns>
        Journey<T> CreateArrivingTransfer(Journey<T> buildOn, uint previousConnection,
            uint connDeparture, ulong connDepartureLoc,
            uint connArrival, ulong connArrLoc, uint tripId);

    }
}