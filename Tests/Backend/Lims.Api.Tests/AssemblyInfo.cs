// In-process API hosts share Serilog's bootstrap logger; construct and dispose hosts serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
