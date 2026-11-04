namespace Codelab.Testing.Tests.Support;

/// <summary>A uniquely named temporary folder, deleted on dispose.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"codelab-testing-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Directory(string relativePath) => System.IO.Directory.CreateDirectory(Path.Combine(Root, relativePath)).FullName;

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Root))
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}
