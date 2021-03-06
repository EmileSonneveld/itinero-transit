using System;
using System.Collections.Generic;
using Itinero.Transit.Data;

namespace Itinero.Transit.Processor.Switch
{
    class SwitchGc : DocumentedSwitch,
        ITransitDbModifier, ITransitDbSink, ITransitDbSource
    {
        private static readonly string[] _names = {"--gc","--garbage-collect"};

        private static string _about =
            "Run garbage collection. This is for debugging";


        private static readonly List<(List<string> args, bool isObligated, string comment, string defaultValue)>
            _extraParams =
                new List<(List<string> args, bool isObligated, string comment, string defaultValue)>();

        private const bool _isStable = false;


        public SwitchGc
            () :base(_names, _about, _extraParams, _isStable)
        {
        }

        private void Run()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public TransitDb Modify(Dictionary<string, string> parameters, TransitDb transitDb)
        {
            Run();
            return transitDb;
        }

        public void Use(Dictionary<string, string> parameters, TransitDb transitDb)
        {
           Run();
        }

        public TransitDb Generate(Dictionary<string, string> parameters)
        {
           Run();
           return new TransitDb(0);
        }
    }
}