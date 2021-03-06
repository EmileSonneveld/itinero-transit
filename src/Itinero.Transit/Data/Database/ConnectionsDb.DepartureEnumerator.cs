using System.Diagnostics.Contracts;
using Itinero.Transit.Data.Core;

namespace Itinero.Transit.Data
{
    public partial class ConnectionsDb
    {
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
        /// The departureEnumerator uses the departureWindowIndexes to crawl through the connectionsDB
        ///
        /// The object itself is pretty stateless, all the relevant state is saved in the 'DepartureTimeIndex'.
        ///
        /// 
        /// </summary>
        public class DepartureEnumerator : IConnectionEnumerator
        {
            private readonly ConnectionsDb _connectionsDb;


            /// <summary>
            /// The current DateTime
            /// This implies which window we want to use for the IndexInWindow
            /// </summary>
            public ulong CurrentDateTime { get; private set; }

            /// <summary>
            /// Where are we in the current window?
            /// </summary>
            private uint _indexInWindow;

            /// <summary>
            /// What is the corresponding internal id?
            /// </summary>
            private uint _connectionInternalId = uint.MaxValue;


            /// <summary>
            /// A single window can contain chunks which are one cycle (e.g. one day) away from each other.
            /// E.g. a window can contain departure dates as following (if windowSize = 1 minute and number of windows = 24*60):
            ///     [ (yesterday 10:00), (today: 10:00), (today: 10:00), (tomorrow: 10:00), ...]
            ///
            /// When enumerating, this means that we must be able to suddenly jump into the middle of the window, if yesterday has already been enumerated
            ///
            /// This array keeps track of that
            /// 
            /// </summary>
            private readonly uint[] _alreadyUsed;


            public DepartureEnumerator(
                ConnectionsDb connectionsDb)
            {
                _connectionsDb = connectionsDb;
                _indexInWindow = 0;

                _alreadyUsed = new uint[_connectionsDb.NumberOfWindows];
            }

            public void MoveTo(ulong dateTime)
            {
                // An initial data pointer: we are given the right window at index 0
                // Next will figure out if that data actually exist and run through the windows until it finds an actual connection
                CurrentDateTime = dateTime;
                _indexInWindow = uint.MaxValue;
                _connectionInternalId = uint.MaxValue;
                for (var i = 0; i < _connectionsDb.NumberOfWindows; i++)
                {
                    _alreadyUsed[i] = uint.MaxValue;
                }
            }


            /// <summary>
            /// Gives a DTI where the CurrentDateTime points to the window just after the current window
            /// </summary>
            /// <returns></returns>
            private void NextWindow()
            {
                // We increase the dateTime so that it is exactly the start of the next window
                CurrentDateTime =
                    ((CurrentDateTime / _connectionsDb.WindowSizeInSeconds) + 1) *
                    _connectionsDb.WindowSizeInSeconds;
                _indexInWindow = _alreadyUsed[_connectionsDb.WindowFor(CurrentDateTime)];
            }


            /// <summary>
            /// Searches the index of the first connection within the structure, so that:
            /// this connection is the first to depart at (or later then) the given dateTime, in the given window
            /// </summary>
            /// <returns></returns>
            private uint BinarySearch(uint window, ulong dateTime)
            {
                // https://en.wikipedia.org/wiki/Binary_search_algorithm#Procedure_for_finding_the_leftmost_element
                var windowPointer = _connectionsDb.DepartureWindowPointers[window * 2 + 0];
                var windowSize = _connectionsDb.DepartureWindowPointers[window * 2 + 1];

                if (windowSize <= 1)
                {
                    return 0;
                }


                uint left = 0;
                var right = windowSize - 1;
                while (left < right)
                {
                    var m = (left + right) / 2;
                    var connId = _connectionsDb.DeparturePointers[windowPointer + m];
                    var connDepTime = _connectionsDb.GetConnectionDeparture(connId);
                    if (connDepTime < dateTime)
                    {
                        left = m + 1;
                    }
                    else
                    {
                        right = m;
                    }
                }

                return left;
            }

