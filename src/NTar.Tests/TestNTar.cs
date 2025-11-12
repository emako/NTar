using NUnit.Framework;
using System.Text;

namespace NTar.Tests;

[TestFixture]
public class TestNTar
{
    [Test]
    public void TestUntar()
    {
        string testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        using FileStream stream = File.OpenRead(Path.Combine(testDirectory, "test.tar"));
        Dictionary<string, string> files = [];

        // Untar the stream
        foreach (var entryStream in stream.Untar())
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
            stream.UntarTo(testDirectory);
            Assert.That("0123456789", Is.EqualTo(File.ReadAllText(Path.Combine(testDirectory, "./a.txt"))));
        }
    }

    [Test]
    public void TestUntarTo()
    {
        string testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        string outputDirectory = Path.Combine(testDirectory, "output");

        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, true);
        }

        using FileStream stream = File.OpenRead(Path.Combine(testDirectory, "test.tar"));
        stream.UntarTo(outputDirectory);

        string fileA = Path.Combine(outputDirectory, "./a.txt");
        string fileB = Path.Combine(outputDirectory, "./b/b.txt");

        Assert.That(File.Exists(fileA), Is.True);
        Assert.That(File.Exists(fileB), Is.True);

        Assert.That("0123456789", Is.EqualTo(File.ReadAllText(fileA)));
        Assert.That(string.Empty, Is.EqualTo(File.ReadAllText(fileB)));
    }

    [Test]
    public void TestTar()
    {
        string testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);

        List<TarEntryStream> entries =
        [
            TarEntryStream.Create([], "b/", mode: TarEntryMode.OwnerWrite, userName: "root", groupName: "root2", type: TarEntryType.Directory, groupId: 5300, userId: 3200),
            TarEntryStream.Create(Encoding.UTF8.GetBytes("6767167"), "b/a.txt", mode: TarEntryMode.Full, groupId: 488, userId: 2390),
            TarEntryStream.Create(Encoding.UTF8.GetBytes("6969169"), "b.txt", mode: TarEntryMode.Full, groupId: 500, userId: 200),
        ];

        using Stream tarStream = entries.Tar();
        string outputFile = Path.GetFullPath(Path.Combine(testDirectory, "test_created.tar"));
        string outDir = Path.GetDirectoryName(outputFile);

        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using FileStream fs = new(outputFile, FileMode.Create, FileAccess.Write);
        tarStream.CopyTo(fs);
    }

    [Test]
    public void TestTarTo()
    {
        string testDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        string tempDir = Path.Combine(testDirectory, "input");

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "a.txt"), "0123456789");
        Directory.CreateDirectory(Path.Combine(tempDir, "b"));
        File.WriteAllText(Path.Combine(tempDir, "b", "b.txt"), string.Empty);

        string outputTar = Path.Combine(testDirectory, "test_created.tar");
        if (File.Exists(outputTar)) File.Delete(outputTar);

        // Create tar
        tempDir.TarTo(outputTar);

        // Read back and verify
        using (FileStream stream = File.OpenRead(outputTar))
        {
            Dictionary<string, string> files = [];

            foreach (var entryStream in stream.Untar())
            {
                if (entryStream.IsDirectory) continue;

                StreamReader reader = new(entryStream);
                files[entryStream.FileName] = reader.ReadToEnd();
            }

            Assert.That(2, Is.EqualTo(files.Count));
            Assert.That(files.ContainsKey("a.txt"), Is.True);
            Assert.That(files.ContainsKey("b/b.txt"), Is.True);
            Assert.That("0123456789", Is.EqualTo(files["a.txt"]));
            Assert.That(string.Empty, Is.EqualTo(files["b/b.txt"]));
        }
    }
}
