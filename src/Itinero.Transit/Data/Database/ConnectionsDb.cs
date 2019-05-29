using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.CompilerServices;
using Itinero.Transit.Algorithms.Sorting;
using Reminiscence;
using Reminiscence.Arrays;

// ReSharper disable RedundantAssignment

[assembly: InternalsVisibleTo("Itinero.Transit.Tests")]
[assembly: InternalsVisibleTo("Itinero.Transit.Tests.Benchmarks")]
[assembly: InternalsVisibleTo("Itinero.Transit.Tests.Functional")]

namespace Itinero.Transit.Data
{
    public class ConnectionsDb
    {
        // this is a connections database, it needs to support:
        // -> adding/removing connections by their global id.
        // -> an always sorted version by departure time.
        // -> an always sorted version by arrival time.

        // a connection can be queried by:
        // - a stable global id stored in a dictionary, this is a string.
        // - an id for internal usage, this can change when the connection is updated.
        // - by enumerating them sorted by either:
        //  -> departure time.
        //  -> arrival time.

        // a connection doesn't have:
        // - delay information, just add this to the departure time. the delay offset information is just 
        //   meta data and we can store it as such. Note that a Linked Data server already gives a departure/arrival time with delay included

        // this stores the connections data:
        // - stop1 (8bytes): the departure stop id.
        // - stop2 (8bytes): the arrival stop.
        // - departure time (4bytes): seconds since 1970-1-1: 4bytes.
        // - travel time in seconds (2bytes): the travel time in seconds, max 65535 (~18H). Should be fine until we incorporate the Orient Express
        private uint
            _nextInternalId; // the next empty position in the connection data array, divided by the connection size in bytes.

        private readonly ArrayBase<byte> _data; // the connection data.

        // this stores the connections global id index.
        private const int _globalIdHashSize = ushort.MaxValue;
        private readonly ArrayBase<uint> _globalIdPointersPerHash;

        // ReSharper disable once RedundantDefaultMemberInitializer
        private uint _globalIdLinkedListPointer = 0;
        private readonly ArrayBase<uint> _globalIdLinkedList;

        // the connections meta-data, its global, trip.
        private readonly ArrayBase<string> _globalIds; // holds the global ids.
        private readonly ArrayBase<uint> _tripIds; // holds the trip ids.

        private readonly ArrayBase<uint>
            _departureWindowPointers; // pointers to where the connection window blocks are stored.

        private readonly ArrayBase<uint>
            _departurePointers; // pointers to the connections sorted by departure time per window block.

        private uint _departurePointer;

        private readonly ArrayBase<uint>
            _arrivalWindowPointers; // pointers to where the connection window blocks are stored.

        private readonly ArrayBase<uint>
            _arrivalPointers; // pointers to the connections sorted by arrival time per window block.

        private uint _arrivalPointer;

        private const uint _noData = uint.MaxValue;
        private readonly long _windowSizeInSeconds; // one window per minute by default
        private const int _connectionSizeInBytes = 8 + 8 + 4 + 2 + 2 + 2 + 2;

        private uint _earliestDate = uint.MaxValue;
        private uint _latestDate = uint.MinValue;

        /// <summary>
        /// A runtime tag to distinguish between multiple Databases
        /// </summary>
        public readonly uint DatabaseId;

        /// <summary>
        /// Creates a new connections db.
        /// </summary>
        internal ConnectionsDb(uint databaseId, int windowSizeInSeconds = 60)
        {
            DatabaseId = databaseId;
            _windowSizeInSeconds = windowSizeInSeconds;

            // initialize the data array.
            _data = new MemoryArray<byte>(0);
            _nextInternalId = 0;

            // initialize the meta-data arrays.
            _globalIds = new MemoryArray<string>(0);
            _tripIds = new MemoryArray<uint>(0);
            _nextInternalId = 0;

            // initialize the ids reverse index.
            _globalIdPointersPerHash = new MemoryArray<uint>(_globalIdHashSize);
            for (var h = 0; h < _globalIdPointersPerHash.Length; h++)
            {
                _globalIdPointersPerHash[h] = _noData;
            }

            _globalIdLinkedList = new MemoryArray<uint>(0);

            // initialize the sorting data structures.
            _departureWindowPointers =
                new MemoryArray<uint>((long) Math.Ceiling(24d * 60 * 60 / _windowSizeInSeconds) * 2);
            _arrivalWindowPointers =
                new MemoryArray<uint>((long) Math.Ceiling(24d * 60 * 60 / _windowSizeInSeconds) * 2);
            for (var w = 0; w < _departureWindowPointers.Length / 2; w++)
            {
                _departureWindowPointers[w * 2 + 0] = _noData; // point to nothing.
                _departureWindowPointers[w * 2 + 1] = 0; // empty.
                _arrivalWindowPointers[w * 2 + 0] = _noData; // point to nothing.
                _arrivalWindowPointers[w * 2 + 1] = 0; // empty.
            }

            _departurePointers = new MemoryArray<uint>(0);
            _arrivalPointers = new MemoryArray<uint>(0);
        }