            public bool MoveNext()
            {
                // We put everything in one big loop, to avoid tail recursion
                // Note that the loop is written as a GOTO-label
                // Yes, you read that right! A GOTO
                hasNext:

                if (CurrentDateTime > _connectionsDb.LatestDate)
                {
                    // Nope, depleted!
                    return false;
                }

                if (_indexInWindow == uint.MaxValue - 1)
                {
                    // We got here by a NextWindow, but that window has already been depleted
                    NextWindow();
                    goto hasNext;
                }

                // ALL RIGHT FOLKS
                // Time to figure things out!
                // We need the connection in the window for the given datetime, at the given index
                // IF it does not exist, we go to the next existing connection

                // For starters, what is the wanted window and does it exist?
                var window = _connectionsDb.WindowFor(CurrentDateTime);


                var windowPointer = _connectionsDb.DepartureWindowPointers[window * 2 + 0];

                if (windowPointer == _noData)
                {
                    // Nope, that window is not there!

                    // There might be a next window available
                    NextWindow();

                    goto hasNext; // === return HasNext();
                }

                if (_indexInWindow == uint.MaxValue)
                {
                    // Needs some initialization
                    // We search the first element in the window in the current dateTime
                    _indexInWindow = BinarySearch(window, CurrentDateTime);
                    _alreadyUsed[window] = _indexInWindow;
                }


                ulong depTime;
                do
                {
                    // Ok, so we at least have te right window.
                    // Lets see if we can retrieve the connection itself
                    // For that, we should check if the index is within the window size
                    var windowSize = _connectionsDb.DepartureWindowPointers[window * 2 + 1];

                    if (_indexInWindow >= windowSize)
                    {
                        // Ahh, the good old 'IndexOutOfBounds'
                        // In other words, this window is simply depleted
                        // We attempt to use the next window
                        _alreadyUsed[window] = uint.MaxValue - 1;
                        NextWindow();
                        goto hasNext; // === return HasNext();
                    }


                    // Ok, so we have the right window and the connection exists! Hooray!
                    _connectionInternalId = _connectionsDb.DeparturePointers[windowPointer + _indexInWindow];
                    // and get the departure time of the index because...
                    depTime = _connectionsDb.GetConnectionDeparture(_connectionInternalId);

                    // ... the current connection could fall _too soon_
                    // Either just because of the specified dateTime
                    // or because a window has connections from multiple days
                    // If that happens, we just restart everything:
                    
                    
                    // We update the DTI
                    _indexInWindow++;
                } while (depTime < CurrentDateTime);


                // If we end up here, the desired connection exists and its departure time falls after the specified time
                // However, depTime might have shot to far
                // For this, we check that the depTime still falls within the current range
                if (depTime - CurrentDateTime > _connectionsDb.WindowSizeInSeconds)
                {
                    // Nope, we made a cycle jump
                    _alreadyUsed[window] = _indexInWindow - 1;
                    NextWindow();
                    goto hasNext;
                }

                // current.WindowIndex points to the next needed element in the window
                // And current.ConnectionInternalId is set
                // So, we are pretty much done

                // Only thing that rests us is top update the current.DepartureTime, so that the caller knows when this connection is leaving
                // This is used by the aggregator
                CurrentDateTime = depTime;
                return true;
            }

            private uint BinarySearchLast(uint window, ulong dateTime)
            {
                //https://en.wikipedia.org/wiki/Binary_search_algorithm#Procedure_for_finding_the_rightmost_element
                var windowPointer = _connectionsDb.DepartureWindowPointers[window * 2 + 0];
                var windowSize = _connectionsDb.DepartureWindowPointers[window * 2 + 1];

                if (windowSize <= 1)
                {
                    return 0;
                }


                uint left = 0;
                var right = windowSize;
                while (left < right)
                {
                    var m = (left + right) / 2;
                    var connId = _connectionsDb.DeparturePointers[windowPointer + m];
                    var connDepTime = _connectionsDb.GetConnectionDeparture(connId);
                    if (connDepTime <= dateTime)
                    {
                        left = m + 1;
                    }
                    else
                    {
                        right = m;
                    }
                }

                return left - 1;
            }

