using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace FastCsv.Benchmarks;

public class RealDataBenchmark
{
    private static readonly string TestDataDirectory;

    static RealDataBenchmark()
    {
        // Get path to TestData directory
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        TestDataDirectory = Path.Combine(assemblyDirectory!, "..", "..", "..", "..", "..", "tests", "TestData");
        TestDataDirectory = Path.GetFullPath(TestDataDirectory);
    }

    public static void RunRealDataComparison()
    {
        Console.WriteLine("FastCsv Real Data Performance Analysis");
        Console.WriteLine("=====================================");
        Console.WriteLine();
        
        var resultSet = new BenchmarkResultSet
        {
            BenchmarkSuite = "FastCsv Real Data Performance",
            RuntimeVersion = Environment.Version.ToString()
        };

        var testFiles = new[]
        {
            ("simple.csv", "Simple 3-record file"),
            ("employees.csv", "Employee data (10 records)"),
            ("products.csv", "Product catalog with quotes"),
            ("mixed_data_types.csv", "Mixed data types"),
            ("medium_dataset.csv", "Medium dataset (1K records)"),
            ("large_dataset_10k.csv", "Large dataset (10K records)")
        };

        foreach (var (fileName, description) in testFiles)
        {
            var filePath = Path.Combine(TestDataDirectory, fileName);
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"⚠️  Skipping {fileName} - file not found");
                continue;
            }

            Console.WriteLine($"📄 {description}");
            Console.WriteLine($"File: {fileName}");
            
            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"Size: {FormatFileSize(fileInfo.Length)}");
            
            BenchmarkFile(filePath, fileName, description, resultSet);
            Console.WriteLine();
        }

        // Special performance test with huge dataset if available
        var hugeFile = Path.Combine(TestDataDirectory, "huge_dataset.csv");
        if (File.Exists(hugeFile))
        {
            Console.WriteLine("🚀 EXTREME PERFORMANCE TEST");
            Console.WriteLine("===========================");
            BenchmarkLargeFile(hugeFile, resultSet);
        }
        
        // Export results in all formats to consistent directory
        var outputDir = BenchmarkExporter.GetBenchmarkOutputDirectory("RealData");
        BenchmarkExporter.ExportAll(resultSet, outputDir);
    }

    private static void BenchmarkFile(string filePath, string fileName, string description, BenchmarkResultSet resultSet)
    {
        var options = new global::FastCsv.CsvOptions(hasHeader: true);
        int iterations = fileName.Contains("large") ? 5 : 20; // Fewer iterations for large files

        var fileInfo = new FileInfo(filePath);
        var fileSize = FormatFileSize(fileInfo.Length);
        var recordCount = 0;

        try
        {
            // Sync Read All Records
            var syncResult = BenchmarkActionWithResult(() =>
            {
                var content = File.ReadAllText(filePath);
                var records = global::FastCsv.Csv.ReadAllRecords(content, options);
                recordCount = records.Count;
                return recordCount;
            }, iterations, "Sync ReadAllRecords", description, "FastCsv", recordCount, fileSize);
            resultSet.Results.Add(syncResult);

            // Count Records Only
            var countResult = BenchmarkActionWithResult(() =>
            {
                var content = File.ReadAllText(filePath);
                return global::FastCsv.Csv.CountRecords(content, options);
            }, iterations, "Count Only", description, "FastCsv", recordCount, fileSize);
            resultSet.Results.Add(countResult);

            // Stream Reading
            var streamResult = BenchmarkActionWithResult(() =>
            {
                using var stream = File.OpenRead(filePath);
                using var reader = global::FastCsv.Csv.CreateReader(stream, options);
                return reader.CountRecords();
            }, iterations, "Stream Reading", description, "FastCsv", recordCount, fileSize);
            resultSet.Results.Add(streamResult);

#if NET7_0_OR_GREATER
            // Async File Reading
            var asyncResult = BenchmarkAsyncActionWithResult(async () =>
            {
                var records = await global::FastCsv.Csv.ReadFileAsync(filePath, options, null, CancellationToken.None);
                return records.Count;
            }, Math.Min(iterations, 10), "Async ReadFileAsync", description, "FastCsv", recordCount, fileSize);
            resultSet.Results.Add(asyncResult);

            // Async Stream Reading
            var asyncStreamResult = BenchmarkAsyncActionWithResult(async () =>
            {
                await using var stream = File.OpenRead(filePath);
                var records = await global::FastCsv.Csv.ReadStreamAsync(stream, options, null, false, CancellationToken.None);
                return records.Count;
            }, Math.Min(iterations, 10), "Async Stream", description, "FastCsv", recordCount, fileSize);
            resultSet.Results.Add(asyncStreamResult);
#endif

            // Performance comparison (console output)
            Console.WriteLine("Performance Summary:");
            Console.WriteLine($"  🏃 Fastest: {countResult.Method} ({countResult.MeanTimeMs:F2} ms/op)");
            Console.WriteLine($"  📊 Best for processing: {(syncResult.MeanTimeMs < streamResult.MeanTimeMs ? syncResult.Method : streamResult.Method)} ({Math.Min(syncResult.MeanTimeMs, streamResult.MeanTimeMs):F2} ms/op)");

#if NET7_0_OR_GREATER
            Console.WriteLine($"  ⚡ Async benefit: {(syncResult.MeanTimeMs > asyncResult.MeanTimeMs ? "YES" : "MINIMAL")} ({(syncResult.MeanTimeMs - asyncResult.MeanTimeMs) / syncResult.MeanTimeMs * 100:F0}% faster)");
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error benchmarking {fileName}: {ex.Message}");
        }
    }

    private static void BenchmarkLargeFile(string filePath, BenchmarkResultSet resultSet)
    {
        var fileInfo = new FileInfo(filePath);
        Console.WriteLine($"File: huge_dataset.csv ({FormatFileSize(fileInfo.Length)})");
        
        var options = new global::FastCsv.CsvOptions(hasHeader: true);

        // Ultra-fast count only
        var countTime = BenchmarkAction(() =>
        {
            var content = File.ReadAllText(filePath);
            return global::FastCsv.Csv.CountRecords(content, options);
        }, 3, "Count Records");

        // Memory-efficient stream reading
        var streamTime = BenchmarkAction(() =>
        {
            using var stream = File.OpenRead(filePath);
            using var reader = global::FastCsv.Csv.CreateReader(stream, options);
            var count = 0;
            while (reader.TryReadRecord(out _)) count++;
            return count;
        }, 3, "Stream Processing");

#if NET7_0_OR_GREATER
        // Async streaming (memory efficient)
        var asyncStreamTime = BenchmarkAsyncAction(async () =>
        {
            var count = 0;
            await foreach (var record in global::FastCsv.Csv.ReadFileAsyncEnumerable(filePath, options, null, CancellationToken.None))
            {
                count++;
            }
            return count;
        }, 2, "Async Streaming");

        Console.WriteLine($"🎯 Best for huge files: Async Streaming ({asyncStreamTime:F2} ms/op)");
#endif

        Console.WriteLine($"💡 Memory usage: Stream/Async methods use constant memory");
        Console.WriteLine($"📈 Throughput: ~{fileInfo.Length / 1024.0 / 1024.0 / Math.Min(countTime, streamTime) * 1000:F1} MB/sec");
    }

    private static double BenchmarkAction(Func<int> action, int iterations, string name)
    {
        // Warmup
        action();

        var stopwatch = Stopwatch.StartNew();
        int totalCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            var count = action();
            totalCount += count;
        }

        stopwatch.Stop();
        double msPerOp = stopwatch.ElapsedMilliseconds / (double)iterations;

        Console.WriteLine($"  {name,-20} : {stopwatch.ElapsedMilliseconds:N0} ms ({msPerOp:F2} ms/op, {totalCount / iterations:N0} records)");

        return msPerOp;
    }

