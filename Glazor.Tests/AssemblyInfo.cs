using NUnit.Framework;

// The fixtures set LoggerSupport's process-wide factory and FilterDefFactory.Default, and the cost tests count the library's
// log lines: the suite runs one test at a time on purpose.
[assembly: Parallelizable(ParallelScope.None)]
