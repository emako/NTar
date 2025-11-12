using NUnit.Framework;
using System.IO.Compression;

namespace NTar.Tests;

[TestFixture]
public class TestTar
{
    [Test]
    public void TestEntries()
    {
        var testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        using (var stream = File.OpenRead(Path.Combine(testDirectory, "test.tar")))
        {
            CheckStream(stream);
        }
    }

    /// <summary>
    /// Same test as TestEntries, but we perform this through a GzipStream
    /// </summary>
    [Test]
    public void TestEntriesGunzip()
    {
        var testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);

        var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, true))
        using (var stream = File.OpenRead(Path.Combine(testDirectory, "test.tar")))
        {
            stream.CopyTo(gzipStream);
            gzipStream.Flush();
        }
        memoryStream.Position = 0;
        using (var gunzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
        {
            CheckStream(gunzipStream);
        }
    }

    public void CheckStream(Stream stream)
    {
        var testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        {
            var files = new Dictionary<string, string>();

            // Untar the stream
            foreach (var entryStream in stream.Read())
            {
                if (entryStream.IsDirectory) continue;

                var reader = new StreamReader(entryStream);
                files[entryStream.FileName] = reader.ReadToEnd();
            }

            Assert.That(2, Is.EqualTo(files.Count));

            Assert.That(files.ContainsKey("./a.txt"), Is.True);
            Assert.That(files.ContainsKey("./b/b.txt"), Is.True);

            Assert.That("0123456789", Is.EqualTo(files["./a.txt"]));
            Assert.That(string.Empty, Is.EqualTo(files["./b/b.txt"]));

            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.Extract(testDirectory);
                Assert.That("0123456789", Is.EqualTo(File.ReadAllText(Path.Combine(testDirectory, "./a.txt"))));
            }
        }
    }

    [Test]
    public void TestToDirectory()
    {
        var testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        var outputDirectory = Path.Combine(testDirectory, "output");
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, true);
        }

        using (var stream = File.OpenRead(Path.Combine(testDirectory, "test.tar")))
        {
            stream.Extract(outputDirectory);

            var fileA = Path.Combine(outputDirectory, "./a.txt");
            var fileB = Path.Combine(outputDirectory, "./b/b.txt");

            Assert.That(File.Exists(fileA), Is.True);
            Assert.That(File.Exists(fileB), Is.True);

            Assert.That("0123456789", Is.EqualTo(File.ReadAllText(fileA)));
            Assert.That(string.Empty, Is.EqualTo(File.ReadAllText(fileB)));
        }
    }
}