        private ConnectionsDb(uint databaseId, int windowSizeInSeconds, ArrayBase<byte> data, uint nextInternalId,
            ArrayBase<string> globalIds,
            ArrayBase<uint> tripIds, ArrayBase<uint> globalIdPointersPerHash, ArrayBase<uint> globalIdLinkedList,
            uint globalIdLinkedListPointer,
            ArrayBase<uint> departureWindowPointers, ArrayBase<uint> departurePointers, uint departurePointer,
            ArrayBase<uint> arrivalWindowPointers, ArrayBase<uint> arrivalPointers, uint arrivalPointer,
            uint earliestDate, uint latestDate)
        {
            _windowSizeInSeconds = windowSizeInSeconds;
            _data = data;
            _nextInternalId = nextInternalId;
            _globalIds = globalIds;
            _tripIds = tripIds;
            _globalIdLinkedListPointer = globalIdLinkedListPointer;
            _globalIdPointersPerHash = globalIdPointersPerHash;
            _globalIdLinkedList = globalIdLinkedList;

            _departureWindowPointers = departureWindowPointers;
            _departurePointers = departurePointers;
            _departurePointer = departurePointer;

            _arrivalWindowPointers = arrivalWindowPointers;
            _arrivalPointers = arrivalPointers;
            _arrivalPointer = arrivalPointer;

            _earliestDate = earliestDate;
            _latestDate = latestDate;
            DatabaseId = databaseId;
        }

        /// <summary>
        /// Adds a new connection.
        /// </summary>
        /// <param name="stop1">The first stop.</param>
        /// <param name="stop2">The last stop.</param>
        /// <param name="globalId">The global id.</param>
        /// <param name="departureTime">The departure time.</param>
        /// <param name="travelTime">The travel time in seconds.</param>
        /// <param name="departureDelay">The departure delay time in seconds.</param>
        /// <param name="arrivalDelay">The arrival delay time in seconds.</param>
        /// <param name="tripId">The trip id.</param>
        /// <param name="mode">The mode, indicates if getting on or off is supported</param>
        /// <returns>An internal id representing the connection in this transit db.</returns>
        internal uint AddOrUpdate(LocationId stop1,
            LocationId stop2, string globalId, ulong departureTime, ushort travelTime,
            ushort departureDelay, ushort arrivalDelay, TripId tripId, ushort mode)
        {
            var reader = GetReader();
            if (!reader.MoveTo(globalId))
            {
                // The connection is not yet added
                // We add the connection fresh
                return Add(stop1, stop2, globalId, departureTime, travelTime, departureDelay, arrivalDelay,
                    tripId,
                    mode);
            }

            // get all current data from reader.
            var currentTripId = reader.TripId;

            var currentDepartureTime = (uint) reader.DepartureTime;
            var currentArrivalTime = (uint) reader.ArrivalTime;
            var currentDepartureStop = reader.DepartureStop;
            var currentArrivalStop = reader.ArrivalStop;
            var currentDepartureDelay = reader.DepartureDelay;
            var currentArrivalDelay = reader.ArrivalDelay;

            var internalId = reader.Id;
            reader = null; // don't use the reader, we will start modifying the data from this point on.

            if (currentTripId.InternalId != tripId.InternalId)
            {
                // trip has changed, update it.
                SetTrip(internalId, tripId.InternalId);
            }

            var departureSeconds = (uint) departureTime;
            var arrivalSeconds = (uint) (departureTime + travelTime);


            if (currentDepartureTime == departureSeconds && currentArrivalTime == arrivalSeconds &&
                currentDepartureDelay == departureDelay && currentArrivalDelay == arrivalDelay &&
                currentDepartureStop.Equals(stop1) && currentArrivalStop.Equals(stop2))
            {
                // The important variables have stayed the same - no update needed
                return internalId;
            }


            // something changed - probably departure time due to delays. #SNCB
            // update the connection data.
            SetConnection(internalId, stop1, stop2, departureSeconds, travelTime, departureDelay, arrivalDelay, mode);

            if (currentDepartureTime != departureSeconds)
            {
                // update departure index if needed.
                var currentWindow = WindowFor(currentDepartureTime);
                var window = WindowFor(departureSeconds);

                if (currentWindow != window)
                {
                    // remove from current window.
                    RemoveDepartureIndex(internalId, currentWindow);

                    // add add again to new window.
                    AddDepartureIndex(internalId);
                }
                else
                {
                    // just resort the window.
                    SortDepartureWindow(window);
                }
            }

            if (currentArrivalTime != arrivalSeconds)
            {
                // update arrival index if needed.
                var currentWindow = WindowFor(currentArrivalTime);
                var window = WindowFor(arrivalSeconds);

                if (currentWindow != window)
                {
                    // remove from current window.
                    RemoveArrivalIndex(internalId, currentWindow);

                    // add add again to new window.
                    AddArrivalIndex(internalId);
                }
                else
                {
                    // just resort the window.
                    SortArrivalWindow(window);
                }
            }

            return internalId;
        }

        /// <summary>
        /// Adds a new connection.
        /// </summary>
        /// <param name="stop1">The first stop.</param>
        /// <param name="stop2">The last stop.</param>
        /// <param name="globalId">The global id.</param>
        /// <param name="departureTime">The departure time.</param>
        /// <param name="travelTime">The travel time in seconds.</param>
        /// <param name="departureDelay">The departure delay time in seconds.</param>
        /// <param name="arrivalDelay">The arrival delay time in seconds.</param>
        /// <param name="tripId">The trip id.</param>
        /// <param name="mode">The trip mode</param>
        /// <returns>An internal id representing the connection in this transit db.</returns>
        private uint Add(LocationId stop1,
            LocationId stop2, string globalId, ulong departureTime, ushort travelTime,
            ushort departureDelay,
            ushort arrivalDelay, TripId tripId, ushort mode)
        {
            // get the next internal id.
            var internalId = _nextInternalId;
            _nextInternalId++;

            // set this connection info int the data array.
            var departureSeconds = (uint) departureTime;
            SetConnection(internalId, stop1, stop2, departureSeconds, travelTime, departureDelay, arrivalDelay, mode);

            // check if this connections is the 'earliest' or 'latest' date-wise.
            var departureDateSeconds = DateTimeExtensions.ExtractDate(departureSeconds);
            if (departureDateSeconds < _earliestDate)
            {
                _earliestDate = (uint) departureDateSeconds;
            }

            if (departureDateSeconds > _latestDate)
            {
                _latestDate = (uint) departureDateSeconds;
            }

            // set trip and global ids.
            SetTrip(internalId, tripId.InternalId);
            SetGlobalId(internalId, globalId);

            // update departure time index.
            AddDepartureIndex(internalId);

            // update arrival time index.
            AddArrivalIndex(internalId);
            return internalId;
        }

