// The MIT License (MIT)

// Copyright (c) 2018 Anyways B.V.B.A.

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using Itinero.Transit.Data;
using Xunit;

namespace Itinero.Transit.Tests.Data
{
    public class ConnectionsDbTests
    {
        [Fact]
        public void ConnectionsDb_ShouldStoreWithWindowId()
        {
            var db = new ConnectionsDb(60);
            var departureTime = new DateTime(2018, 11, 14, 2, 3, 9);
            var id = db.Add((100, 0), (100, 1), departureTime, 1024, 10245);
            
            Assert.Equal((uint)0, id.localId);
            Assert.Equal((uint)System.Math.Floor(departureTime.TimeOfDay.TotalSeconds / 60), id.window);
        }
    }
}