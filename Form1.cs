#nullable enable

using Nessos.Effects;
using Nessos.Effects.Handlers;

// See also:
// https://github.com/bent-rasmussen/eff-fx

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            await Test().Run(new PhysicalFileSystemEffectHandler(@"C:\"));
        }

        async Eff Test()
        {
            var results = new List<dynamic>();

            var testPath = @$"temp\hello-world-{Guid.NewGuid()}.txt";

            using var stream = await FileIO.OpenWrite(testPath);

            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync($"Hello world! {DateTimeOffset.Now}");

            await Task.Delay(200);

            foreach (var info in await DirectoryIO.Enumerate("temp"))
            {
                var result =
                    new
                    {
                        Path = info.path,
                        Kind = info.kind,
                        PhysicalPath = await FileSystemIO.ToPhysicalPath(info),
                        Exists  = await FileSystemIO.Exists(info),
                        Created = await FileSystemIO.CreationTime(info),
                        Changed = await FileSystemIO.LastWriteTime(info),
                        Length  = (info.kind == FileKind.File ? (long?)await FileIO.Length(info.path) : null),
                    };

                results.Add(result);
            }
        }
    }

    // Effect Handler

    public abstract class PhysicalFileSystemEffectHandlerBase : EffectHandler
    {
        public PhysicalFileSystemEffectHandlerBase(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public string ResolvePath(string path) =>
            Path.Combine(Root, path);

        public string ResolvePath<T>(FilePathEffect<T> effect) =>
            ResolvePath(effect.Path);
    }

    public class PhysicalFileSystemEffectHandler : PhysicalFileSystemEffectHandlerBase
    {
        public PhysicalFileSystemEffectHandler(string root)
            : base(root)
        {
        }

        public override ValueTask Handle<TResult>(EffectAwaiter<TResult> awaiter)
        {
            switch (awaiter)
            {
                // General effects

                case EffectAwaiter<string?> { Effect: GetPhysicalPathEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        awtr.SetResult(resolvedPath);
                    }
                    break;

                case EffectAwaiter<bool> { Effect: GetFileExistsEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        switch (info.Kind)
                        {
                            case FileKind.File:
                                awtr.SetResult(
                                    File.Exists(resolvedPath));
                                break;

                            case FileKind.Directory:
                                awtr.SetResult(
                                    Directory.Exists(resolvedPath));
                                break;
                        }
                    }
                    break;

                case EffectAwaiter<DateTimeOffset> { Effect: GetFileCreationTimeEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        switch (info.Kind)
                        {
                            case FileKind.File:
                                awtr.SetResult(
                                    File.GetCreationTime(resolvedPath));
                                break;

                            case FileKind.Directory:
                                awtr.SetResult(
                                    Directory.GetCreationTime(resolvedPath));
                                break;
                        }
                    }
                    break;

                case EffectAwaiter<DateTimeOffset> { Effect: GetFileLastWriteTimeEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        switch (info.Kind)
                        {
                            case FileKind.File:
                                awtr.SetResult(
                                    File.GetLastWriteTime(resolvedPath));
                                break;

                            case FileKind.Directory:
                                awtr.SetResult(
                                    Directory.GetLastWriteTime(resolvedPath));
                                break;
                        }
                    }
                    break;

                case EffectAwaiter<Unit> { Effect: DeleteFileEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        switch (info.Kind)
                        {
                            case FileKind.File:
                                File.Delete(resolvedPath);
                                awtr.SetResult(Unit.Value);
                                break;

                            case FileKind.Directory:
                                Directory.Delete(resolvedPath);
                                awtr.SetResult(Unit.Value);
                                break;
                        }
                    }
                    break;

                // File-specific effects

                case EffectAwaiter<long> { Effect: GetFileLengthEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        var length = new FileInfo(resolvedPath).Length;
                        awtr.SetResult(length);
                    }
                    break;

                case EffectAwaiter<Stream> { Effect: GetFileInputStreamEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        var stream = File.OpenRead(resolvedPath);
                        awtr.SetResult(stream);
                    }
                    break;

                case EffectAwaiter<Stream> { Effect: GetFileOutputStreamEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info);

                        var stream = File.OpenWrite(resolvedPath);
                        awtr.SetResult(stream);
                    }
                    break;

                // Directory-specific effects

                case EffectAwaiter<IEnumerable<(string, FileKind)>> { Effect: EnumerateDirectoryEffect info } awtr:
                    {
                        var resolvedPath = ResolvePath(info.Path);

                        string Localize(string path) =>
                            Path.Combine(info.Path, Path.GetFileName(path));

                        (string, FileKind) LocalizeToFile(string path) =>
                            (Localize(path), FileKind.File);

                        (string, FileKind) LocalizeToDirectory(string path) =>
                            (Localize(path), FileKind.Directory);

                        switch (info.FindKind)
                        {
                            case FileKind.File:
                                awtr.SetResult(
                                    Directory.EnumerateFiles(resolvedPath).Select(LocalizeToFile));
                                break;

                            case FileKind.Directory:
                                awtr.SetResult(
                                    Directory.EnumerateDirectories(resolvedPath).Select(LocalizeToDirectory));
                                break;

                            case null:
                                var dirs = Directory.EnumerateDirectories(resolvedPath).Select(LocalizeToDirectory);
                                var files = Directory.EnumerateFiles(resolvedPath).Select(LocalizeToFile);

                                awtr.SetResult(
                                    Enumerable.Concat(dirs, files));
                                break;
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException(
                        awaiter.Effect.GetType().Name);
            }

            return default;
        }
    }

    // Effects

    public static class FileSystemIO
    {
        public static GetPhysicalPathEffect ToPhysicalPath((string path, FileKind kind) info) =>
            new GetPhysicalPathEffect(info.path, info.kind);

        public static GetFileExistsEffect Exists((string path, FileKind kind) info) =>
            new GetFileExistsEffect(info.path, info.kind);

        public static GetFileCreationTimeEffect CreationTime((string path, FileKind kind) info) =>
            new GetFileCreationTimeEffect(info.path, info.kind);

        public static GetFileLastWriteTimeEffect LastWriteTime((string path, FileKind kind) info) =>
            new GetFileLastWriteTimeEffect(info.path, info.kind);

        public static DeleteFileEffect Delete((string path, FileKind kind) info) =>
            new DeleteFileEffect(info.path, info.kind);
    }

    public static class FileIO
    {
        public static FileKind Kind => FileKind.File;

        // General effects

        public static GetPhysicalPathEffect ToPhysicalPath(string path) =>
            new GetPhysicalPathEffect(path, Kind);

        public static GetFileExistsEffect Exists(string path) =>
            new GetFileExistsEffect(path, Kind);

        public static GetFileCreationTimeEffect CreationTime(string path) =>
            new GetFileCreationTimeEffect(path, Kind);

        public static GetFileLastWriteTimeEffect LastWriteTime(string path) =>
            new GetFileLastWriteTimeEffect(path, Kind);

        public static DeleteFileEffect Delete(string path) =>
            new DeleteFileEffect(path, Kind);

        // File-specific effects

        public static GetFileLengthEffect Length(string path) =>
            new GetFileLengthEffect(path);

        public static GetFileInputStreamEffect OpenRead(string path) =>
            new GetFileInputStreamEffect(path);

        public static GetFileOutputStreamEffect OpenWrite(string path) =>
            new GetFileOutputStreamEffect(path);
    }

    public static class DirectoryIO
    {
        public static FileKind Kind => FileKind.Directory;

        // General effects

        public static GetPhysicalPathEffect ToPhysicalPath(string path) =>
            new GetPhysicalPathEffect(path, Kind);

        public static GetFileExistsEffect Exists(string path) =>
            new GetFileExistsEffect(path, Kind);

        public static GetFileCreationTimeEffect CreationTime(string path) =>
            new GetFileCreationTimeEffect(path, Kind);

        public static GetFileLastWriteTimeEffect LastWriteTime(string path) =>
            new GetFileLastWriteTimeEffect(path, Kind);

        public static DeleteFileEffect Delete(string path) =>
            new DeleteFileEffect(path, Kind);

        // Directory-specific effects

        public static EnumerateDirectoryEffect Enumerate(string path, FileKind? findKind = null) =>
            new EnumerateDirectoryEffect(path, findKind);

        public static EnumerateDirectoryEffect EnumerateFiles(string path) =>
            Enumerate(path, findKind: FileKind.File);

        public static EnumerateDirectoryEffect EnumerateDirectories(string path) =>
            Enumerate(path, findKind: FileKind.Directory);
    }

    // (Types capturing individual effects)

    public enum FileKind
    {
        File,
        Directory,
    }

    public abstract class FilePathEffect<T> : Effect<T>
    {
        public FilePathEffect(string path, FileKind kind)
        {
            Path = path;
            Kind = kind;
        }

        public string Path { get; }

        public FileKind Kind { get; }
    }

    public class EnumerateDirectoryEffect : FilePathEffect<IEnumerable<(string path, FileKind kind)>>
    {
        public EnumerateDirectoryEffect(string path, FileKind? kind = null)
            : base(path, FileKind.Directory)
        {
            FindKind = kind;
        }

        public FileKind? FindKind { get; }
    }

    public class GetPhysicalPathEffect : FilePathEffect<string?>
    {
        public GetPhysicalPathEffect(string path, FileKind kind)
            : base(path, kind)
        {
        }
    }

    public class GetFileExistsEffect : FilePathEffect<bool>
    {
        public GetFileExistsEffect(string path, FileKind kind)
            : base(path, kind)
        {
        }
    }

    public class GetFileLengthEffect : FilePathEffect<long>
    {
        public GetFileLengthEffect(string path)
            : base(path, FileKind.File)
        {
        }
    }

    public class GetFileInputStreamEffect : FilePathEffect<Stream>
    {
        public GetFileInputStreamEffect(string path)
            : base(path, FileKind.File)
        {
        }
    }

    public class GetFileOutputStreamEffect : FilePathEffect<Stream>
    {
        public GetFileOutputStreamEffect(string path)
            : base(path, FileKind.File)
        {
        }
    }

    public class GetFileCreationTimeEffect : FilePathEffect<DateTimeOffset>
    {
        public GetFileCreationTimeEffect(string path, FileKind kind)
            : base(path, kind)
        {
        }
    }

    public class GetFileLastWriteTimeEffect : FilePathEffect<DateTimeOffset>
    {
        public GetFileLastWriteTimeEffect(string path, FileKind kind)
            : base(path, kind)
        {
        }
    }

    public class DeleteFileEffect : FilePathEffect<Unit>
    {
        public DeleteFileEffect(string path, FileKind kind)
            : base(path, kind)
        {
        }
    }
}
