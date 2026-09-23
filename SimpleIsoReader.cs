using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickLook.Plugin.IsoViewer
{
    public sealed class IsoDirEntry
    {
        public string Name { get; set; }
        public bool IsDir { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// ISO9660 reader with Joliet support.
    /// Resilient: one bad directory entry does not abort the whole tree.
    /// </summary>
    public sealed class SimpleIsoReader : IDisposable
    {
        const int SectorSize = 2048;

        readonly FileStream _fs;
        readonly BinaryReader _br;
        readonly Dictionary<string, IsoEntry> _root = new Dictionary<string, IsoEntry>(StringComparer.OrdinalIgnoreCase);
        public string VolumeId { get; private set; }

        class IsoEntry
        {
            public string Name;
            public bool IsDir;
            public long Lba;
            public long Size;
            public Dictionary<string, IsoEntry> Children;
        }

        public SimpleIsoReader(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _br = new BinaryReader(_fs);
            ParseVolumes();
        }

        void ParseVolumes()
        {
            uint rootLba = 0;
            uint rootSize = 0;
            bool foundIso = false;
            bool foundJoliet = false;

            for (int sector = 16; sector < 32; sector++)
            {
                _fs.Position = (long)sector * SectorSize;
                byte[] vd = _br.ReadBytes(SectorSize);
                if (vd.Length < 6) break;

                byte type = vd[0];
                if (vd[1] != 'C' || vd[2] != 'D' || vd[3] != '0' || vd[4] != '0' || vd[5] != '1')
                    break;

                if (type == 255)
                    break;

                if (type == 1 && !foundIso)
                {
                    VolumeId = Encoding.ASCII.GetString(vd, 40, 32).Trim();
                    rootLba = BitConverter.ToUInt32(vd, 158);
                    rootSize = BitConverter.ToUInt32(vd, 166);
                    foundIso = true;
                }
                else if (type == 2)
                {
                    if (vd[88] == 0x25 && vd[89] == 0x2F &&
                        (vd[90] == 0x40 || vd[90] == 0x43 || vd[90] == 0x45))
                    {
                        rootLba = BitConverter.ToUInt32(vd, 158);
                        rootSize = BitConverter.ToUInt32(vd, 166);
                        try
                        {
                            var jolietId = Encoding.BigEndianUnicode.GetString(vd, 40, 32).Trim('\0').Trim();
                            if (!string.IsNullOrEmpty(jolietId))
                                VolumeId = jolietId;
                        }
                        catch { }
                        foundJoliet = true;
                    }
                }
            }

            if (!foundIso && !foundJoliet)
                throw new Exception("Not a valid ISO9660/Joliet image (no volume descriptor found)");

            if (rootLba == 0 || rootSize == 0)
                throw new Exception("Invalid root directory in volume descriptor");

            ParseDirectory(rootLba, rootSize, _root, "", foundJoliet);
        }

        void ParseDirectory(uint lba, uint size, Dictionary<string, IsoEntry> parent, string pathPrefix, bool joliet)
        {
            if (size == 0 || lba == 0) return;

            long start = (long)lba * SectorSize;
            long end = start + size;
            if (start < 0 || start >= _fs.Length) return;
            if (end > _fs.Length) end = _fs.Length;

            long pos = start;
            int depth = pathPrefix.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth > 16) return;

            while (pos < end)
            {
                try
                {
                    _fs.Position = pos;
                    int len = _fs.ReadByte();
                    if (len < 0) break;

                    if (len == 0)
                    {
                        long next = ((pos / SectorSize) + 1) * SectorSize;
                        if (next >= end) break;
                        pos = next;
                        continue;
                    }

                    if (len < 34)
                    {
                        pos += len;
                        continue;
                    }

                    byte[] rec = new byte[len];
                    rec[0] = (byte)len;
                    int got = _fs.Read(rec, 1, len - 1);
                    if (got < len - 1)
                    {
                        pos += len;
                        continue;
                    }

                    int nameLen = rec[32];
                    if (nameLen <= 0 || 33 + nameLen > len)
                    {
                        pos += len;
                        continue;
                    }

                    bool isDir = (rec[25] & 0x02) != 0;
                    uint extent = BitConverter.ToUInt32(rec, 2);
                    uint dataLen = BitConverter.ToUInt32(rec, 10);

                    string name;
                    if (nameLen == 1 && rec[33] == 0) name = ".";
                    else if (nameLen == 1 && rec[33] == 1) name = "..";
                    else if (joliet && nameLen >= 2)
                    {
                        try
                        {
                            name = Encoding.BigEndianUnicode.GetString(rec, 33, nameLen).TrimEnd('\0');
                        }
                        catch
                        {
                            name = Encoding.ASCII.GetString(rec, 33, nameLen);
                        }
                    }
                    else
                    {
                        name = Encoding.ASCII.GetString(rec, 33, nameLen);
                    }

                    int semi = name.IndexOf(';');
                    if (semi >= 0) name = name.Substring(0, semi);
                    name = name.TrimEnd('.').Trim();

                    pos += len;

                    if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                        continue;

                    if (parent.ContainsKey(name))
                        continue;

                    var entry = new IsoEntry
                    {
                        Name = name,
                        IsDir = isDir,
                        Lba = extent,
                        Size = dataLen
                    };

                    parent[name] = entry;

                    if (isDir && dataLen > 0 && extent > 0)
                    {
                        entry.Children = new Dictionary<string, IsoEntry>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            string childPrefix = string.IsNullOrEmpty(pathPrefix) ? name : pathPrefix + "/" + name;
                            ParseDirectory(extent, dataLen, entry.Children, childPrefix, joliet);
                        }
                        catch
                        {
                            // keep folder even if children failed
                        }
                    }
                }
                catch
                {
                    pos = ((pos / SectorSize) + 1) * SectorSize;
                    if (pos >= end) break;
                }
            }
        }

        public bool Exists(string path)
        {
            return Find(path) != null;
        }

        public byte[] ReadFile(string path)
        {
            var e = Find(path);
            if (e == null || e.IsDir || e.Size <= 0 || e.Size > 64L * 1024 * 1024)
                return null;

            try
            {
                long offset = e.Lba * SectorSize;
                if (offset < 0 || offset >= _fs.Length) return null;

                _fs.Position = offset;
                int toRead = (int)Math.Min(e.Size, _fs.Length - offset);
                if (toRead <= 0) return null;

                byte[] data = new byte[toRead];
                int read = 0;
                while (read < toRead)
                {
                    int n = _fs.Read(data, read, toRead - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != data.Length)
                    Array.Resize(ref data, read);
                return data;
            }
            catch
            {
                return null;
            }
        }

        public IEnumerable<IsoDirEntry> ListDirectory(string path)
        {
            Dictionary<string, IsoEntry> dict;
            if (string.IsNullOrEmpty(path) || path == "/" || path == "\\")
            {
                dict = _root;
            }
            else
            {
                var e = Find(path);
                dict = e?.Children;
            }

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

        public void Walk(Action<string, bool, long> visitor, string path = "", int maxDepth = 12)
        {
            var dict = string.IsNullOrEmpty(path) ? _root : Find(path)?.Children;
            WalkInternal(dict, path, 0, maxDepth, visitor);
        }

        void WalkInternal(Dictionary<string, IsoEntry> dir, string prefix, int depth, int maxDepth, Action<string, bool, long> visitor)
        {
            if (dir == null || depth > maxDepth) return;
            foreach (var kv in dir)
            {
                string full = string.IsNullOrEmpty(prefix) ? kv.Key : prefix + "/" + kv.Key;
                visitor(full, kv.Value.IsDir, kv.Value.Size);
                if (kv.Value.IsDir)
                    WalkInternal(kv.Value.Children, full, depth + 1, maxDepth, visitor);
            }
        }

        IsoEntry Find(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/" || path == "\\")
                return null;

            path = path.Replace('\\', '/').Trim('/');
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            Dictionary<string, IsoEntry> cur = _root;
            IsoEntry found = null;

            for (int i = 0; i < parts.Length; i++)
            {
                if (cur == null || !cur.TryGetValue(parts[i], out found))
                    return null;
                if (i < parts.Length - 1)
                    cur = found.Children;
            }
            return found;
        }

        public void Dispose()
        {
            _br?.Dispose();
            _fs?.Dispose();
        }
    }
}
