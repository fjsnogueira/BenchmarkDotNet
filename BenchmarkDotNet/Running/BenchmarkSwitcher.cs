﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Horology;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Portability;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Properties;

namespace BenchmarkDotNet.Running
{
    public class BenchmarkSwitcher
    {
        private readonly ConsoleLogger logger = new ConsoleLogger();
        private readonly TypeParser typeParser;

        public BenchmarkSwitcher(Type[] types)
        {
            typeParser = new TypeParser(types, logger);
        }

        public BenchmarkSwitcher(Assembly assembly)
        {
            // Use reflection for a more maintainable way of creating the benchmark switcher,
            // Benchmarks are listed in namespace order first (e.g. BenchmarkDotNet.Samples.CPU,
            // BenchmarkDotNet.Samples.IL, etc) then by name, so the output is easy to understand.
            var types = assembly
                .GetTypes()
                .Where(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                             .Any(m => MemberInfoExtensions.GetCustomAttributes<BenchmarkAttribute>(m, true).Any()))
                .Where(t => !Portability.TypeExtensions.IsGenericType(t))
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Name)
                .ToArray();
            typeParser = new TypeParser(types, logger);
        }

        public IEnumerable<Summary> Run(string[] args = null)
        {
            args = typeParser.ReadArgumentList(args ?? new string[0]);
            return RunBenchmarks(args);
        }

        private IEnumerable<Summary> RunBenchmarks(string[] args)
        {
            var globalChronometer = Chronometer.Start();
            var summaries = new List<Summary>();

            if (ShouldDisplayOptions(args))
            {
                DisplayOptions();
                return Enumerable.Empty<Summary>();
            }

            var config = ManualConfig.Union(DefaultConfig.Instance, ManualConfig.Parse(args));

            foreach (var typeWithMethods in typeParser.MatchingTypesWithMethods(args))
            {
                logger.WriteLineHeader("Target type: " + typeWithMethods.Type.Name);
                if (typeWithMethods.AllMethodsInType)
                    summaries.Add(BenchmarkRunner.Run(typeWithMethods.Type, config));
                else
                    summaries.Add(BenchmarkRunner.Run(typeWithMethods.Type, typeWithMethods.Methods, config));
                logger.WriteLine();
            }

            // TODO: move this logic to the RunUrl method
#if CLASSIC
            if (args.Length > 0 && (args[0].StartsWith("http://") || args[0].StartsWith("https://")))
            {
                var url = args[0];
                Uri uri = new Uri(url);
                var name = uri.IsFile ? Path.GetFileName(uri.LocalPath) : "URL";
                summaries.Add(BenchmarkRunner.RunUrl(url, config));
            }
#endif

            var clockSpan = globalChronometer.Stop();
            BenchmarkRunner.LogTotalTime(logger, clockSpan.GetTimeSpan(), "Global total time");
            return summaries;
        }

        public bool ShouldDisplayOptions(string[] args)
        {
            return args.Select(a => a.ToLowerInvariant()).Any(a => a == "--help" || a == "-h");
        }

        private void DisplayOptions()
        {
            logger.WriteLineHeader($"{BenchmarkDotNetInfo.FullTitle}");
            logger.WriteLine();
            logger.WriteLineHeader("Options:");

            ManualConfig.PrintOptions(logger, prefixWidth: 30, outputWidth: Console.WindowWidth);
            logger.WriteLine();
            typeParser.PrintOptions(logger, prefixWidth: 30, outputWidth: Console.WindowWidth);
        }
    }
}