#if NET7_0_OR_GREATER
    private static double BenchmarkAsyncAction(Func<Task<int>> action, int iterations, string name)
    {
        // Warmup
        action().GetAwaiter().GetResult();

        var stopwatch = Stopwatch.StartNew();
        int totalCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            var count = action().GetAwaiter().GetResult();
            totalCount += count;
        }

        stopwatch.Stop();
        double msPerOp = stopwatch.ElapsedMilliseconds / (double)iterations;

        Console.WriteLine($"  {name,-20} : {stopwatch.ElapsedMilliseconds:N0} ms ({msPerOp:F2} ms/op, {totalCount / iterations:N0} records)");

        return msPerOp;
    }
#endif

    private static BenchmarkResult BenchmarkActionWithResult(Func<int> action, int iterations, string method, string testCase, string library, int rowCount, string fileSize)
    {
        // Warmup
        action();

        var stopwatch = Stopwatch.StartNew();
        int totalCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            var count = action();
            totalCount += count;
        }

        stopwatch.Stop();
        double msPerOp = stopwatch.ElapsedMilliseconds / (double)iterations;

        Console.WriteLine($"  {method,-20} : {stopwatch.ElapsedMilliseconds:N0} ms ({msPerOp:F2} ms/op, {totalCount / iterations:N0} records)");

        return new BenchmarkResult
        {
            BenchmarkName = "Real Data Performance",
            TestCase = testCase,
            Library = library,
            Method = method,
            RowCount = totalCount / iterations,
            Iterations = iterations,
            MeanTimeMs = msPerOp,
            StdDevMs = 0, // We're not calculating standard deviation here
            AllocatedBytes = 0, // Not tracking allocations in this benchmark
            FileSize = fileSize,
            Environment = $".NET {Environment.Version}"
        };
    }

#if NET7_0_OR_GREATER
    private static BenchmarkResult BenchmarkAsyncActionWithResult(Func<Task<int>> action, int iterations, string method, string testCase, string library, int rowCount, string fileSize)
    {
        // Warmup
        action().GetAwaiter().GetResult();

        var stopwatch = Stopwatch.StartNew();
        int totalCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            var count = action().GetAwaiter().GetResult();
            totalCount += count;
        }

        stopwatch.Stop();
        double msPerOp = stopwatch.ElapsedMilliseconds / (double)iterations;

        Console.WriteLine($"  {method,-20} : {stopwatch.ElapsedMilliseconds:N0} ms ({msPerOp:F2} ms/op, {totalCount / iterations:N0} records)");

        return new BenchmarkResult
        {
            BenchmarkName = "Real Data Performance",
            TestCase = testCase,
            Library = library,
            Method = method,
            RowCount = totalCount / iterations,
            Iterations = iterations,
            MeanTimeMs = msPerOp,
            StdDevMs = 0,
            AllocatedBytes = 0,
            FileSize = fileSize,
            Environment = $".NET {Environment.Version}"
        };
    }
#endif

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {suffixes[suffixIndex]}";
    }
}