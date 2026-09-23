using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.Udf;

namespace QuickLook.Plugin.IsoViewer
{
    /// <summary>
    /// Hybrid ISO9660/Joliet + UDF.
    /// Windows install ISOs: ISO9660 often only README.TXT; real tree is UDF.
    /// </summary>
    public sealed class IsoFileSystem : IDisposable
    {
        readonly SimpleIsoReader _iso;
        readonly UdfBackend _udf;
        readonly bool _useUdf;

        public string VolumeId { get; private set; }

        public IsoFileSystem(string path)
        {
            int isoCount = 0;
            int udfCount = 0;
            Exception isoEx = null, udfEx = null;
            SimpleIsoReader iso = null;
            UdfBackend udf = null;

            try
            {
                iso = new SimpleIsoReader(path);
                VolumeId = iso.VolumeId;
                isoCount = CountEntries(iso.ListDirectory(""), iso, "", 0, 3);
            }
            catch (Exception ex)
            {
                isoEx = ex;
                try { iso?.Dispose(); } catch { }
                iso = null;
            }

            try
            {
                udf = new UdfBackend(path);
                if (string.IsNullOrEmpty(VolumeId))
                    VolumeId = udf.VolumeId;
                else if (!string.IsNullOrEmpty(udf.VolumeId))
                    VolumeId = udf.VolumeId;
                udfCount = CountEntries(udf.ListDirectory(""), udf, "", 0, 3);
            }
            catch (Exception ex)
            {
                udfEx = ex;
                try { udf?.Dispose(); } catch { }
                udf = null;
            }

            if (udf != null && (iso == null || udfCount > isoCount || (isoCount <= 3 && udfCount > isoCount)))
            {
                _udf = udf;
                _useUdf = true;
                try { iso?.Dispose(); } catch { }
                return;
            }

            if (iso != null)
            {
                _iso = iso;
                _useUdf = false;
                try { udf?.Dispose(); } catch { }
                return;
            }

            throw new Exception(
                "Cannot read image as ISO9660/Joliet or UDF. " +
                (isoEx != null ? "ISO: " + isoEx.Message + ". " : "") +
                (udfEx != null ? "UDF: " + udfEx.Message : "UDF: not available"));
        }

        static int CountEntries(IEnumerable<IsoDirEntry> entries, object fs, string path, int depth, int maxDepth)
        {
            int n = 0;
            if (entries == null || depth > maxDepth) return 0;
            foreach (var e in entries)
            {
                n++;
                if (e.IsDir && depth < maxDepth)
                {
                    string child = string.IsNullOrEmpty(path) ? e.Name : path + "/" + e.Name;
                    try
                    {
                        IEnumerable<IsoDirEntry> sub = null;
                        if (fs is SimpleIsoReader r) sub = r.ListDirectory(child);
                        else if (fs is UdfBackend u) sub = u.ListDirectory(child);
                        n += CountEntries(sub, fs, child, depth + 1, maxDepth);
                    }
                    catch { }
                }
            }
            return n;
        }

        public bool Exists(string path) =>
            _useUdf ? _udf.Exists(path) : _iso.Exists(path);

        public byte[] ReadFile(string path) =>
            _useUdf ? _udf.ReadFile(path) : _iso.ReadFile(path);

        public IEnumerable<IsoDirEntry> ListDirectory(string path) =>
            _useUdf ? _udf.ListDirectory(path) : _iso.ListDirectory(path);

