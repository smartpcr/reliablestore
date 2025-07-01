//-------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Benchmarks
{
    using BenchmarkDotNet.Running;
    using System;

    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--concurrent")
            {
                // Run concurrent benchmarks
                var summary = BenchmarkRunner.Run<ConcurrentProviderBenchmarks>();
            }
            else
            {
                // Run sequential benchmarks
                var summary = BenchmarkRunner.Run<ProviderBenchmarks>();
            }
            
            Console.WriteLine("Benchmarks completed. Press any key to exit...");
            Console.ReadKey();
        }
    }
}