            private void PreviousWindow()
            {
                // We decrease the dateTime so that it is exactly the end of the previous window

                // First, put it at the start of the current window
                CurrentDateTime =
                    (CurrentDateTime / _connectionsDb.WindowSizeInSeconds) *
                    _connectionsDb.WindowSizeInSeconds;
                if (CurrentDateTime > 0)
                {
                    // And decrease by one - but don't underflow
                    CurrentDateTime--;
                }

                // And we should point to its last element

                _indexInWindow = _alreadyUsed[_connectionsDb.WindowFor(CurrentDateTime)];
            }

            public bool MovePrevious()
            {
                hasPrevious:
                if (CurrentDateTime == 0)
                {
                    return false;
                }


                // ALL RIGHT FOLKS
                // Time to figure things out!
                // We need the connection in the window for the given datetime, at the given index
                // IF it does not exist, we go to the next existing connection

                // For starters, what is the wanted window and does it exist?
                var window = _connectionsDb.WindowFor(CurrentDateTime);
                var windowPointer = _connectionsDb.DepartureWindowPointers[window * 2 + 0];

                if (windowPointer == _noData)
                {
                    // Nope, that window is not there!

                    // Either this window just happens to be empty
                    // Or we are at the end of our connections database

                    if (CurrentDateTime < _connectionsDb.EarliestDate)
                    {
                        // Yep, the database is depleted
                        return false;
                    }

                    // There might be a next window available
                    PreviousWindow();
                    goto hasPrevious; // === return HasPrevious();
                }

                if (_indexInWindow == uint.MaxValue)
                {
                    // Needs some initialization
                    // We search the last element in the window in the current dateTime
                    _indexInWindow = BinarySearchLast(window, CurrentDateTime) + 1;
                    _alreadyUsed[window] = _indexInWindow;
                }


                ulong depTime;
                do
                {
                    // Ok, so we at least have te right window.
                    // Lets see if we can retrieve the connection itself
                    // For that, we should check if the index is within the window size
                    if (_indexInWindow < 1)
                    {
                        // Ahh, the good old 'IndexOutOfBounds'
                        // In other words, this window is simply depleted
                        // We attempt to use the next window
                        PreviousWindow();
                        goto hasPrevious; // === return HasPrevious();
                    }


                    // Ok, so we have the right window and the connection exists! Hooray!
                    _connectionInternalId = _connectionsDb.DeparturePointers[windowPointer + _indexInWindow - 1];

                    // We update the DTI
                    _indexInWindow--;

                    // and get the departure time of the index because...
                    depTime = _connectionsDb.GetConnectionDeparture(_connectionInternalId);

                    // ... the current connection could fall _too late_
                    // Either just because of the specified dateTime
                    // or because a window has connections from multiple days
                    // If that happens, we just restart everything:
                } while (depTime > CurrentDateTime);

                // If we end up here, the desired connection exists and its departure time falls before the specified time
                // however, we might have jumped an entire cycle
                if (CurrentDateTime - depTime > _connectionsDb.WindowSizeInSeconds)
                {
                    // Nope, we made a cycle jump
                    _alreadyUsed[window] = _indexInWindow + 1;
                    PreviousWindow();
                    goto hasPrevious;
                }

                // current.WindowIndex points to the next needed element in the window
                // And current.ConnectionInternalId is set
                // So, we are pretty much done

                // Only thing that rests us is top update the current.DepartureTime, so that the caller knows when this connection is leaving
                // This is used by the aggregator
                CurrentDateTime = depTime;
                return true;
            }

            public bool Current(Connection toWrite)
            {
                return _connectionsDb.Get(new ConnectionId(_connectionsDb.DatabaseId, _connectionInternalId), toWrite);
            }
        }
    }
}