        private void SetConnection(uint internalId, LocationId stop1,
            LocationId stop2, uint departure, ushort travelTime, ushort departureDelay,
            ushort arrivalDelay, ushort mode)
        {
            // make sure the data array is big enough.
            var dataPointer = internalId * _connectionSizeInBytes;
            while (_data.Length <= dataPointer + _connectionSizeInBytes)
            {
                var oldLength = _data.Length;
                _data.Resize(_data.Length + 1024);
                for (var i = oldLength; i < _data.Length; i++)
                {
                    _data[i] = byte.MaxValue;
                }
            }


            // Start saving the data
            var offset = 0;


            // Note that the database id of the location is _not_ saved
            var bytes = BitConverter.GetBytes(stop1.LocalTileId);
            for (var b = 0; b < 4; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 4;
            bytes = BitConverter.GetBytes(stop1.LocalId);
            for (var b = 0; b < 4; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 4;
            bytes = BitConverter.GetBytes(stop2.LocalTileId);
            for (var b = 0; b < 4; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 4;
            bytes = BitConverter.GetBytes(stop2.LocalId);
            for (var b = 0; b < 4; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 4;
            bytes = BitConverter.GetBytes(departure);
            for (var b = 0; b < 4; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 4;
            bytes = BitConverter.GetBytes(travelTime);
            for (var b = 0; b < 2; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 2;
            bytes = BitConverter.GetBytes(departureDelay);
            for (var b = 0; b < 2; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 2;
            bytes = BitConverter.GetBytes(arrivalDelay);
            for (var b = 0; b < 2; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 2;
            bytes = BitConverter.GetBytes(mode);
            for (var b = 0; b < 2; b++)
            {
                _data[dataPointer + offset + b] = bytes[b];
            }

            offset += 2;

            if (offset != _connectionSizeInBytes)
            {
                throw new ArgumentException($"Only wrote {offset} bytes while {_connectionSizeInBytes} expected");
            }
        }

        [Pure]
        private (LocationId departureLocation,
            LocationId arrivalLocation,
            ulong departureTime, ushort travelTime,
            ushort departureDelay, ushort arrivalDelay, ushort mode)
            GetConnection(uint internalId)
        {
            
            var dataPointer = internalId * _connectionSizeInBytes;
            if (_data.Length <= dataPointer + _connectionSizeInBytes)
            {
                return (LocationId.Invalid, LocationId.Invalid, uint.MaxValue, ushort.MaxValue,
                    ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
            }

            var bytes = new byte[_connectionSizeInBytes];
            for (var b = 0;
                b < _connectionSizeInBytes;
                b++)
            {
                bytes[b] = _data[dataPointer + b];
            }

            var offset = 0;

            var stop1 = (BitConverter.ToUInt32(bytes, 0),
                BitConverter.ToUInt32(bytes, 4));
            if (stop1.Item1 == uint.MaxValue &&
                stop1.Item1 == uint.MaxValue)
            {
                return (LocationId.Invalid, LocationId.Invalid,
                    uint.MaxValue, ushort.MaxValue,
                    ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
            }

            offset += 8;

            var stop2 = (BitConverter.ToUInt32(bytes, offset + 0),
                BitConverter.ToUInt32(bytes, offset + 4));

            offset += 8;
            var departureTime = BitConverter.ToUInt32(bytes, offset);
            offset += 4;
            var travelTime = BitConverter.ToUInt16(bytes, offset);
            offset += 2;
            var departureDelay = BitConverter.ToUInt16(bytes, offset);
            offset += 2;
            var arrivalDelay = BitConverter.ToUInt16(bytes, offset);
            offset += 2;
            var mode = BitConverter.ToUInt16(bytes, offset);
            offset += 2;
            return (new LocationId(DatabaseId, stop1.Item1, stop1.Item2),
                new LocationId(DatabaseId, stop2.Item1, stop2.Item2),
                departureTime, travelTime, departureDelay, arrivalDelay, mode);
        }

        [Pure]
        private uint GetConnectionDeparture(uint internalId)
        {
            var dataPointer = internalId * _connectionSizeInBytes;
            if (_data.Length <= dataPointer + _connectionSizeInBytes)
            {
                return uint.MaxValue;
            }

            var bytes = new byte[4];
            for (var b = 0; b < 4; b++)
            {
                bytes[b] = _data[dataPointer + 16 + b];
            }

            return BitConverter.ToUInt32(bytes, 0);
        }

        [Pure]
        private uint GetConnectionArrival(uint internalId)
        {
            var dataPointer = internalId * _connectionSizeInBytes;
            if (_data.Length <= dataPointer + _connectionSizeInBytes)
            {
                return uint.MaxValue;
            }

            var bytes = new byte[6];
            for (var b = 0; b < 6; b++)
            {
                bytes[b] = _data[dataPointer + 16 + b];
            }

            return BitConverter.ToUInt32(bytes, 0) +
                   BitConverter.ToUInt16(bytes, 4);
        }

        private void SetTrip(uint internalId, uint tripId)
        {
            while (_tripIds.Length <= internalId)
            {
                _tripIds.Resize(_tripIds.Length + 1024);
            }

            _tripIds[internalId] = tripId;
        }

        private void SetGlobalId(uint internalId, string globalId)
        {
            while (_globalIds.Length <= internalId)
            {
                _globalIds.Resize(_globalIds.Length + 1024);
            }

            _globalIds[internalId] = globalId;

            // add stop id to the index.
            var linkedListPointer = _globalIdLinkedListPointer;
            _globalIdLinkedListPointer += 2;
            while (_globalIdLinkedList.Length <= linkedListPointer)
            {
                _globalIdLinkedList.Resize(_globalIdLinkedList.Length + 1024);
            }

            var hash = Hash(globalId);

            _globalIdLinkedList[linkedListPointer + 0] = internalId;
            _globalIdLinkedList[linkedListPointer + 1] = _globalIdPointersPerHash[hash];
            _globalIdPointersPerHash[hash] = linkedListPointer + 0;
        }

        [Pure]
        private static uint Hash(string id)
        {
            // https://stackoverflow.com/questions/5154970/how-do-i-create-a-hashcode-in-net-c-for-a-string-that-is-safe-to-store-in-a
            unchecked
            {
                uint hash = 23;
                foreach (var c in id)
                {
                    hash = hash * 31 + c;
                }

                return hash % _globalIdHashSize;
            }
        }

        [Pure]
        private uint WindowFor(uint unixTime)
        {
            return (uint) Math.Floor(DateTimeExtensions.FromUnixTime(unixTime).TimeOfDay.TotalSeconds /
                                     _windowSizeInSeconds);
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool RemoveDepartureIndex(uint internalId, uint window)
        {
            var windowPointer = _departureWindowPointers[window * 2 + 0];
            if (_departureWindowPointers[window * 2 + 0] == _noData)
            {
                // nothing to remove.
                return false;
            }

            var windowSize = _departureWindowPointers[window * 2 + 1];

            // find entry.
            for (var p = windowPointer; p < windowPointer + windowSize; p++)
            {
                var id = _departurePointers[p];
                if (id != internalId) continue;

                // move all after one down.
                for (; p < windowPointer + windowSize - 1; p++)
                {
                    _departurePointers[p] = _departurePointers[p + 1];
                }

                // decrease window size.
                _departureWindowPointers[window * 2 + 1] = windowSize - 1;
                return true;
            }

            return false;
        }

        private void AddDepartureIndex(uint internalId)
        {
            // determine window.
            var departure = GetConnectionDeparture(internalId);
            var window = WindowFor(departure);
            var nextEmpty = uint.MaxValue;
            var windowPointer = _departureWindowPointers[window * 2 + 0];
            if (_departureWindowPointers[window * 2 + 0] == _noData)
            {
                // add a new window.
                nextEmpty = _departurePointer;
                _departurePointer += 1;

                // update the window.
                _departureWindowPointers[window * 2 + 0] = nextEmpty;
                _departureWindowPointers[window * 2 + 1] = 1;
            }
            else
            {
                // there is already data in the window.
                var windowSize = _departureWindowPointers[window * 2 + 1];
                if ((windowSize & (windowSize - 1)) == 0)
                {
                    // power of 2, time to increase the window capacity.
                    // allocate new space.
                    var newWindowPointer = _departurePointer;
                    _departurePointer += windowSize * 2;

                    // copy over data.
                    while (_departurePointers.Length <= _departurePointer)
                    {
                        _departurePointers.Resize(_departurePointers.Length + 1024);
                    }

                    for (var c = 0; c < windowSize; c++)
                    {
                        _departurePointers[newWindowPointer + c] =
                            _departurePointers[windowPointer + c];
                    }

                    windowPointer = newWindowPointer;
                    _departureWindowPointers[window * 2 + 0] = newWindowPointer;
                }

                // increase size.
                _departureWindowPointers[window * 2 + 1] = windowSize + 1;
                nextEmpty = windowPointer + windowSize;
            }

            // set the data.
            while (_departurePointers.Length <= nextEmpty)
            {
                _departurePointers.Resize(_departurePointers.Length + 1024);
            }

            _departurePointers[nextEmpty] = internalId;

            // sort the window.
            SortDepartureWindow(window);
        }

        private void SortDepartureWindow(uint window)
        {
            var windowPointer = _departureWindowPointers[window * 2 + 0];
            var windowSize = _departureWindowPointers[window * 2 + 1];
            QuickSort.Sort(i => GetConnectionDeparture(_departurePointers[i]),
                (i1, i2) =>
                {
                    var temp = _departurePointers[i1];
                    _departurePointers[i1] = _departurePointers[i2];
                    _departurePointers[i2] = temp;
                }, windowPointer, windowPointer + windowSize - 1);
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool RemoveArrivalIndex(uint internalId, uint window)
        {
            var windowPointer = _arrivalWindowPointers[window * 2 + 0];
            if (_arrivalWindowPointers[window * 2 + 0] == _noData)
            {
                // nothing to remove.
                return false;
            }

            var windowSize = _arrivalWindowPointers[window * 2 + 1];

            // find entry.
            for (var p = windowPointer; p < windowPointer + windowSize; p++)
            {
                var id = _arrivalPointers[p];
                if (id != internalId) continue;

                // move all after one down.
                for (; p < windowPointer + windowSize - 1; p++)
                {
                    _arrivalPointers[p] = _arrivalPointers[p + 1];
                }

                // decrease window size.
                _arrivalWindowPointers[window * 2 + 1] = windowSize - 1;
                return true;
            }

            return false;
        }

        private void AddArrivalIndex(uint internalId)
        {
            // determine window.
            var arrival = GetConnectionArrival(internalId);
            var window = (uint) Math.Floor(DateTimeExtensions.FromUnixTime(arrival).TimeOfDay.TotalSeconds /
                                           _windowSizeInSeconds);
            var nextEmpty = uint.MaxValue;
            var windowPointer = _arrivalWindowPointers[window * 2 + 0];
            if (_arrivalWindowPointers[window * 2 + 0] == _noData)
            {
                // add a new window.
                nextEmpty = _arrivalPointer;
                _arrivalPointer += 1;

                // update the window.
                _arrivalWindowPointers[window * 2 + 0] = nextEmpty;
                _arrivalWindowPointers[window * 2 + 1] = 1;
            }
            else
            {
                // there is already data in the window.
                var windowSize = _arrivalWindowPointers[window * 2 + 1];
                if ((windowSize & (windowSize - 1)) == 0)
                {
                    // power of 2, time to increase the window capacity.
                    // allocate new space.
                    var newWindowPointer = _arrivalPointer;
                    _arrivalPointer += windowSize * 2;

                    // copy over data.
                    while (_arrivalPointers.Length <= _arrivalPointer)
                    {
                        _arrivalPointers.Resize(_arrivalPointers.Length + 1024);
                    }

                    for (var c = 0; c < windowSize; c++)
                    {
                        _arrivalPointers[newWindowPointer + c] =
                            _arrivalPointers[windowPointer + c];
                    }

                    windowPointer = newWindowPointer;
                    _arrivalWindowPointers[window * 2 + 0] = newWindowPointer;
                }

                // increase size.
                _arrivalWindowPointers[window * 2 + 1] = windowSize + 1;
                nextEmpty = windowPointer + windowSize;
            }

            // set the data.
            while (_arrivalPointers.Length <= nextEmpty)
            {
                _arrivalPointers.Resize(_arrivalPointers.Length + 1024);
            }

            _arrivalPointers[nextEmpty] = internalId;

            // sort the window.
            SortArrivalWindow(window);
        }

        private void SortArrivalWindow(uint window)
        {
            var windowPointer = _arrivalWindowPointers[window * 2 + 0];
            var windowSize = _arrivalWindowPointers[window * 2 + 1];
            QuickSort.Sort(i => GetConnectionArrival(_arrivalPointers[i]),
                (i1, i2) =>
                {
                    var temp = _arrivalPointers[i1];
                    _arrivalPointers[i1] = _arrivalPointers[i2];
                    _arrivalPointers[i2] = temp;
                }, windowPointer, windowPointer + windowSize - 1);
        }

        /// <summary>
        /// Returns a deep in-memory copy.
        /// </summary>
        /// <returns></returns>
        [Pure]
        public ConnectionsDb Clone()
        {
            var data = new MemoryArray<byte>(_data.Length);
            data.CopyFrom(_data, _data.Length);
            var globalIds = new MemoryArray<string>(_globalIds.Length);
            globalIds.CopyFrom(_globalIds, _globalIds.Length);
            var tripIds = new MemoryArray<uint>(_tripIds.Length);
            tripIds.CopyFrom(_tripIds, _tripIds.Length);
            var globalIdPointersPerHash = new MemoryArray<uint>(_globalIdPointersPerHash.Length);
            globalIdPointersPerHash.CopyFrom(_globalIdPointersPerHash, _globalIdPointersPerHash.Length);
            var globalIdLinkedList = new MemoryArray<uint>(_globalIdLinkedList.Length);
            globalIdLinkedList.CopyFrom(_globalIdLinkedList, _globalIdLinkedList.Length);
            var departureWindowPointers = new MemoryArray<uint>(_departureWindowPointers.Length);
            departureWindowPointers.CopyFrom(_departureWindowPointers, _departureWindowPointers.Length);
            var departurePointers = new MemoryArray<uint>(_departurePointers.Length);
            departurePointers.CopyFrom(_departurePointers, _departurePointers.Length);
            var arrivalWindowPointers = new MemoryArray<uint>(_arrivalWindowPointers.Length);
            arrivalWindowPointers.CopyFrom(_arrivalWindowPointers, _arrivalWindowPointers.Length);
            var arrivalPointers = new MemoryArray<uint>(_arrivalPointers.Length);
            arrivalPointers.CopyFrom(_arrivalPointers, _arrivalPointers.Length);
            return new ConnectionsDb(
                DatabaseId,
                (int) _windowSizeInSeconds, data, _nextInternalId, globalIds, tripIds,
                globalIdPointersPerHash, globalIdLinkedList,
                _globalIdLinkedListPointer, departureWindowPointers, departurePointers, _departurePointer,
                arrivalWindowPointers, arrivalPointers, _arrivalPointer,
                _earliestDate, _latestDate);
        }

        internal long WriteTo(Stream stream)
        {
            var length = 0L;

// write version #.
            stream.WriteByte(1);
            length++;
            length += _data.CopyToWithSize(stream);
            length += _globalIds.CopyToWithSize(stream);
            length += _tripIds.CopyToWithSize(stream);
            length += _globalIdPointersPerHash.CopyToWithSize(stream);
            length += _globalIdLinkedList.CopyToWithSize(stream);
            var bytes = BitConverter.GetBytes(_globalIdLinkedListPointer);
            stream.Write(bytes, 0, 4);
            length += 4;
            length += _departureWindowPointers.CopyToWithSize(stream);
            length += _departurePointers.CopyToWithSize(stream);
            bytes = BitConverter.GetBytes(_departurePointer);
            stream.Write(bytes, 0, 4);
            length += 4;
            length += _arrivalWindowPointers.CopyToWithSize(stream);
            length += _arrivalPointers.CopyToWithSize(stream);
            bytes = BitConverter.GetBytes(_arrivalPointer);
            stream.Write(bytes, 0, 4);
            length += 4;
            bytes = BitConverter.GetBytes(_windowSizeInSeconds);
            stream.Write(bytes, 0, 8);
            length += 8;
            bytes = BitConverter.GetBytes(_nextInternalId);
            stream.Write(bytes, 0, 4);
            length += 4;
            bytes = BitConverter.GetBytes(_earliestDate);
            stream.Write(bytes, 0, 4);
            length += 4;
            bytes = BitConverter.GetBytes(_latestDate);
            stream.Write(bytes, 0, 4);
            length += 4;
            return length;
        }

        [Pure]
        internal static ConnectionsDb ReadFrom(Stream stream, uint databaseId)
        {
            var buffer = new byte[8];
            var version = stream.ReadByte();
            if (version != 1)
                throw new InvalidDataException($"Cannot read {nameof(ConnectionsDb)}, invalid version #.");
            var data = MemoryArray<byte>.CopyFromWithSize(stream);
            var globalIds = MemoryArray<string>.CopyFromWithSize(stream);
            var tripIds = MemoryArray<uint>.CopyFromWithSize(stream);
            var globalIdPointersPerHash = MemoryArray<uint>.CopyFromWithSize(stream);
            var globalIdLinkedList = MemoryArray<uint>.CopyFromWithSize(stream);
            stream.Read(buffer, 0, 4);
            var globalIdLinkedListPointer = BitConverter.ToUInt32(buffer, 0);
            var departureWindowPointers = MemoryArray<uint>.CopyFromWithSize(stream);
            var departurePointers = MemoryArray<uint>.CopyFromWithSize(stream);
            stream.Read(buffer, 0, 4);
            var departurePointer = BitConverter.ToUInt32(buffer, 0);
            var arrivalWindowPointers = MemoryArray<uint>.CopyFromWithSize(stream);
            var arrivalPointers = MemoryArray<uint>.CopyFromWithSize(stream);
            stream.Read(buffer, 0, 4);
            var arrivalPointer = BitConverter.ToUInt32(buffer, 0);
            stream.Read(buffer, 0, 8);
            var windowSizeInSeconds = BitConverter.ToInt64(buffer, 0);
            stream.Read(buffer, 0, 4);
            var nextInternalId = BitConverter.ToUInt32(buffer, 0);
            stream.Read(buffer, 0, 4);
            var earliestDate = BitConverter.ToUInt32(buffer, 0);
            stream.Read(buffer, 0, 4);
            var latestDate = BitConverter.ToUInt32(buffer, 0);
            return new ConnectionsDb(
                databaseId, // Database ID's are not serialized
                (int) windowSizeInSeconds, data, nextInternalId, globalIds, tripIds,
                globalIdPointersPerHash, globalIdLinkedList, globalIdLinkedListPointer,
                departureWindowPointers, departurePointers, departurePointer,
                arrivalWindowPointers, arrivalPointers, arrivalPointer,
                earliestDate, latestDate);
        }

        /// <summary>
        /// Gets a reader.
        /// </summary>
        /// <returns></returns>
        [Pure]
        public ConnectionsDbReader GetReader()
        {
            return new ConnectionsDbReader(this);
        }

        /// <summary>
        /// A connections DB reader is an object which allows accessing properties of a single connection contained in the DB
        /// </summary>
        public class ConnectionsDbReader : IConnectionReader
        {
            private readonly ConnectionsDb _db;

            internal ConnectionsDbReader(ConnectionsDb db)
            {
                _db = db;
            }

            private uint _internalId;
            private LocationId _departureStop;
            private LocationId _arrivalStop;
            private ulong _departureTime, _arrivalTime;
            private ushort _travelTime, _departureDelay, _arrivalDelay, _mode;

            /// <summary>
            /// Gets the global id.
            /// </summary>
            [Pure]
            public string GlobalId => _db._globalIds[_internalId];

            /// <inheritdoc />
            [Pure]
            public TripId TripId => new TripId(_db.DatabaseId, _db._tripIds[_internalId]);

            /// <inheritdoc />
            [Pure]
            public LocationId DepartureStop => _departureStop;

            /// <inheritdoc />
            [Pure]
            public LocationId ArrivalStop => _arrivalStop;

            /// <inheritdoc />
            [Pure]
            public ulong DepartureTime => _departureTime;

            /// <inheritdoc />
            [Pure]
            public ushort TravelTime => _travelTime;

            /// <inheritdoc />
            [Pure]
            public ushort DepartureDelay => _departureDelay;

            /// <inheritdoc />
            [Pure]
            public ushort ArrivalDelay => _arrivalDelay;

            /// <inheritdoc />
            [Pure]
            public ulong ArrivalTime => _arrivalTime;

            /// <inheritdoc />
            [Pure]
            public uint Id => _internalId;

            /// <inheritdoc />
            [Pure]
            public ushort Mode => _mode;

            /// <summary>
            /// Moves this reader to the connection with the given internal id.
            /// </summary>
            /// <param name="internalId">The internal id.</param>
            /// <returns>True if the connection was found and there is data.</returns>
            public bool MoveTo(uint internalId)
            {
                var details = _db.GetConnection(internalId);
                if (details.departureLocation.LocalTileId == uint.MaxValue)
                {
// no data.
                    return false;
                }

                _internalId = internalId;
                _departureStop = details.departureLocation;
                _arrivalStop = details.arrivalLocation;
                _departureTime = details.departureTime;
                _travelTime = details.travelTime;
                _arrivalTime = details.departureTime + details.travelTime;
                _departureDelay = details.departureDelay;
                _arrivalDelay = details.arrivalDelay;
                _mode = details.mode;
                return true;
            }

            public bool MoveNext()
            {
                return MoveTo(_internalId + 1);
            }

            public void Reset()
            {
                MoveTo(0);
            }

            public bool MoveTo(uint dbId, uint connectionId)
            {
                // ReSharper disable once ConvertIfStatementToReturnStatement
                if (_db.DatabaseId != dbId)
                {
                    return false;
                }

                return MoveTo(connectionId);
            }

            /// <summary>
            /// Moves this reader to the connection with the given global id.
            /// </summary>
            /// <param name="globalId">The global id.</param>
            /// <returns>True if the connection was found and there is data.</returns>
            public bool MoveTo(string globalId)
            {
                var hash = Hash(globalId);
                var pointer = _db._globalIdPointersPerHash[hash];
                while (pointer != _noData)
                {
                    var internalId = _db._globalIdLinkedList[pointer + 0];
                    if (MoveTo(internalId))
                    {
                        var potentialMatch = GlobalId;
                        if (potentialMatch == globalId)
                        {
                            return true;
                        }
                    }

                    pointer = _db._globalIdLinkedList[pointer + 1];
                }

                return false;
            }
        }

        /// <summary>
        /// Gets an enumerator enumerating connections sorted by their departure time.
        /// </summary>
        /// <returns>The departure enumerator.</returns>
        [Pure]
        public DepartureEnumerator GetDepartureEnumerator()
        {
            return new DepartureEnumerator(this);
        }

        /// <summary>
        /// A enumerator by departure.
        /// </summary>
        public class DepartureEnumerator : IConnectionEnumerator
        {
            private readonly ConnectionsDb _db;
            private readonly ConnectionsDbReader _reader;

            internal DepartureEnumerator(ConnectionsDb db)
            {
                _db = db;
                _reader = _db.GetReader();
                _date = uint.MaxValue;
            }

            private uint _window = uint.MaxValue;
            private long _windowPosition = long.MaxValue;
            private uint _windowPointer = uint.MaxValue;
            private uint _windowSize = uint.MaxValue;
            private uint _date;

            /// <summary>
            /// Resets the enumerator.
            /// </summary>
            public void Reset()
            {
                ResetIgnoreDate();
                _date = uint.MaxValue;
            }

            /// <summary>
            /// Resets the enumerator.
            /// </summary>
            private void ResetIgnoreDate()
            {
                _window = uint.MaxValue;
                _windowPosition = uint.MaxValue;
                _windowSize = uint.MaxValue;
            }

            /// <summary>
            /// Moves to the next connection.
            /// </summary>
            /// <param name="dateTime">The date time to move to, gives the first connection after the given date time.</param>
            /// <returns>True if there is data.</returns>
            public bool MoveNext(DateTime? dateTime = null)
            {
                if (dateTime != null)
                {
                    // move to the given date.
                    _date = (uint) DateTimeExtensions.ExtractDate(dateTime.Value.ToUnixTime());
                }

                if (_date == uint.MaxValue)
                {
                    if (_db._earliestDate == uint.MaxValue) return false;
                    _date = _db._earliestDate;
                }

                while (true)
                {
                    if (!MoveNextIgnoreDate(dateTime))
                    {
                        // move to next date. 
                        _date = (uint) DateTimeExtensions.AddDay(_date);
                        if (_date > _db._latestDate)
                        {
                            return false;
                        }

                        // reset enumerator.
                        ResetIgnoreDate();
                    }
                    else
                    {
                        if (_date == DateTimeExtensions.ExtractDate(_reader.DepartureTime))
                        {
                            return true;
                        }
                    }

                    dateTime = null; // no use trying this again.
                }
            }

            /// <summary>
            /// Moves this enumerator to the next connection ignoring the data component.
            /// </summary>
            /// <returns></returns>
            private bool MoveNextIgnoreDate(DateTime? dateTime = null)
            {
                if (dateTime != null)
                {
                    // move directly to the actual window.
                    _window = (uint) Math.Floor(dateTime.Value.TimeOfDay.TotalSeconds /
                                                _db._windowSizeInSeconds);
                    _windowSize = _db._departureWindowPointers[_window * 2 + 1];
                    _windowPosition = 0;
                    _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                    if (_windowSize > 0)
                    {
                        _reader.MoveTo(
                            _db._departurePointers[
                                _windowPointer +
                                _windowPosition]); // keep moving next until we reach a departure time after the given date time.
                        var unixTime = dateTime.Value.ToUnixTime();
                        while (unixTime > _reader.DepartureTime)
                        {
                            // move next.
                            if (!MoveNextIgnoreDate())
                            {
                                // connection after this departure time doesn't exist.
                                return false;
                            }
                        }

                        return true;
                    }
                }

                if (_window == uint.MaxValue)
                {
                    // no data, find first window with data.
                    for (uint w = 0; w < _db._departureWindowPointers.Length / 2; w++)
                    {
                        var windowSize = _db._departureWindowPointers[w * 2 + 1];
                        if (windowSize <= 0) continue;
                        _window = w;
                        _windowSize = windowSize;
                        break;
                    }

                    if (_window == uint.MaxValue)
                    {
                        // no window with data found.
                        return false;
                    }

                    // window changed.
                    _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                    _windowPosition = 0;
                }
                else
                {
                    // there is an active window, try to move to the next window.
                    if (_windowPosition + 1 >= _windowSize)
                    {
                        // move to next window.
                        var w = _window + 1;
                        _window = uint.MaxValue;
                        for (; w < _db._departureWindowPointers.Length / 2; w++)
                        {
                            var windowSize = _db._departureWindowPointers[w * 2 + 1];
                            if (windowSize <= 0) continue;
                            _window = w;
                            _windowSize = windowSize;
                            break;
                        }

                        if (_window == uint.MaxValue)
                        {
                            // no more windows with data found.
                            return false;
                        }

                        // window changed.
                        _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                        _windowPosition = 0;
                    }
                    else
                    {
                        // move to the next connection.
                        _windowPosition++;
                    }
                }

                // move the reader to the correct location.
                _reader.MoveTo(_db._departurePointers[_windowPointer + _windowPosition]);
                if (dateTime != null)
                {
                    // keep move next until we reach a departure time after the given date time.
                    var unixTime = dateTime.Value.ToUnixTime();
                    while (unixTime > _reader.DepartureTime)
                    {
                        // move next.
                        if (!MoveNextIgnoreDate())
                        {
                            // connection after this departure time doesn't exist.
                            return false;
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// Moves to the previous connection.
            /// </summary>
            /// <param name="dateTime">The date time to move to, gives the first connection before the given date time.</param>
            /// <returns>True if there is data.</returns>
            public bool MovePrevious(DateTime? dateTime = null)
            {
                if (dateTime != null)
                {
                    // move to the given date.
                    _date = (uint) DateTimeExtensions.ExtractDate(dateTime.Value.ToUnixTime());
                }

                if (_date == uint.MaxValue)
                {
                    if (_db._latestDate == uint.MaxValue) return false;
                    _date = _db._latestDate;
                }

                while (true)
                {
                    if (!MovePreviousIgnoreDate(dateTime))
                    {
                        if (_date <= _db._earliestDate)
                        {
                            // In the very corner case that we are on the start-of epoch day
                            return false;
                        }
                        
                        // move to previous date. 
                        _date = (uint) DateTimeExtensions.RemoveDay(_date);
                        if (_date < _db._earliestDate)
                        {
                            return false;
                        }

                        // reset enumerator.
                        ResetIgnoreDate();
                    }
                    else
                    {
                        if (_date == DateTimeExtensions.ExtractDate(_reader.DepartureTime))
                        {
                            return true;
                        }
                    }

                    dateTime = null; // no use trying this again.
                }
            }

            /// <summary>
            /// Moves this enumerator to the previous connection ignoring the data component.
            /// </summary>
            /// <returns></returns>
            private bool MovePreviousIgnoreDate(DateTime? dateTime = null)
            {
                if (dateTime != null)
                {
                    // move directly to the actual window.
                    _window = (uint) Math.Floor(dateTime.Value.TimeOfDay.TotalSeconds /
                                                _db._windowSizeInSeconds);
                    _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                    _windowSize = _db._departureWindowPointers[_window * 2 + 1];
                    _windowPosition = _windowSize - 1;
                }

                if (_window == uint.MaxValue)
                {
                    // no data, find last window with data.
                    for (var w = _db._departureWindowPointers.Length / 2 - 1; w >= 0; w--)
                    {
                        var windowSize = _db._departureWindowPointers[w * 2 + 1];
                        if (windowSize <= 0) continue;
                        _window = (uint) w;
                        _windowSize = windowSize;
                        break;
                    }

                    // window changed.
                    _windowPosition = _windowSize - 1;
                    _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                }
                else
                {
                    // there is an active window, try to move to the previous window.
                    if (_windowPosition <= 0 ||
                        _windowPointer == uint.MaxValue)
                    {
                        // move to previous window.
                        var w = (long) _window - 1;
                        _window = uint.MaxValue;
                        for (; w >= 0; w--)
                        {
                            var windowSize = _db._departureWindowPointers[w * 2 + 1];
                            if (windowSize <= 0) continue;
                            _window = (uint) w;
                            _windowSize = windowSize;
                            break;
                        }

                        if (_window == uint.MaxValue)
                        {
                            // no more windows with data found.
                            return false;
                        }

                        // window changed.
                        _windowPosition = _windowSize - 1;
                        _windowPointer = _db._departureWindowPointers[_window * 2 + 0];
                        if (_windowPointer == uint.MaxValue)
                        {
                            //Console.WriteLine($"{_date}:{_window}-{_windowPosition}: Invalid pointer here, there is supposed to be one here.");
                            _windowPosition = 0;
                            return MovePreviousIgnoreDate();
                        }
                    }
                    else
                    {
                        // move to the next connection.
                        _windowPosition--;
                    }
                }

                // move the reader to the correct location.
                _reader.MoveTo(_db._departurePointers[_windowPointer + _windowPosition]);
                if (dateTime != null)
                {
                    // keep move next until we reach a departure time after the given date time.
                    var unixTime = dateTime.Value.ToUnixTime();
                    while (unixTime < _reader.DepartureTime)
                    {
                        // move next.
                        if (!MovePreviousIgnoreDate())
                        {
                            // connection after this departure time doesn't exist.
                            return false;
                        }
                    }
                }

                return true;
            }

            /// <inheritdoc />
            // ReSharper disable once UnusedMember.Global
            [Pure]
            public LocationId DepartureStop => _reader.DepartureStop;

            /// <inheritdoc />
            // ReSharper disable once UnusedMember.Global
            [Pure]
            public LocationId ArrivalStop => _reader.ArrivalStop;

            /// <inheritdoc />

            [Pure]
            public ulong DepartureTime => _reader.DepartureTime;

            /// <inheritdoc />

            [Pure]
            public ulong ArrivalTime => _reader.ArrivalTime;

            /// <inheritdoc />

            [Pure]
            public ushort TravelTime => _reader.TravelTime;

            /// <inheritdoc />

            [Pure]
            public ushort DepartureDelay => _reader.DepartureDelay;

            /// <inheritdoc />

            [Pure]
            public ushort ArrivalDelay => _reader.ArrivalDelay;

            /// <inheritdoc />

            [Pure]
            public uint Id => _reader.Id;

            /// <inheritdoc />

            [Pure]
            public string GlobalId => _reader.GlobalId;

            /// <inheritdoc />
            public TripId TripId => _reader.TripId;

            /// <inheritdoc />
            public ushort Mode => _reader.Mode;
        }
    }
}