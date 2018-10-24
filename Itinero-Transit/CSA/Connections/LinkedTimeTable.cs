using System;
using System.Collections.Generic;
using Itinero_Transit.LinkedData;
using JsonLD.Core;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Itinero_Transit.CSA.ConnectionProviders
{
    /// <summary>
    /// Represents one entire page of connections, based on a LinkedConnections JSON-LD
    /// </summary>
    [Serializable]
    public class LinkedTimeTable : LinkedObject, ITimeTable
    {
        public Uri Next { get; set; }
        public Uri Prev { get; set; }

        private DateTime _startTime, _endTime;

        public List<IConnection> Graph { get; set; }

        public LinkedTimeTable(Uri uri) : base(uri)
        {
        }


        public LinkedTimeTable(JObject json) : base(new Uri(json["@id"].ToString()))
        {
            FromJson(json);
        }

        protected sealed override void FromJson(JObject json)
        {
            json.AssertTypeIs("http://www.w3.org/ns/hydra/core#PagedCollection");

            Next = AsUri(json["http://www.w3.org/ns/hydra/core#next"][0]["@id"].ToString());
            Prev = AsUri(json["http://www.w3.org/ns/hydra/core#previous"][0]["@id"].ToString());
            _startTime = _extractTime(AsUri(json["@id"].ToString()));
            _endTime = _extractTime(Next);


            Graph = new List<IConnection>();
            var jsonGraph = json["@graph"];
            foreach (var conn in jsonGraph)
            {
                try
                {
                    Graph.Add(new CSA.LinkedConnection((JObject) conn));
                }
                catch (ArgumentException e)
                {
                    Log.Warning(e, "Connection ignored due to exception");
                }
            }
        }

        private static DateTime _extractTime(Uri u)
        {
            var raw = u.OriginalString;
            var ind = raw.IndexOf("departureTime=", StringComparison.Ordinal);
            if (ind < 0)
            {
                throw new ArgumentException("The passed URI does not contain a departureTime argument");
            }

            var start = ind + "departureTime=".Length;
            var time = raw.Substring(start, raw.Length - start - 2);
            return DateTime.Parse(time);
        }

        public override string ToString()
        {
            return ToString(null);
        }

        public string ToString(ILocationProvider locationDecoder)
        {
            return ToString(locationDecoder, null);
        }

        public string ToString(ILocationProvider locationDecoder, List<Uri> whitelist)
        {
            var omitted = 0;
            var cons = "";
            foreach (var conn in Graph)
            {
                if (whitelist != null && !whitelist.Contains(conn.DepartureLocation())
                                      && !whitelist.Contains(conn.ArrivalLocation()))
                {
                    omitted++;
                    continue;
                }

                cons += $"  {conn.ToString(locationDecoder)}\n";
            }

            cons = cons.Substring(0, cons.Length - 1);

            var header =
                $"Timetable with {Graph.Count} connections ({omitted} omitted below); ID: {Uri} Next: {Next} Prev: {Prev}\n";
            return header + cons;
        }

        public DateTime StartTime()
        {
            return _startTime;
        }

        public DateTime EndTime()
        {
            return _endTime;
        }

        public Uri NextTable()
        {
            return Next;
        }

        public Uri PreviousTable()
        {
            return Prev;
        }

        public List<IConnection> Connections()
        {
            return Graph;
        }
    }
}