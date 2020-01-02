using System;
using System.Collections.Generic;
using System.IO;
using Itinero.Transit.Data;

namespace Itinero.Transit.Processor.Switch
{
    class SwitchDumpTransitDbStops : DocumentedSwitch, ITransitDbSink
    {
        private static readonly string[] _names = {"--dump-stops"};

        private static string About = "Writes all stops contained in a transitDB to console";


        private static readonly List<(List<string> args, bool isObligated, string comment, string defaultValue)>
            _extraParams =
                new List<(List<string> args, bool isObligated, string comment, string defaultValue)>
                {
                    SwitchesExtensions.opt("file", "The file to write the data to, in .csv format")
                        .SetDefault("")
                };

        private const bool IsStable = true;


        public SwitchDumpTransitDbStops
            () :
            base(_names, About, _extraParams, IsStable)
        {
        }

        public void Use(Dictionary<string, string> arguments, TransitDb tdb)
        {
            var writeTo = arguments["file"];


            var stops = tdb.Latest.StopsDb;


            using (var outStream =
                string.IsNullOrEmpty(writeTo) ? Console.Out : new StreamWriter(File.OpenWrite(writeTo)))
            {
                var knownAttributes = new List<string>();

                foreach (var stop in stops)
                {
                    var attributes = stop.Attributes;
                    foreach (var (key, _) in attributes)
                    {
                        if (!knownAttributes.Contains(key))
                        {
                            knownAttributes.Add(key);
                        }
                    }
                }


                var header = "globalId,Latitude,Longitude,";
                foreach (var knownAttribute in knownAttributes)
                {
                    header += "," + knownAttribute;
                }

                outStream.WriteLine(header);

                foreach (var stop in stops)
                {
                    
                    var value =
                        $"{stop.GlobalId},{stop.Latitude}, {stop.Longitude}";

                    var attributes = stop.Attributes;
                    foreach (var attribute in knownAttributes)
                    {
                        attributes.TryGetValue(attribute, out var val);
                        value += $",{val ?? ""}";
                    }

                    outStream.WriteLine(value);
                }
            }
        }
    }
}