        public void Dispose()
        {
            try { _iso?.Dispose(); } catch { }
            try { _udf?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// DiscUtils UDF backend using GetDirectories / GetFiles / GetFileLength
    /// (reliable dir detection and sizes — unlike DiscFileSystemInfo casts).
    /// </summary>
    sealed class UdfBackend : IDisposable
    {
        readonly FileStream _stream;
        readonly UdfReader _udf;
        readonly Dictionary<string, Node> _root =
            new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        public string VolumeId { get; }

        class Node
        {
            public string Name;
            public bool IsDir;
            public long Size;
            public Dictionary<string, Node> Children;
        }

        public UdfBackend(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _udf = new UdfReader(_stream);
            VolumeId = _udf.VolumeLabel;

            BuildTree("", _root, 0);

            if (_root.Count == 0)
                throw new Exception("UDF tree is empty");
        }

        void BuildTree(string udfDirPath, Dictionary<string, Node> parent, int depth)
        {
            if (depth > 24) return;

            // udfDirPath: "" for root, or "sources", or "sources\install"
            string listPath = string.IsNullOrEmpty(udfDirPath) ? "\\" : "\\" + udfDirPath.Trim('\\');

            string[] dirs = null;
            string[] files = null;

            try { dirs = _udf.GetDirectories(listPath); } catch { dirs = new string[0]; }
            try { files = _udf.GetFiles(listPath); } catch { files = new string[0]; }

            if (dirs != null)
            {
                foreach (var fullDir in dirs)
                {
                    string name = Path.GetFileName(fullDir.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(name) || name == "." || name == "..") continue;
                    if (parent.ContainsKey(name)) continue;

                    string childUdf = string.IsNullOrEmpty(udfDirPath)
                        ? name
                        : udfDirPath.TrimEnd('\\') + "\\" + name;

                    var node = new Node
                    {
                        Name = name,
                        IsDir = true,
                        Size = 0,
                        Children = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase)
                    };
                    parent[name] = node;

                    try { BuildTree(childUdf, node.Children, depth + 1); }
                    catch { }
                }
            }

            if (files != null)
            {
                foreach (var fullFile in files)
                {
                    string name = Path.GetFileName(fullFile);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (parent.ContainsKey(name)) continue;

                    long size = 0;
                    try { size = _udf.GetFileLength(fullFile); }
                    catch
                    {
                        try
                        {
                            using (var s = _udf.OpenFile(fullFile, FileMode.Open))
                                size = s.Length;
                        }
                        catch { }
                    }

                    parent[name] = new Node
                    {
                        Name = name,
                        IsDir = false,
                        Size = size
                    };
                }
            }
        }

        public bool Exists(string path)
        {
            if (Find(path) != null) return true;
            string p = ToUdfPath(path);
            try
            {
                return _udf.FileExists(p) || _udf.DirectoryExists(p);
            }
            catch { return false; }
        }

        public byte[] ReadFile(string path)
        {
            try
            {
                string p = ToUdfPath(path);
                if (!_udf.FileExists(p)) return null;
                using (var s = _udf.OpenFile(p, FileMode.Open))
                {
                    if (s.Length <= 0 || s.Length > 64L * 1024 * 1024) return null;
                    byte[] data = new byte[s.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = s.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != data.Length) Array.Resize(ref data, read);
                    return data;
                }
            }
            catch { return null; }
        }

        public IEnumerable<IsoDirEntry> ListDirectory(string path)
        {
            Dictionary<string, Node> dict;
            if (string.IsNullOrEmpty(path) || path == "/" || path == "\\")
                dict = _root;
            else
                dict = Find(path)?.Children;

            if (dict == null) yield break;
            foreach (var kv in dict)
            {
                yield return new IsoDirEntry
                {
                    Name = kv.Key,
                    IsDir = kv.Value.IsDir,
                    Size = kv.Value.Size
                };
            }
        }

        Node Find(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/" || path == "\\")
                return null;

            path = path.Replace('\\', '/').Trim('/');
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, Node> cur = _root;
            Node found = null;

            for (int i = 0; i < parts.Length; i++)
            {
                if (cur == null || !cur.TryGetValue(parts[i], out found))
                    return null;
                if (i < parts.Length - 1)
                    cur = found.Children;
            }
            return found;
        }

        static string ToUdfPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            return "\\" + path.Replace('/', '\\').Trim('\\');
        }

        public void Dispose()
        {
            try { _udf?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
        }
    }
}
